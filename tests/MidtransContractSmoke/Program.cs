using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zetruv.Api.Features.Payments;

var key = "SB-Mid-server-synthetic-test-key-no-network";
var secret = "synthetic-signature-secret-separate-from-server-key";
var settings = new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> {
        ["Payments:Midtrans:Enabled"] = "true",
        ["Payments:Midtrans:Mode"] = "Sandbox",
        ["Payments:Midtrans:ServerKey"] = key,
        ["Payments:Midtrans:SignatureSecretKey"] = secret,
        ["Payments:Midtrans:FinishUrl"] = "https://dev.zetruv.com/payment",
        ["ASPNETCORE_ENVIRONMENT"] = "Development"
    }).Build();

var handler = new FakeHandler();
var gateway = new MidtransSnapGateway(new Factory(handler), settings);
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
Check(gateway.IsSandbox && gateway.IsLiveConfigured, "sandbox-only configuration");
Check(gateway.SupportsChannel("MIDTRANS_SNAP") && !gateway.SupportsChannel("QRIS"),
    "only redirect method is advertised");

Check(gateway.SupportsChannel("MIDTRANS_OTHER_QRIS") && !gateway.SupportsChannel("MIDTRANS_UNKNOWN"), "strict channel allowlist");
var request = new PaymentGatewayCreateRequest(
    Guid.NewGuid(), "ZTR-SYNTHETIC", 25000m, "IDR", "Test", "sandbox@example.test",
    "081234567890", MidtransSnapGateway.SnapMethod, 2);
handler.Reply = async req =>
{
    Check(req.Method == HttpMethod.Post &&
        req.RequestUri?.AbsoluteUri == "https://app.sandbox.midtrans.com/snap/v1/transactions",
        "correct Snap create endpoint");
    Check(req.Headers.Authorization?.Scheme == "Basic" &&
        req.Headers.Authorization.Parameter ==
        Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":")),
        "server-only Basic authorization");
    using var json = JsonDocument.Parse(await req.Content!.ReadAsStringAsync());
    Check(json.RootElement.GetProperty("transaction_details").GetProperty("order_id").GetString() ==
        "ZTR-SYNTHETIC-A2", "attempt reference unique");
    Check(json.RootElement.GetProperty("transaction_details").GetProperty("gross_amount").GetInt64() ==
        25000, "server amount exact in integer IDR");
    Check(json.RootElement.GetProperty("expiry").GetProperty("duration").GetInt32() == 60,
        "bounded provider expiry");
    return Json(201, new { token="synthetic-token",
        redirect_url="https://app.sandbox.midtrans.com/snap/v3/redirection/TEST" });
};
var started = await gateway.CreatePaymentAsync(request);
Check(started.IsSuccess && started.ProviderReference == "ZTR-SYNTHETIC-A2" &&
    started.PaymentUrl?.StartsWith("https://app.sandbox.midtrans.com/") == true,
    "Snap redirect and reference accepted");

handler.Reply = req => Task.FromResult(Json(201, new {
    token="synthetic",redirect_url="https://evil.example/redirect" }));
var badRedirect = await gateway.CreatePaymentAsync(request);
Check(!badRedirect.IsSuccess, "untrusted redirect rejected");

var calls = 0;
handler.Reply = async req => {
    calls++;
    if (req.Method == HttpMethod.Get) return Json(200, new { payment_channels = new[] {
        new { name="other_qris", enabled=true }, new { name="bca_va", enabled=false },
        new { name="unknown", enabled=true } } });
    using var json = JsonDocument.Parse(await req.Content!.ReadAsStringAsync());
    var chosen = json.RootElement.GetProperty("enabled_payments");
    Check(chosen.GetArrayLength() == 1 && chosen[0].GetString() == "other_qris", "exactly one selected Snap channel");
    return Json(201, new { token="synthetic", redirect_url="https://app.sandbox.midtrans.com/snap/v3/redirection/TEST" });
};
var enabled = await gateway.GetEnabledChannelsAsync(default);
Check(enabled.Count == 1 && enabled[0].Code == "MIDTRANS_OTHER_QRIS", "only allowlisted enabled preference channels");
Check((await gateway.CreatePaymentAsync(request with { MethodCode = "MIDTRANS_OTHER_QRIS" })).IsSuccess, "selected QRIS session created");
var beforeDisabled = calls;
Check(!(await gateway.CreatePaymentAsync(request with { MethodCode = "MIDTRANS_BCA_VA" })).IsSuccess && calls == beforeDisabled+1, "disabled channel never creates session");
handler.Reply = _ => Task.FromResult(Json(503, new { }));
Check((await gateway.GetEnabledChannelsAsync(default)).Count == 0, "preference outage fails closed");

