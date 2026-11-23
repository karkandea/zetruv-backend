using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zetruv.Api.Features.Payments;

/// <summary>
/// Midtrans Snap Redirect sandbox adapter. Confirmation is ALWAYS based on an
/// authenticated Core API GET Status, never on a browser redirect or unverified webhook.
/// Supports server keys issued after the October 2026 split of the Signature Secret Key.
/// This version deliberately cannot accept production payments.
/// </summary>
public sealed class MidtransSnapGateway(
    IHttpClientFactory clients, IConfiguration configuration)
    : IPaymentGateway, ILivePaymentChannelGateway
{
    public const string SnapMethod = "MIDTRANS_SNAP";
    private static readonly Uri SnapUrl =
        new("https://app.sandbox.midtrans.com/snap/v1/transactions");
    private static readonly Uri StatusBase =
        new("https://api.sandbox.midtrans.com/");
    private static readonly Regex ReferencePattern =
        new(@"^[a-zA-Z0-9_.~-]{1,50}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "midtrans";
    public bool IsSandbox => true;
    private string ServerKey => configuration["Payments:Midtrans:ServerKey"]?.Trim() ?? "";
    private string SignatureSecret => configuration["Payments:Midtrans:SignatureSecretKey"]?.Trim() ?? "";

    public bool IsLiveConfigured =>
        configuration.GetValue<bool>("Payments:Midtrans:Enabled") &&
        string.Equals(configuration["Payments:Midtrans:Mode"], "Sandbox", StringComparison.OrdinalIgnoreCase) &&
        ServerKey.StartsWith("SB-Mid-server-", StringComparison.Ordinal) &&
        ServerKey.Length > "SB-Mid-server-".Length + 8 &&
        !string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);

    public bool SupportsChannel(string code) =>
        IsLiveConfigured && string.Equals(code, SnapMethod, StringComparison.OrdinalIgnoreCase);

    private HttpClient Client()
    {
        var client = clients.CreateClient("zetruv-midtrans");
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(ServerKey + ":")));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request, CancellationToken ct = default)
    {
        if (!IsLiveConfigured || !SupportsChannel(request.MethodCode ?? ""))
            return PaymentGatewayCreateResult.Failure("Midtrans Sandbox is not configured.");
        if (request.Currency != "IDR" || request.Amount <= 0 ||
            request.Amount != decimal.Truncate(request.Amount) ||
            request.Amount > long.MaxValue)
            return PaymentGatewayCreateResult.Failure("Midtrans requires a positive whole-IDR amount.");

        var reference = request.OrderNumber + "-A" +
                        request.AttemptNumber.ToString(CultureInfo.InvariantCulture);
        if (!ReferencePattern.IsMatch(reference))
            return PaymentGatewayCreateResult.Failure("Payment order reference is not supported.");

        // A fixed start_time makes the expiry authoritative from creation time,
        // including if the shopper has not selected a Snap payment channel.
        var now = DateTimeOffset.UtcNow;
        var start = now.ToOffset(TimeSpan.FromHours(7));
        var startTime = start.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) +
                        "+0700";
        var body = new Dictionary<string, object>
        {
            ["transaction_details"] = new
            {
                order_id = reference,
                gross_amount = (long)request.Amount
            },
            ["expiry"] = new { start_time = startTime, unit = "minutes", duration = 60 },
            ["customer_details"] = new
            {
                first_name = Truncate(request.CustomerName, 100),
                email = Truncate(request.CustomerEmail, 100),
                phone = Truncate(request.CustomerPhone, 30)
            }
        };
        var finishUrl = configuration["Payments:Midtrans:FinishUrl"]?.Trim();
        if (Uri.TryCreate(finishUrl, UriKind.Absolute, out var parsedFinish) &&
            parsedFinish.Scheme == Uri.UriSchemeHttps)
            body["callbacks"] = new { finish = finishUrl };

        try
        {
            using var client = Client();
            using var message = new HttpRequestMessage(HttpMethod.Post, SnapUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions),
                    Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                return PaymentGatewayCreateResult.Failure(
                    response.StatusCode == HttpStatusCode.Forbidden
                        ? "Midtrans server key lacks Snap Checkout write permission."
                        : response.StatusCode == HttpStatusCode.Unauthorized
                            ? "Midtrans Sandbox server key was rejected."
                            : "Midtrans could not create the Snap payment session.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!TryString(json.RootElement, "token", out _) ||
                !TryString(json.RootElement, "redirect_url", out var url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var redirect) ||
                redirect.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(redirect.Host, "app.sandbox.midtrans.com", StringComparison.OrdinalIgnoreCase))
                return PaymentGatewayCreateResult.Failure(
                    "Midtrans returned an invalid sandbox payment redirect.");
            return PaymentGatewayCreateResult.Success(
                reference, paymentUrl: url, expiresAt: now.AddMinutes(60));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PaymentGatewayCreateResult.Failure(
                "Midtrans request timed out. Check the existing order before retrying.");
        }
        catch (HttpRequestException)
        {
            return PaymentGatewayCreateResult.Failure("Midtrans Sandbox is currently unreachable.");
        }
        catch (JsonException)
        {
            return PaymentGatewayCreateResult.Failure("Invalid Midtrans payment response.");
        }
    }

    public Task<PaymentGatewayStatusResult> GetPaymentStatusAsync(
        PaymentGatewayStatusRequest request, CancellationToken ct = default)
    {
        if (!IsLiveConfigured || !ReferencePattern.IsMatch(request.ProviderReference))
            return Task.FromResult(PaymentGatewayStatusResult.Failure(
                "Midtrans Sandbox is not configured.", isConfigurationError: true));
        return FetchAuthoritativeStatusAsync(
            request.ProviderReference, request.Amount, request.Currency, ct);
    }

    public async Task<PaymentWebhookParseResult> ParseWebhookAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (!IsLiveConfigured)
            return PaymentWebhookParseResult.Failure(
                "Midtrans Sandbox is not configured.", isConfigurationError: true);
        if (rawBody.Length > 16_384)
            return PaymentWebhookParseResult.Failure("Webhook payload is too large.");

        try
        {
            using var json = JsonDocument.Parse(rawBody);
            var root = json.RootElement;
            if (!TryString(root, "order_id", out var reference) ||
                !ReferencePattern.IsMatch(reference) ||
                !TryString(root, "status_code", out var statusCode) ||
                !TryString(root, "gross_amount", out var rawAmount) ||
                !decimal.TryParse(rawAmount, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                return PaymentWebhookParseResult.Failure("Webhook evidence is incomplete.");

            // New Midtrans integrations (since Oct 28 2026) can have a distinct
            // Signature Secret Key. If provided, verify it in constant time.
            // Even with a valid signature, never settle on the webhook alone.
            if (SignatureSecret.Length > 0)
            {
                if (!TryString(root, "signature_key", out var supplied) ||
                    !VerifySignature(reference + statusCode + rawAmount + SignatureSecret, supplied))
                    return PaymentWebhookParseResult.Failure("Invalid Midtrans webhook signature.");
            }

            // When Signature Secret Key hasn't been supplied yet, the HTTPS
            // server-to-server status API is authoritative and prevents anyone
            // from spoofing a paid/failed event via a forged webhook.
            var verified = await FetchAuthoritativeStatusAsync(reference, amount, "IDR", ct);
            if (verified.Notification is null)
                return PaymentWebhookParseResult.Failure(
                    verified.Error ?? "Midtrans status could not be independently verified.",
                    verified.IsConfigurationError);
            return PaymentWebhookParseResult.Success(verified.Notification);
        }
        catch (JsonException)
        {
            return PaymentWebhookParseResult.Failure("Webhook JSON is invalid.");
        }
    }

    private async Task<PaymentGatewayStatusResult> FetchAuthoritativeStatusAsync(
        string reference, decimal expectedAmount, string expectedCurrency, CancellationToken ct)
    {
        if (expectedCurrency != "IDR" || expectedAmount <= 0 ||
            !ReferencePattern.IsMatch(reference))
            return PaymentGatewayStatusResult.Failure("Invalid Midtrans status request.");
        try
        {
            using var client = Client();
            using var response = await client.GetAsync(
                new Uri(StatusBase, "v2/" + Uri.EscapeDataString(reference) + "/status"), ct);
            // Snap sometimes returns 404 until the shopper chooses a payment
            // method. That is not a failed transaction; reconciliation retries.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return PaymentGatewayStatusResult.Failure(
                    "Midtrans has no selected payment method for this session yet.");
            if (!response.IsSuccessStatusCode)
                return PaymentGatewayStatusResult.Failure(
                    response.StatusCode == HttpStatusCode.Forbidden
                        ? "Midtrans server key lacks transaction status read permission."
                        : "Midtrans payment status is currently unavailable.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (!TryString(root, "order_id", out var orderId) ||
                !string.Equals(reference, orderId, StringComparison.Ordinal) ||
                !TryString(root, "gross_amount", out var amountText) ||
                !decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount) ||
                amount != expectedAmount ||
                (TryString(root, "currency", out var currency) &&
                 !string.Equals(currency, expectedCurrency, StringComparison.OrdinalIgnoreCase)) ||
                !TryString(root, "transaction_status", out var status))
                return PaymentGatewayStatusResult.Failure(
                    "Midtrans order ID or amount did not match the payment attempt.");

            var statusCode = TryString(root, "status_code", out var code) ? code : "";
            var fraud = TryString(root, "fraud_status", out var fraudText)
                ? fraudText.ToLowerInvariant() : "accept";
            PaymentWebhookStatus mapped;
            switch (status.ToLowerInvariant())
            {
                case "settlement":
                case "capture":
                    mapped = statusCode == "200" && fraud == "accept"
                        ? PaymentWebhookStatus.Paid : PaymentWebhookStatus.Pending;
                    break;
                case "refund":
                    mapped = PaymentWebhookStatus.Refunded;
                    break;
                case "deny":
                case "cancel":
                case "expire":
                case "failure":
                    mapped = PaymentWebhookStatus.Failed;
                    break;
                default:
                    mapped = PaymentWebhookStatus.Pending;
                    break;
            }
            var eventId = TryString(root, "transaction_id", out var transactionId)
                ? transactionId + ":" + status.ToLowerInvariant() : null;
            if (eventId?.Length > 200) eventId = null;
            return PaymentGatewayStatusResult.Success(new PaymentWebhookNotification(
                reference, mapped, amount, expectedCurrency, eventId));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PaymentGatewayStatusResult.Failure("Midtrans status check timed out.");
        }
        catch (HttpRequestException)
        {
            return PaymentGatewayStatusResult.Failure("Midtrans Sandbox status API is unreachable.");
        }
        catch (JsonException)
        {
            return PaymentGatewayStatusResult.Failure("Midtrans status response is invalid.");
        }
    }

    private static bool VerifySignature(string material, string supplied)
    {
        if (supplied.Length != 128) return false;
        byte[] bytes;
        try { bytes = Convert.FromHexString(supplied); }
        catch (FormatException) { return false; }
        var expected = SHA512.HashData(Encoding.UTF8.GetBytes(material));
        return CryptographicOperations.FixedTimeEquals(bytes, expected);
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim()[..Math.Min(value.Trim().Length, max)];

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = "";
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(name, out var node) &&
               node.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = node.GetString() ?? "");
    }
}
