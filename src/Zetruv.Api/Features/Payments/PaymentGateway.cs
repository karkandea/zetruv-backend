using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zetruv.Api.Features.Payments;

public sealed record PaymentGatewayCreateRequest(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone);

public sealed record PaymentGatewayCreateResult(
    bool IsSuccess,
    string? ProviderReference,
    string? PaymentUrl,
    string? QrString,
    DateTimeOffset? ExpiresAt,
    string? Error)
{
    public static PaymentGatewayCreateResult Success(
        string providerReference,
        string? paymentUrl = null,
        string? qrString = null,
        DateTimeOffset? expiresAt = null) =>
        new(true, providerReference, paymentUrl, qrString, expiresAt, null);

    public static PaymentGatewayCreateResult Failure(string error) =>
        new(false, null, null, null, null, error);
}

public enum PaymentWebhookStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public sealed record PaymentWebhookNotification(
    string ProviderReference,
    PaymentWebhookStatus Status,
    decimal Amount,
    string Currency);

public sealed record PaymentWebhookParseResult(
    PaymentWebhookNotification? Notification,
    string? Error,
    bool IsConfigurationError = false)
{
    public static PaymentWebhookParseResult Success(PaymentWebhookNotification notification) =>
        new(notification, null);

    public static PaymentWebhookParseResult Failure(
        string error,
        bool isConfigurationError = false) =>
        new(null, error, isConfigurationError);
}

public interface IPaymentGateway
{
    string Name { get; }

    Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentWebhookParseResult> ParseWebhookAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);
}

public sealed class MockPaymentGateway(IConfiguration configuration) : IPaymentGateway
{
    public string Name => "mock";

    public Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var reference = $"MOCK-{request.OrderNumber}-{Guid.NewGuid():N}";
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        return Task.FromResult(PaymentGatewayCreateResult.Success(
            reference,
            paymentUrl: $"mock://payment/{reference}",
            expiresAt: expiresAt));
    }

    public Task<PaymentWebhookParseResult> ParseWebhookAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var secret = configuration["Payments:Mock:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Task.FromResult(PaymentWebhookParseResult.Failure(
                "Mock webhook secret is not configured.",
                isConfigurationError: true));
        }

        if (!headers.TryGetValue("X-Mock-Signature", out var signature) ||
            string.IsNullOrWhiteSpace(signature) ||
            !VerifySignature(rawBody, signature, secret))
        {
            return Task.FromResult(PaymentWebhookParseResult.Failure(
                "Invalid webhook signature."));
        }

        MockWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MockWebhookPayload>(
                rawBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return Task.FromResult(PaymentWebhookParseResult.Failure(
                "Invalid webhook payload."));
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.ProviderReference) ||
            !Enum.TryParse<PaymentWebhookStatus>(payload.Status, true, out var status) ||
            payload.Amount <= 0 ||
            string.IsNullOrWhiteSpace(payload.Currency))
        {
            return Task.FromResult(PaymentWebhookParseResult.Failure(
                "Webhook payload is incomplete or invalid."));
        }

        return Task.FromResult(PaymentWebhookParseResult.Success(
            new PaymentWebhookNotification(
                payload.ProviderReference.Trim(),
                status,
                payload.Amount,
                payload.Currency.Trim().ToUpperInvariant())));
    }

    private static bool VerifySignature(string rawBody, string signature, string secret)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return supplied.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private sealed record MockWebhookPayload(
        string ProviderReference,
        string Status,
        decimal Amount,
        string Currency);
}

public sealed class PaymentGatewayResolver(
    IEnumerable<IPaymentGateway> gateways,
    IConfiguration configuration)
{
    public IPaymentGateway? Resolve()
    {
        var configuredProvider = configuration["Payments:Provider"]?.Trim();
        return string.IsNullOrWhiteSpace(configuredProvider)
            ? null
            : ResolveByName(configuredProvider);
    }

    public IPaymentGateway? ResolveByName(string provider) =>
        gateways.FirstOrDefault(x =>
            string.Equals(x.Name, provider.Trim(), StringComparison.OrdinalIgnoreCase));
}
