using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zetruv.Api.Features.Payments;

const string referencePrefix = "ZTR-SYNTHETIC";
var providerId = "pr-11111111-2222-4333-8444-555555555555";
var expires = DateTimeOffset.UtcNow.AddMinutes(12);
var webhookToken = "synthetic-callback-token-for-contract-test";
var config = new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> {
        ["Payments:Xendit:Enabled"] = "true",
        ["Payments:Xendit:Mode"] = "Sandbox",
        ["Payments:Xendit:ApiKey"] = "xnd_development_synthetic-only-not-real-123456789",
        ["Payments:Xendit:WebhookToken"] = webhookToken,
        ["Payments:Xendit:Channels:0"] = "QRIS",
        ["Payments:Xendit:Channels:1"] = "GOPAY",
        ["Payments:Xendit:ReturnUrl"] = "https://dev.zetruv.com/payment"
    }).Build();

int checks = 0;
void Check(bool result, string message) {
    if (!result) throw new InvalidOperationException("FAIL: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

var replies = new FakeXenditHandler();
var gateway = new XenditPaymentGateway(new FakeClientFactory(replies), config);
Check(gateway.IsSandbox && gateway.IsLiveConfigured, "valid synthetic sandbox configuration");
Check(gateway.SupportsChannel("QRIS") && gateway.SupportsChannel("GOPAY") &&
    !gateway.SupportsChannel("BCA_VA"), "channel allowlist rejects unsupported VA");
var orderId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
var request = new PaymentGatewayCreateRequest(orderId, referencePrefix, 12345m, "IDR", 
    "Synthetic Tester", null, null, "QRIS", 2);

replies.OnRequest = async req => {
    Check(req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == "/v3/payment_requests",
        "v3 POST path");
    Check(req.Headers.TryGetValues("idempotency-key", out var ids) &&
        ids.Single() == orderId.ToString("D") + "-A2", "attempt-scoped idempotency key");
    using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync());
    Check(body.RootElement.GetProperty("reference_id").GetString() == referencePrefix + "-A2",
        "retry reference remains unique per attempt");
    Check(body.RootElement.GetProperty("channel_code").GetString() == "QRIS",
        "QRIS channel payload");
    Check(body.RootElement.GetProperty("request_amount").GetDecimal() == 12345m,
        "server-quoted total sent unchanged");
    return Json(201, new {
        payment_request_id = providerId, reference_id = referencePrefix + "-A2",
        channel_properties = new { expires_at = expires.ToString("O") },
        actions = new [] { new { type = "PRESENT_TO_CUSTOMER", descriptor = "QR_STRING",
            value = "SYNTHETIC_QR_NOT_FOR_PAYMENT" } }
    });
};
var created = await gateway.CreatePaymentAsync(request);
Check(created.IsSuccess && created.ProviderReference == providerId,
    "v3 payment_request_id parsed");
Check(created.QrString == "SYNTHETIC_QR_NOT_FOR_PAYMENT" &&
    created.PaymentUrl is null, "provider-only QR payload");
Check(created.ExpiresAt == expires, "provider expiry used without invented countdown");

replies.OnRequest = req => Task.FromResult(Json(201, new {
    payment_request_id = providerId, reference_id = referencePrefix + "-A3",
    actions = new [] { new { type = "REDIRECT_CUSTOMER", descriptor = "WEB_URL",
        value = "https://checkout.xendit.co/synthetic" } }
}));
var gopay = await gateway.CreatePaymentAsync(request with { MethodCode = "GOPAY", AttemptNumber = 3 });
Check(gopay.IsSuccess && gopay.PaymentUrl == "https://checkout.xendit.co/synthetic" &&
    gopay.QrString is null, "GoPay provider HTTPS redirect");
Check(gopay.ExpiresAt is null, "no fabricated expiry when omitted by provider");

replies.OnRequest = req => Task.FromResult(Json(201, new {
    payment_request_id = providerId, reference_id = "wrong-order",
    actions = new [] { new { type = "PRESENT_TO_CUSTOMER", descriptor = "QR_STRING",
        value = "SYNTHETIC_QR" } }
}));
var mismatch = await gateway.CreatePaymentAsync(request);
Check(!mismatch.IsSuccess, "mismatched provider reference rejected");

replies.OnRequest = req => Task.FromResult(Json(200, new {
    payment_request_id = providerId,
    currency = "IDR", request_amount = 12345m, status = "SUCCEEDED"
}));
var status = await gateway.GetPaymentStatusAsync(
    new PaymentGatewayStatusRequest(orderId, referencePrefix, providerId, 12345m, "IDR"));
Check(status.Notification?.Status == PaymentWebhookStatus.Paid, "verified provider GET maps success");

replies.OnRequest = req => Task.FromResult(Json(200, new {
    payment_request_id = providerId,
    currency = "IDR", request_amount = 99999m, status = "SUCCEEDED"
}));
var wrongAmount = await gateway.GetPaymentStatusAsync(
    new PaymentGatewayStatusRequest(orderId, referencePrefix, providerId, 12345m, "IDR"));
Check(wrongAmount.Notification is null, "provider amount mismatch rejected");

var payload = JsonSerializer.Serialize(new {
    @event = "payment.capture",
    data = new { payment_id = "py-synthetic-111", payment_request_id = providerId,
        currency = "IDR", request_amount = 12345m }
});
var invalid = await gateway.ParseWebhookAsync(payload, new Dictionary<string,string> {
    ["x-callback-token"] = "bad"
});
Check(invalid.Notification is null, "webhook with wrong token rejected");
var valid = await gateway.ParseWebhookAsync(payload, new Dictionary<string,string> {
    ["x-callback-token"] = webhookToken
});
Check(valid.Notification?.ProviderReference == providerId &&
    valid.Notification.Status == PaymentWebhookStatus.Paid, "authorized synthetic callback parsed");

Console.WriteLine($"PASS: {checks} adapter contract assertions (synthetic only, no provider calls or payments).");

static HttpResponseMessage Json(int status, object value) => new((HttpStatusCode)status) {
    Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
};

sealed class FakeClientFactory(FakeXenditHandler handler) : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new(handler, disposeHandler:false);
}
sealed class FakeXenditHandler : HttpMessageHandler {
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> OnRequest { get; set; } =
        _ => throw new InvalidOperationException("No synthetic response configured");
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
        OnRequest(req);
}
