using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.GameAccounts;

[Table("game_account_validations")]
[Index(nameof(ProductId))]
[Index(nameof(OrderItemId), IsUnique = true)]
[Index(nameof(ExpiresAt))]
[Index(nameof(Provider), nameof(ProviderReference))]
public sealed class GameAccountValidation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [ForeignKey(nameof(OrderItem))]
    public Guid? OrderItemId { get; set; }
    public OrderItem? OrderItem { get; set; }

    [MaxLength(80)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(180)]
    public string? ProviderReference { get; set; }

    [MaxLength(160)]
    public string? AccountDisplayName { get; set; }

    [Column(TypeName = "jsonb")]
    public string InputJson { get; set; } = "{}";

    [MaxLength(64)]
    public string InputFingerprint { get; set; } = string.Empty;

    public DateTimeOffset ValidatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record GameAccountValidationRequest(
    Guid ProductId,
    [Required] IReadOnlyDictionary<string, string> Fields);

public sealed record GameAccountValidationResponse(
    Guid ValidationId,
    Guid ProductId,
    string Provider,
    string? AccountDisplayName,
    DateTimeOffset ExpiresAt);

public sealed record GameAccountProviderRequest(
    Guid ProductId,
    string ProductName,
    Guid GameId,
    string GameName,
    string GameSlug,
    IReadOnlyDictionary<string, string> Fields);

public sealed record GameAccountProviderResult(
    bool IsValid,
    string? ProviderReference,
    string? AccountDisplayName,
    DateTimeOffset? ExpiresAt,
    string? Error)
{
    public static GameAccountProviderResult Valid(
        string? providerReference = null,
        string? accountDisplayName = null,
        DateTimeOffset? expiresAt = null) =>
        new(true, providerReference, accountDisplayName, expiresAt, null);

    public static GameAccountProviderResult Invalid(string error) =>
        new(false, null, null, null, error);
}

public interface IGameAccountValidator
{
    string Name { get; }

