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

public interface IPaymentGateway
{
    string Name { get; }

    Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MockPaymentGateway : IPaymentGateway
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
}

public sealed class PaymentGatewayResolver(
    IEnumerable<IPaymentGateway> gateways,
    IConfiguration configuration)
{
    public IPaymentGateway? Resolve()
    {
        var configuredProvider = configuration["Payments:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredProvider))
        {
            return null;
        }

        return gateways.FirstOrDefault(x =>
            string.Equals(x.Name, configuredProvider, StringComparison.OrdinalIgnoreCase));
    }
}