var reference = "ZTR-SYNTHETIC-A2";
var statusRequest = new PaymentGatewayStatusRequest(
    request.OrderId, request.OrderNumber, reference, 25000m, "IDR");
handler.Reply = req => {
    Check(req.Method == HttpMethod.Get &&
        req.RequestUri?.AbsoluteUri ==
            "https://api.sandbox.midtrans.com/v2/ZTR-SYNTHETIC-A2/status",
        "authenticated status path");
    return Task.FromResult(Json(200, new {
        order_id=reference, gross_amount="25000.00",currency="IDR",
        status_code="200",transaction_status="settlement",fraud_status="accept",
        transaction_id="synthetic-transaction"
    }));
};
var paid = await gateway.GetPaymentStatusAsync(statusRequest);
Check(paid.Notification?.Status == PaymentWebhookStatus.Paid &&
    paid.Notification.Amount == 25000m, "provider GET confirms settlement");

handler.Reply = req => Task.FromResult(Json(200, new {
    order_id=reference, gross_amount="25100.00",currency="IDR",
    status_code="200",transaction_status="settlement",fraud_status="accept"
}));
var mismatch = await gateway.GetPaymentStatusAsync(statusRequest);
Check(mismatch.Notification is null, "amount mismatch cannot mark payment paid");

handler.Reply = req => Task.FromResult(Json(200, new {
    order_id=reference, gross_amount="25000.00", currency="IDR",
    status_code="200",transaction_status="capture",fraud_status="challenge"
}));
var challenge = await gateway.GetPaymentStatusAsync(statusRequest);
Check(challenge.Notification?.Status == PaymentWebhookStatus.Pending,
    "fraud challenge never considered paid");

handler.Reply = req => Task.FromResult(Json(404, new { message="not found" }));
var notChosen = await gateway.GetPaymentStatusAsync(statusRequest);
Check(notChosen.Notification is null,
    "unselected Snap payment method is not a failed/paid transaction");

var sigMaterial = reference + "200" + "25000.00" + secret;
var signature = Convert.ToHexString(
    SHA512.HashData(Encoding.UTF8.GetBytes(sigMaterial))).ToLowerInvariant();
var payload = JsonSerializer.Serialize(new {
    order_id=reference, gross_amount="25000.00",
    status_code="200", transaction_status="settlement", signature_key=signature
});
var invalid = await gateway.ParseWebhookAsync(payload.Replace(signature, new string('f',128)),
    new Dictionary<string,string>());
Check(invalid.Notification is null, "incorrect separate Signature Secret rejected");

handler.Reply = req => Task.FromResult(Json(200, new {
    order_id=reference, gross_amount="25000.00",currency="IDR",
    status_code="201",transaction_status="pending",transaction_id="synthetic-transaction"
}));
var verified = await gateway.ParseWebhookAsync(payload,new Dictionary<string,string>());
Check(verified.Notification?.Status == PaymentWebhookStatus.Pending,
    "signed fake settlement cannot bypass server-to-server pending status");

var prodConfig = new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string,string?> {
        ["Payments:Midtrans:Enabled"]="true",["Payments:Midtrans:Mode"]="Sandbox",
        ["Payments:Midtrans:ServerKey"]=key,["ASPNETCORE_ENVIRONMENT"]="Production"
    }).Build();
Check(!new MidtransSnapGateway(new Factory(handler),prodConfig).IsLiveConfigured,
    "production environment cannot use Sandbox");
Console.WriteLine($"PASS: {passed} synthetic adapter checks, zero external API calls.");

static HttpResponseMessage Json(int status, object value) =>
    new((HttpStatusCode)status) {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8,"application/json")
    };

sealed class Factory(FakeHandler handler) : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new(handler, disposeHandler:false);
}
sealed class FakeHandler : HttpMessageHandler {
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply {get;set;} =
        _ => throw new InvalidOperationException("No fake response");
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage req,CancellationToken ct) => Reply(req);
}