    Task<GameAccountProviderResult> ValidateAsync(
        GameAccountProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MockGameAccountValidator : IGameAccountValidator
{
    public string Name => "mock";

    public Task<GameAccountProviderResult> ValidateAsync(
        GameAccountProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Fields.Count == 0)
        {
            return Task.FromResult(GameAccountProviderResult.Invalid(
                "At least one account field is required."));
        }

        var displayName = TryGetDisplayName(request.Fields);
        var reference = $"MOCK-{request.GameSlug}-{Guid.NewGuid():N}";

        return Task.FromResult(GameAccountProviderResult.Valid(
            reference,
            displayName,
            DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    private static string? TryGetDisplayName(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var key in new[] { "nickname", "username", "accountName", "displayName" })
        {
            var match = fields.FirstOrDefault(x =>
                string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }
}

public sealed class GameAccountValidatorResolver(
    IEnumerable<IGameAccountValidator> validators,
    IConfiguration configuration)
{
    public IGameAccountValidator? Resolve()
    {
        var configuredProvider = configuration["GameAccountValidation:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredProvider))
        {
            return null;
        }

        return validators.FirstOrDefault(x =>
            string.Equals(x.Name, configuredProvider, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record GameAccountValidationResult(
    GameAccountValidationResponse? Validation,
    string? Error,
    bool IsConfigurationError = false)
{
    public static GameAccountValidationResult Success(GameAccountValidationResponse validation) =>
        new(validation, null);

    public static GameAccountValidationResult Failure(
        string error,
        bool isConfigurationError = false) =>
        new(null, error, isConfigurationError);
}

public sealed class GameAccountValidationService(
    ZetruvDbContext db,
    GameAccountValidatorResolver resolver)
{
    private static readonly HashSet<string> SensitiveFieldNames = new(
        new[]
        {
            "password",
            "pass",
            "passwd",
            "otp",
            "pin",
            "token",
            "secret",
            "credential",
            "credentials",
            "cookie",
            "session",
            "sessionid",
            "session_id"
        },
        StringComparer.OrdinalIgnoreCase);

    public async Task<GameAccountValidationResult> ValidateAsync(
        GameAccountValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var fieldsResult = NormalizeFields(request.Fields);
        if (fieldsResult.Error is not null)
        {
            return GameAccountValidationResult.Failure(fieldsResult.Error);
        }

        var product = await db.Products
            .AsNoTracking()
            .Where(x => x.Id == request.ProductId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IsActive,
                x.RequiresGameAccountValidation,
                CategoryIsActive = x.Category.IsActive,
                GameId = x.GameId,
                GameName = x.Game == null ? null : x.Game.Name,
                GameSlug = x.Game == null ? null : x.Game.Slug,
                GameIsActive = x.Game != null && x.Game.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null || !product.IsActive || !product.CategoryIsActive)
        {
            return GameAccountValidationResult.Failure("Product is not available.");
        }

        if (!product.RequiresGameAccountValidation)
        {
            return GameAccountValidationResult.Failure(
                "This product does not require game account validation.");
        }

        if (!product.GameId.HasValue ||
            string.IsNullOrWhiteSpace(product.GameName) ||
            string.IsNullOrWhiteSpace(product.GameSlug) ||
            !product.GameIsActive)
        {
            return GameAccountValidationResult.Failure(
                "This product does not have an active game configured for account validation.");
        }

        var validator = resolver.Resolve();
        if (validator is null)
        {
            return GameAccountValidationResult.Failure(
                "Game account validation provider is not configured.",
                isConfigurationError: true);
        }

        var providerResult = await validator.ValidateAsync(
            new GameAccountProviderRequest(
                product.Id,
                product.Name,
                product.GameId.Value,
                product.GameName,
                product.GameSlug,
                fieldsResult.Fields!),
            cancellationToken);

        if (!providerResult.IsValid)
        {
            return GameAccountValidationResult.Failure(
                providerResult.Error ?? "Game account could not be validated.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = providerResult.ExpiresAt ?? now.AddMinutes(10);
        if (expiresAt <= now)
        {
            return GameAccountValidationResult.Failure(
                "Game account validation provider returned an expired result.");
        }

        var inputJson = JsonSerializer.Serialize(fieldsResult.Fields);
        var validation = new GameAccountValidation
        {
            ProductId = product.Id,
            Provider = validator.Name,
            ProviderReference = Clean(providerResult.ProviderReference),
            AccountDisplayName = Clean(providerResult.AccountDisplayName),
            InputJson = inputJson,
            InputFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(inputJson))),
            ValidatedAt = now,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        db.Set<GameAccountValidation>().Add(validation);
        await db.SaveChangesAsync(cancellationToken);

        return GameAccountValidationResult.Success(
            new GameAccountValidationResponse(
                validation.Id,
                validation.ProductId,
                validation.Provider,
                validation.AccountDisplayName,
                validation.ExpiresAt));
    }

    private static (IReadOnlyDictionary<string, string>? Fields, string? Error) NormalizeFields(
        IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return (null, "At least one account field is required.");
        }

        if (fields.Count > 10)
        {
            return (null, "Game account validation supports up to 10 fields.");
        }

        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            var key = pair.Key?.Trim();
            var value = pair.Value?.Trim();

            if (string.IsNullOrWhiteSpace(key) || key.Length > 60)
            {
                return (null, "Account field names must contain 1 to 60 characters.");
            }

            if (SensitiveFieldNames.Contains(key))
            {
                return (null, $"Sensitive field '{key}' is not accepted by this validation endpoint.");
            }

            if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            {
                return (null, $"Account field '{key}' must contain 1 to 200 characters.");
            }

            normalized[key] = value;
        }

        return (normalized, null);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[ApiController]
[Route("api/v1/game-account")]
public sealed class GameAccountValidationController(
    GameAccountValidationService validationService) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<ActionResult<GameAccountValidationResponse>> Validate(
        GameAccountValidationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await validationService.ValidateAsync(request, cancellationToken);
        if (result.Validation is not null)
        {
            return Ok(result.Validation);
        }

        if (result.IsConfigurationError)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = result.Error });
        }

        return BadRequest(new { message = result.Error });
    }
}
