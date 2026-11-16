using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zetruv.Api.Features.Payments;

public sealed class MidtransPaymentGateway(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IHostEnvironment environment) : IPaymentGateway, ILivePaymentChannelGateway
{
    public const string ProviderCode = "midtrans";
    public const string HttpClientName = "zetruv-midtrans";
    private const int DurationMinutes = 15;
    private const string Config = "Payments:Midtrans:";
    private const string SandboxPrefix = "ZTS-";
    private const string LivePrefix = "ZTR-";
    private static readonly TimeSpan StatusGracePeriod = TimeSpan.FromMinutes(1);
    private static readonly IReadOnlyDictionary<string, string> Channels = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["QRIS"] = "other_qris",
        ["GOPAY"] = "gopay",
        ["BCA_VA"] = "bca_va"
    };

    public string Name => ProviderCode;
    public bool IsSandbox => string.Equals(configuration[Config + "Mode"], "Sandbox", StringComparison.OrdinalIgnoreCase);
    public bool MerchantAbsorbsFees => configuration.GetValue<bool>(Config + "MerchantAbsorbsFees");
    private string ServerKey => configuration[Config + "ServerKey"]?.Trim() ?? string.Empty;
    private string CheckoutHost => IsSandbox ? "app.sandbox.midtrans.com" : "app.midtrans.com";
    private string ApiHost => IsSandbox ? "api.sandbox.midtrans.com" : "api.midtrans.com";

    public bool IsLiveConfigured => configuration.GetValue<bool>(Config + "Enabled") &&
        MerchantAbsorbsFees && ServerKey.Length >= 20 &&
        Uri.TryCreate(configuration[Config + "ReturnUrl"], UriKind.Absolute, out var returnUrl) &&
        returnUrl.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(returnUrl.UserInfo) &&
        (IsSandbox
            ? !environment.IsProduction() && ServerKey.StartsWith("SB-Mid-server-", StringComparison.Ordinal)
            : string.Equals(configuration[Config + "Mode"], "Live", StringComparison.OrdinalIgnoreCase) &&
              environment.IsProduction() && ServerKey.StartsWith("Mid-server-", StringComparison.Ordinal));

    public bool SupportsChannel(string code) => IsLiveConfigured && Channels.ContainsKey(code) &&
        configuration.GetSection(Config + "Channels").GetChildren()
            .Any(x => string.Equals(x.Value?.Trim(), code, StringComparison.OrdinalIgnoreCase));

    public bool OwnsReference(string reference) => reference.Length is >= 39 and <= 50 &&
        reference.StartsWith(IsSandbox ? SandboxPrefix : LivePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(reference.AsSpan(4, 32), "N", out _) &&
        reference.AsSpan(36, 2).SequenceEqual("-A") &&
        reference[38..].All(char.IsAsciiDigit);

    public async Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.MethodCode is null || !SupportsChannel(request.MethodCode))
            return PaymentGatewayCreateResult.Failure("Midtrans payment channel is unavailable.");
        if (request.Currency != "IDR" || request.Amount <= 0 || request.Amount != decimal.Truncate(request.Amount))
            return PaymentGatewayCreateResult.Failure("Midtrans requires a positive amount in whole rupiah.");

        var orderId = (IsSandbox ? SandboxPrefix : LivePrefix) +
            $"{request.OrderId:N}-A{request.AttemptNumber.ToString(CultureInfo.InvariantCulture)}";
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var startTime = now.ToOffset(TimeSpan.FromHours(7))
            .ToString("yyyy-MM-dd HH:mm:ss '+0700'", CultureInfo.InvariantCulture);
        var payload = new
        {
            transaction_details = new { order_id = orderId, gross_amount = request.Amount },
            enabled_payments = new[] { Channels[request.MethodCode] },
            callbacks = new { finish = configuration[Config + "ReturnUrl"]!.Trim() },
            expiry = new { start_time = startTime, duration = DurationMinutes, unit = "minute" }
        };

        try
        {
            using var http = CreateClient(CheckoutHost);
            using var response = await http.PostAsJsonAsync("snap/v1/transactions", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return PaymentGatewayCreateResult.Failure("Midtrans could not create a payment session.");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            if (!Uri.TryCreate(Read(root, "redirect_url"), UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || url.Host != CheckoutHost || !url.IsDefaultPort ||
                !string.IsNullOrEmpty(url.UserInfo) || !url.AbsolutePath.StartsWith("/snap/", StringComparison.Ordinal))
                return PaymentGatewayCreateResult.Failure("Midtrans returned invalid payment instructions.");

            return PaymentGatewayCreateResult.Success(orderId, paymentUrl: url.AbsoluteUri,
                expiresAt: now.AddMinutes(DurationMinutes));
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            return PaymentGatewayCreateResult.Failure("Midtrans could not confirm the payment session. Try again on this order.");
        }
    }

    public async Task<PaymentGatewayStatusResult> GetPaymentStatusAsync(
        PaymentGatewayStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsLiveConfigured || !OwnsReference(request.ProviderReference))
            return PaymentGatewayStatusResult.Failure("Midtrans mode or reference is unavailable.", isConfigurationError: true);

        return await QueryStatusAsync(request.ProviderReference, request, cancellationToken);
    }

    public async Task<PaymentWebhookParseResult> ParseWebhookAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken = default)
    {
        if (!IsLiveConfigured)
            return PaymentWebhookParseResult.Failure("Midtrans is unavailable.", isConfigurationError: true);

        string? reference;
        try
        {
            using var json = JsonDocument.Parse(rawBody);
            var root = json.RootElement;
            reference = Read(root, "order_id");
            var code = Read(root, "status_code");
            var amount = Read(root, "gross_amount");
            var signature = Read(root, "signature_key");
            if (reference is null || !OwnsReference(reference) || code is null || amount is null ||
                signature is not { Length: 128 } || !signature.All(Uri.IsHexDigit))
                return PaymentWebhookParseResult.Failure("Invalid Midtrans notification.");

            var expected = SHA512.HashData(Encoding.UTF8.GetBytes(reference + code + amount + ServerKey));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), expected))
                return PaymentWebhookParseResult.Failure("Invalid Midtrans signature.");
        }
        catch (JsonException)
        {
            return PaymentWebhookParseResult.Failure("Invalid Midtrans notification.");
        }

        // The signature does not cover transaction_status or fraud_status. Use the authenticated status API.
        var status = await QueryStatusAsync(reference, null, cancellationToken);
        return status.Notification is { } notification
            ? PaymentWebhookParseResult.Success(notification)
            : PaymentWebhookParseResult.Failure(status.Error ?? "Midtrans status could not be confirmed.",
                isConfigurationError: true);
    }

    private async Task<PaymentGatewayStatusResult> QueryStatusAsync(
        string reference, PaymentGatewayStatusRequest? request, CancellationToken cancellationToken)
    {
        var finalCheck = request?.ExpiresAt is { } expiry && DateTimeOffset.UtcNow >= expiry + StatusGracePeriod;
        try
        {
            using var http = CreateClient(ApiHost);
            using var response = await http.GetAsync($"v2/{Uri.EscapeDataString(reference)}/status", cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                return PaymentGatewayStatusResult.Failure("Could not obtain Midtrans payment status.");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            var code = Read(root, "status_code");
            if (code == "404" && request is not null)
                return MissingSnapStatus(request, finalCheck);
            if (!response.IsSuccessStatusCode)
                return PaymentGatewayStatusResult.Failure("Could not obtain Midtrans payment status.");
            var currency = Read(root, "currency") ?? "IDR";
            if (Read(root, "order_id") != reference || currency != "IDR" ||
                !decimal.TryParse(Read(root, "gross_amount"), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount) || amount <= 0 ||
                Read(root, "transaction_id") is not { Length: > 0 and <= 100 } transactionId)
                return PaymentGatewayStatusResult.Failure("Midtrans returned inconsistent payment details.");

            var fraud = Read(root, "fraud_status");
            var status = Read(root, "transaction_status") switch
            {
                "settlement" or "capture" when code == "200" && (fraud is null or "accept") => PaymentWebhookStatus.Paid,
                "settlement" or "capture" or "pending" or "authorize" => PaymentWebhookStatus.Pending,
                "expire" or "cancel" or "deny" or "failure" => finalCheck ? PaymentWebhookStatus.Failed : PaymentWebhookStatus.Pending,
                "refund" => PaymentWebhookStatus.Refunded,
                _ => (PaymentWebhookStatus?)null
            };
            return status.HasValue
                ? PaymentGatewayStatusResult.Success(new(reference, status.Value, amount, currency,
                    $"{reference}:{transactionId}:{status}"))
                : PaymentGatewayStatusResult.Failure("Midtrans returned an unsupported transaction status.");
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            return PaymentGatewayStatusResult.Failure("Midtrans payment status is temporarily unavailable.");
        }
    }

    private HttpClient CreateClient(string host)
    {
        var client = clients.CreateClient(HttpClientName);
        client.BaseAddress = new Uri($"https://{host}/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(ServerKey + ":")));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static PaymentGatewayStatusResult MissingSnapStatus(PaymentGatewayStatusRequest request, bool finalCheck) =>
        PaymentGatewayStatusResult.Success(new(request.ProviderReference,
            finalCheck ? PaymentWebhookStatus.Failed : PaymentWebhookStatus.Pending, request.Amount, request.Currency));

    private static string? Read(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var element) &&
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        exception is HttpRequestException or OperationCanceledException or JsonException;
}
