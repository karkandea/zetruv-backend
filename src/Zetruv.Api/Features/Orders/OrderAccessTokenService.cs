using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zetruv.Api.Features.Orders;

public sealed record OrderAccessGrant(
    string Token,
    DateTimeOffset ExpiresAt);

public sealed class OrderAccessTokenService
{
    private const string Version = "v1";
    private readonly byte[] signingKey;
    private readonly TimeSpan lifetime;

    public OrderAccessTokenService(IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]?.Trim();
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must contain at least 32 characters before order access tokens can be issued.");
        }

        signingKey = SHA256.HashData(
            Encoding.UTF8.GetBytes($"Zetruv.OrderAccess.{Version}\0{jwtKey}"));

        var configuredMinutes = configuration.GetValue<int?>(
            "OrderAccess:TokenLifetimeMinutes") ?? 1440;

        if (configuredMinutes is < 5 or > 10080)
        {
            throw new InvalidOperationException(
                "OrderAccess:TokenLifetimeMinutes must be between 5 and 10080 minutes.");
        }

        lifetime = TimeSpan.FromMinutes(configuredMinutes);
    }

    public OrderAccessGrant Issue(Guid orderId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var expiresUnix = expiresAt.ToUnixTimeSeconds();
        var signature = Sign(orderId, expiresUnix);

        return new OrderAccessGrant(
            $"{Version}.{expiresUnix}.{Base64UrlEncode(signature)}",
            expiresAt);
    }

    public bool Validate(Guid orderId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Trim().Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var expiresUnix))
        {
            return false;
        }

        if (expiresUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        var suppliedSignature = Base64UrlDecode(parts[2]);
        if (suppliedSignature is null)
        {
            return false;
        }

        var expectedSignature = Sign(orderId, expiresUnix);
        return suppliedSignature.Length == expectedSignature.Length &&
               CryptographicOperations.FixedTimeEquals(
                   suppliedSignature,
                   expectedSignature);
    }

    private byte[] Sign(Guid orderId, long expiresUnix)
    {
        var payload = $"{Version}:{orderId:N}:{expiresUnix}";
        return HMACSHA256.HashData(
            signingKey,
            Encoding.UTF8.GetBytes(payload));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[]? Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => string.Empty
        };

        if (base64.Length == 0)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
