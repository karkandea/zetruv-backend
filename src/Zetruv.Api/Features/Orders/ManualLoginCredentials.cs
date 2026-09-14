using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

[Index(nameof(OrderItemId), IsUnique = true)]
public sealed class ManualLoginCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public string? EncryptedPayload { get; set; }
    public string FieldNamesJson { get; set; } = "[]";
    public int RevealCount { get; set; }
    public DateTimeOffset? LastRevealedAt { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ManualLoginCredentialRevealResponse(
    Guid OrderId,
    Guid OrderItemId,
    IReadOnlyDictionary<string, string> Fields,
    int RevealCount,
    DateTimeOffset RevealedAt);

public sealed record ManualLoginCredentialRevealResult(
    ManualLoginCredentialRevealResponse? Credentials,
    string? Error,
    bool NotFound = false,
    bool Conflict = false,
    bool Gone = false);

public sealed record ManualLoginCredentialNormalizationResult(
    IReadOnlyDictionary<string, string>? Fields,
    string? Error);

public sealed class ManualLoginCredentialProtector(IConfiguration configuration)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[]? key = ParseKey(configuration["ManualLogin:EncryptionKey"]);

    public bool IsConfigured => key is not null;

    public string Protect(Guid orderItemId, IReadOnlyDictionary<string, string> fields)
    {
        if (key is null)
        {
            throw new InvalidOperationException("Manual login credential encryption is not configured.");
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(fields);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, orderItemId.ToByteArray());

        var envelope = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        envelope[0] = 1;
        Buffer.BlockCopy(nonce, 0, envelope, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, envelope, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, envelope, 1 + NonceSize + TagSize, ciphertext.Length);
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String(envelope);
    }

    public IReadOnlyDictionary<string, string> Unprotect(Guid orderItemId, string protectedPayload)
    {
        if (key is null)
        {
            throw new InvalidOperationException("Manual login credential encryption is not configured.");
        }

        var envelope = Convert.FromBase64String(protectedPayload);
        if (envelope.Length <= 1 + NonceSize + TagSize || envelope[0] != 1)
        {
            throw new CryptographicException("Unsupported credential payload.");
        }

        var nonce = envelope.AsSpan(1, NonceSize);
        var tag = envelope.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = envelope.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, orderItemId.ToByteArray());

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
                ?? throw new CryptographicException("Credential payload is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool IsValidConfiguredKey(string? value)
    {
        try
        {
            return ParseKey(value) is not null;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[]? ParseKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = Convert.FromBase64String(value.Trim());
        if (decoded.Length != 32)
        {
            throw new FormatException("ManualLogin:EncryptionKey must be a base64-encoded 32-byte key.");
        }

        return decoded;
    }
}

public sealed class ManualLoginCredentialService(
    ZetruvDbContext db,
    ManualLoginCredentialProtector protector,
    IConfiguration configuration)
{
    private readonly int retentionHours = Math.Clamp(
        configuration.GetValue<int?>("ManualLogin:RetentionHours") ?? 24,
        1,
        168);
    private static readonly HashSet<string> BlockedOneTimeSecretFields = new(
        ["otp", "2fa", "totp", "verificationcode", "verification_code", "cookie", "session", "sessionid", "session_id", "token"],
        StringComparer.OrdinalIgnoreCase);

    public ManualLoginCredentialNormalizationResult Normalize(
        IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return new(null, "Login credentials are required for MANUAL_LOGIN products.");
        }

        if (fields.Count > 20)
        {
            return new(null, "Login credentials support at most 20 fields.");
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in fields)
        {
            var fieldName = pair.Key?.Trim() ?? string.Empty;
            if (fieldName.Length is < 1 or > 80 ||
                fieldName.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            {
                return new(null, "Credential field names must use letters, numbers, dot, dash, or underscore and be at most 80 characters.");
            }

            if (BlockedOneTimeSecretFields.Contains(fieldName))
            {
                return new(null, $"One-time/session credential field '{fieldName}' is not accepted at checkout.");
            }

            if (normalized.ContainsKey(fieldName))
            {
                return new(null, $"Duplicate credential field '{fieldName}'.");
            }

            var value = pair.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1000)
            {
                return new(null, $"Credential field '{fieldName}' must contain 1-1000 characters.");
            }

            normalized[fieldName] = value;
        }

        var ordered = normalized
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        if (JsonSerializer.SerializeToUtf8Bytes(ordered).Length > 16 * 1024)
        {
            return new(null, "Login credential payload is too large.");
        }

        return new(ordered, null);
    }

    public ManualLoginCredential Create(
        Guid orderItemId,
        IReadOnlyDictionary<string, string> fields,
        DateTimeOffset now)
    {
        if (!protector.IsConfigured)
        {
            throw new InvalidOperationException("Manual login credential encryption is not configured.");
        }

        return new ManualLoginCredential
        {
            OrderItemId = orderItemId,
            EncryptedPayload = protector.Protect(orderItemId, fields),
            FieldNamesJson = JsonSerializer.Serialize(fields.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            ExpiresAt = now.AddHours(retentionHours),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<ManualLoginCredentialRevealResult> RevealAsync(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken = default)
    {
        var credential = await db.Set<ManualLoginCredential>()
            .Include(x => x.OrderItem)
                .ThenInclude(x => x.Order)
            .SingleOrDefaultAsync(
                x => x.OrderItemId == orderItemId && x.OrderItem.OrderId == orderId,
                cancellationToken);

        if (credential is null || credential.OrderItem.FulfillmentMethod != FulfillmentMethod.MANUAL_LOGIN)
        {
            return new(null, null, NotFound: true);
        }

        if (credential.OrderItem.Order.PaymentStatus != PaymentStatus.Paid)
        {
            return new(null, "Credentials can only be revealed after the order is paid.", Conflict: true);
        }

        var now = DateTimeOffset.UtcNow;
        if (credential.ExpiresAt <= now)
        {
            Clear(credential, now);
            await db.SaveChangesAsync(cancellationToken);
            return new(null, "Credentials expired and were cleared.", Gone: true);
        }

        if (string.IsNullOrWhiteSpace(credential.EncryptedPayload))
        {
            return new(null, "Credentials were cleared after fulfillment or cancellation.", Gone: true);
        }

        var fields = protector.Unprotect(orderItemId, credential.EncryptedPayload);
        credential.RevealCount++;
        credential.LastRevealedAt = now;
        credential.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return new(
            new ManualLoginCredentialRevealResponse(
                orderId,
                orderItemId,
                fields,
                credential.RevealCount,
                now),
            null);
    }

    public async Task<int> ClearExpiredAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Set<ManualLoginCredential>()
            .Where(x => x.EncryptedPayload != null && x.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EncryptedPayload, (string?)null)
                .SetProperty(x => x.ClearedAt, now)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);
    }

    public static void Clear(ManualLoginCredential? credential, DateTimeOffset now)
    {
        if (credential is null || credential.EncryptedPayload is null)
        {
            return;
        }

        credential.EncryptedPayload = null;
        credential.ClearedAt = now;
        credential.UpdatedAt = now;
    }

    public static IReadOnlyList<string> ParseFieldNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}


public sealed class ManualLoginCredentialCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ManualLoginCredentialCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ManualLoginCredentialService>();
                await service.ClearExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to clear expired manual login credentials.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
