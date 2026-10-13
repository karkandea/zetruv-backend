using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zetruv.Api.Features.Orders;

namespace Zetruv.Api.Features.Payments;

/// <summary>
/// Xendit Payments API v3 adapter. Disabled until a real test/live key, webhook
/// token, enabled switch and supported channels are configured. There is no
/// mock-success fallback. Payment settlement still goes through PaymentService.
/// </summary>
public sealed class XenditPaymentGateway(
    IHttpClientFactory clients, IConfiguration config) : IPaymentGateway, ILivePaymentChannelGateway
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> ChannelMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["QRIS"] = "QRIS",
            ["GOPAY"] = "GOPAY"
            // BCA VA needs its own payment-code response and UI contract;
            // do not advertise it before that field is wired end-to-end.
        };

    public string Name => "xendit";
    public bool IsSandbox => string.Equals(config["Payments:Xendit:Mode"],
        "Sandbox", StringComparison.OrdinalIgnoreCase);
    private string ApiKey => config["Payments:Xendit:ApiKey"]?.Trim() ?? "";
    private string WebhookToken => config["Payments:Xendit:WebhookToken"]?.Trim() ?? "";

    public bool IsLiveConfigured {
        get {
            if (!config.GetValue<bool>("Payments:Xendit:Enabled") ||
                ApiKey.Length < 25 || WebhookToken.Length < 16) return false;
            var developmentKey = ApiKey.StartsWith("xnd_development_", StringComparison.Ordinal);
            var productionKey = ApiKey.StartsWith("xnd_production_", StringComparison.Ordinal);
            if (IsSandbox) return developmentKey &&
                !string.Equals(config["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);
            return productionKey && string.Equals(config["Payments:Xendit:Mode"],
                "Live", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool SupportsChannel(string code) =>
        IsLiveConfigured && ChannelMap.ContainsKey(code) &&
        config.GetSection("Payments:Xendit:Channels").GetChildren()
            .Any(x => string.Equals(x.Value, code, StringComparison.OrdinalIgnoreCase));

    private HttpClient GetClient() {
        var http = clients.CreateClient("zetruv-xendit");
        http.BaseAddress = new Uri("https://api.xendit.co/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(ApiKey + ":")));
        http.DefaultRequestHeaders.Add("api-version", "2024-11-11");
        return http;
    }

    public async Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request, CancellationToken ct = default)
    {
        if (!IsLiveConfigured || request.MethodCode is null ||
            !SupportsChannel(request.MethodCode))
            return PaymentGatewayCreateResult.Failure("Payment channel is not configured.");
        if (request.Currency != "IDR" || request.Amount <= 0)
            return PaymentGatewayCreateResult.Failure("Invalid payment amount or currency.");
        var code = ChannelMap[request.MethodCode];
        var returnUrl = config["Payments:Xendit:ReturnUrl"]?.Trim();
        if (code == "GOPAY" && (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps))
            return PaymentGatewayCreateResult.Failure("Payment return URL is not configured.");

        var body = new Dictionary<string, object> {
            ["reference_id"] = request.OrderNumber,
            ["type"] = "PAY",
            ["country"] = "ID",
            ["currency"] = "IDR",
            ["request_amount"] = request.Amount,
            ["channel_code"] = code
        };
        if (code == "GOPAY")
            body["channel_properties"] = new { success_return_url = returnUrl };
        try {
            using var http = GetClient();
            using var message = new HttpRequestMessage(HttpMethod.Post, "v3/payment_requests") {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            message.Headers.TryAddWithoutValidation("idempotency-key", request.OrderId.ToString("D"));
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                return PaymentGatewayCreateResult.Failure("Payment provider could not create a payment.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (!TryString(root, "id", out var refId) || !refId.StartsWith("pr-", StringComparison.Ordinal))
                return PaymentGatewayCreateResult.Failure("Payment provider returned an invalid reference.");
            string? url = null, qr = null;
            if (root.TryGetProperty("actions", out var actions) &&
                actions.ValueKind == JsonValueKind.Array) {
                foreach (var action in actions.EnumerateArray()) {
                    if (!TryString(action, "value", out var value)) continue;
                    if (TryString(action, "type", out var type) && type == "REDIRECT_CUSTOMER" &&
                        Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
                        candidate.Scheme == Uri.UriSchemeHttps)
                        url ??= value;
                    else if (type == "PRESENT_TO_CUSTOMER" && code == "QRIS")
                        qr ??= value;
                }
            }
            if (url is null && qr is null)
                return PaymentGatewayCreateResult.Failure("Payment instructions are not yet available.");
            return PaymentGatewayCreateResult.Success(refId, paymentUrl:url, qrString:qr,
                expiresAt:DateTimeOffset.UtcNow.AddMinutes(30));
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return PaymentGatewayCreateResult.Failure("Payment provider timed out; check the existing order before retrying.");
        } catch (HttpRequestException) {
            return PaymentGatewayCreateResult.Failure("Payment provider is temporarily unreachable.");
        } catch (JsonException) {
            return PaymentGatewayCreateResult.Failure("Payment provider returned an unexpected response.");
        }
    }

    public async Task<PaymentGatewayStatusResult> GetPaymentStatusAsync(
        PaymentGatewayStatusRequest request, CancellationToken ct = default)
    {
        if (!IsLiveConfigured || !request.ProviderReference.StartsWith("pr-", StringComparison.Ordinal))
            return PaymentGatewayStatusResult.Failure("Provider is not configured.", isConfigurationError:true);
        try {
            using var http = GetClient();
            using var response = await http.GetAsync("v3/payment_requests/" +
                Uri.EscapeDataString(request.ProviderReference), ct);
            if (!response.IsSuccessStatusCode)
                return PaymentGatewayStatusResult.Failure("Could not obtain payment status.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (!TryString(root,"status",out var status) ||
                !TryString(root,"id",out var providerId) ||
                providerId != request.ProviderReference ||
                !TryString(root,"currency",out var currency) ||
                !string.Equals(currency,request.Currency,StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("request_amount",out var amountNode) ||
                !amountNode.TryGetDecimal(out var amount) || amount != request.Amount)
                return PaymentGatewayStatusResult.Failure("Payment provider status does not match order.");

            var mapped = status switch {
                "SUCCEEDED" => PaymentWebhookStatus.Paid,
                "FAILED" or "EXPIRED" or "CANCELED" => PaymentWebhookStatus.Failed,
                _ => PaymentWebhookStatus.Pending
            };
            return PaymentGatewayStatusResult.Success(new PaymentWebhookNotification(
                request.ProviderReference,mapped,request.Amount,request.Currency));
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return PaymentGatewayStatusResult.Failure("Payment provider timed out.");
        } catch (HttpRequestException) {
            return PaymentGatewayStatusResult.Failure("Payment provider could not be reached.");
        } catch (JsonException) {
            return PaymentGatewayStatusResult.Failure("Payment status response is invalid.");
        }
    }

    public Task<PaymentWebhookParseResult> ParseWebhookAsync(
        string rawBody, IReadOnlyDictionary<string,string> headers, CancellationToken ct = default)
    {
        if (!IsLiveConfigured)
            return Task.FromResult(PaymentWebhookParseResult.Failure("Provider is not configured.",true));
        if (!headers.TryGetValue("x-callback-token",out var supplied) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
                SHA256.HashData(Encoding.UTF8.GetBytes(WebhookToken))))
            return Task.FromResult(PaymentWebhookParseResult.Failure("Webhook token is invalid."));
        try {
            using var json = JsonDocument.Parse(rawBody);
            var root = json.RootElement;
            if (!TryString(root,"event",out var ev) ||
                !root.TryGetProperty("data",out var data))
                return Task.FromResult(PaymentWebhookParseResult.Failure("Webhook is incomplete."));
            var mapped = ev switch {
                "payment.capture" => PaymentWebhookStatus.Paid,
                "payment.failure" or "payment_request.expiry" => PaymentWebhookStatus.Failed,
                _ => (PaymentWebhookStatus?)null
            };
            if (mapped is null ||
                !TryString(data,"payment_request_id",out var refId) ||
                !TryString(data,"currency",out var currency) ||
                !data.TryGetProperty("request_amount",out var rawAmount) ||
                !rawAmount.TryGetDecimal(out var amount) || amount <= 0)
                return Task.FromResult(PaymentWebhookParseResult.Failure("Webhook is missing payment evidence."));
            var eventId = TryString(data,"payment_id",out var id)?id:null;
            if (eventId is {Length: > 200} || refId.Length > 200)
                return Task.FromResult(PaymentWebhookParseResult.Failure("Webhook reference is invalid."));
            return Task.FromResult(PaymentWebhookParseResult.Success(new PaymentWebhookNotification(
                refId,mapped.Value,amount,currency.ToUpperInvariant(),eventId)));
        } catch (JsonException) {
            return Task.FromResult(PaymentWebhookParseResult.Failure("Webhook JSON is invalid."));
        }
    }

    private static bool TryString(JsonElement obj,string name,out string value) {
        value = "";
        return obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(name,out var v) && v.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = v.GetString() ?? "");
    }
}
