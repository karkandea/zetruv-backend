using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Zetruv.Api.Features.Payments;

const string sandboxKey = "SB-Mid-server-synthetic-test-key";
const string reference = "ZTS-765bc85ec054436da631bb7ad4197e8c-A1";
var orderId = Guid.Parse("765bc85e-c054-436d-a631-bb7ad4197e8c");
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Payments:Midtrans:Enabled"] = "true",
    ["Payments:Midtrans:Mode"] = "Sandbox",
    ["Payments:Midtrans:ServerKey"] = sandboxKey,
    ["Payments:Midtrans:ReturnUrl"] = "https://dev.zetruv.com/payment",
    ["Payments:Midtrans:MerchantAbsorbsFees"] = "true",
    ["Payments:Midtrans:Channels:0"] = "QRIS",
    ["Payments:Provider"] = "midtrans"
}).Build();
var environment = new TestEnvironment();
var handler = new MidtransHandler();
var gateway = new MidtransPaymentGateway(handler, config, environment);
var resolver = new PaymentGatewayResolver([gateway], config);
Check(gateway.IsSandbox && gateway.SupportsChannel("QRIS") && !gateway.SupportsChannel("GOPAY") &&
    resolver.CanPayForDigiflazz("midtrans", testing: true) && !resolver.CanPayForDigiflazz("midtrans", testing: false),
    "Only configured channels and matching Digiflazz modes are enabled.");
var charge = new PaymentGatewayCreateRequest(orderId, "ZTR-ORDER", 6000, "IDR", null, null, null, "QRIS");
handler.Respond = async message =>
{
    Check(message.Method == HttpMethod.Post &&
        message.RequestUri?.AbsoluteUri == "https://app.sandbox.midtrans.com/snap/v1/transactions",
        "Snap creation uses the Sandbox host.");
    Check(message.Headers.Authorization?.ToString() == "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(sandboxKey + ":")),
        "Midtrans uses the server key through Basic authentication.");
    var body = await message.Content!.ReadFromJsonAsync<JsonElement>();
    var details = body.GetProperty("transaction_details");
    Check(details.GetProperty("order_id").GetString() == reference && details.GetProperty("gross_amount").GetDecimal() == 6000 &&
        body.GetProperty("enabled_payments")[0].GetString() == "other_qris" &&
        body.GetProperty("callbacks").GetProperty("finish").GetString() == "https://dev.zetruv.com/payment",
        "Snap receives the stable attempt reference, server-quoted amount, method, and return URL.");
    Check(body.GetProperty("expiry").GetProperty("start_time").GetString()?.EndsWith("+0700", StringComparison.Ordinal) == true,
        "Payment expiry has an explicit start time in Jakarta.");
    return Reply(new { redirect_url = "https://app.sandbox.midtrans.com/snap/v3/redirection/synthetic" });
};
var created = await gateway.CreatePaymentAsync(charge);
Check(created.IsSuccess && created.ProviderReference == reference && created.ExpiresAt > DateTimeOffset.UtcNow && created.QrString is null,
    "The existing Zetruv payment page receives the hosted Snap URL.");
Check(!(await gateway.CreatePaymentAsync(charge with { Amount = 6000.25m })).IsSuccess, "Fractional rupiah is rejected.");
handler.Respond = _ => Task.FromResult(Reply(new { redirect_url = "https://app.midtrans.com/snap/other-mode" }));
Check(!(await gateway.CreatePaymentAsync(charge)).IsSuccess, "A cross-mode redirect is rejected.");

var notification = Notification(new string('0', 128));
var before = handler.Calls;
Check((await gateway.ParseWebhookAsync(notification, new Dictionary<string, string>())).Notification is null &&
    handler.Calls == before, "Invalid signature is rejected without contacting Midtrans.");
var signature = Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(reference + "2006000.00" + sandboxKey)));
notification = Notification(signature);
handler.Respond = message =>
{
    Check(message.Method == HttpMethod.Get && message.RequestUri?.AbsoluteUri ==
        $"https://api.sandbox.midtrans.com/v2/{reference}/status", "Webhook status comes from the authenticated Midtrans endpoint.");
    return Task.FromResult(Status("pending", "201", "accept"));
};
Check((await gateway.ParseWebhookAsync(notification, new Dictionary<string, string>())).Notification?.Status == PaymentWebhookStatus.Pending,
    "The webhook's claimed paid status cannot override Midtrans's authenticated status.");
handler.Respond = _ => Task.FromResult(Status("settlement", "200", "accept"));
Check((await gateway.ParseWebhookAsync(notification, new Dictionary<string, string>())).Notification is
    { ProviderReference: reference, Amount: 6000, Status: PaymentWebhookStatus.Paid }, "Verified settlement preserves the exact amount and reference.");
handler.Respond = _ => Task.FromResult(Status("capture", "200", "challenge"));
Check((await gateway.ParseWebhookAsync(notification, new Dictionary<string, string>())).Notification?.Status == PaymentWebhookStatus.Pending,
    "A challenged capture cannot trigger fulfillment.");

var statusRequest = new PaymentGatewayStatusRequest(orderId, "ZTR-ORDER", reference, 6000, "IDR", created.ExpiresAt);
handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
{
    Content = JsonContent.Create(new { status_code = "404", status_message = "Transaction doesn't exist." })
});
Check((await gateway.GetPaymentStatusAsync(statusRequest)).Notification?.Status == PaymentWebhookStatus.Pending,
    "A Snap session without a chosen channel stays pending.");
statusRequest = statusRequest with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
Check((await gateway.GetPaymentStatusAsync(statusRequest)).Notification?.Status == PaymentWebhookStatus.Failed,
    "A missing status after the expiry grace period permits a replacement payment.");
handler.Respond = _ => Task.FromResult(Status("settlement", "200", "accept"));
Check((await gateway.GetPaymentStatusAsync(statusRequest)).Notification?.Status == PaymentWebhookStatus.Paid,
    "A late settlement still wins at the final status check.");
environment.EnvironmentName = Environments.Production;
Check(!gateway.IsLiveConfigured, "Sandbox credentials cannot be activated in Production.");
Console.WriteLine("PASS: Midtrans Snap contract (synthetic HTTP responses)");

string Notification(string signatureKey) => JsonSerializer.Serialize(new
{
    order_id = reference, status_code = "200", gross_amount = "6000.00", signature_key = signatureKey,
    transaction_status = "settlement", fraud_status = "accept"
});

HttpResponseMessage Status(string status, string code, string fraud) => Reply(new
{
    order_id = reference, transaction_id = "a9f-synthetic", gross_amount = "6000.00", currency = "IDR",
    status_code = code, transaction_status = status, fraud_status = fraud
});

static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
static void Check(bool result, string message)
{
    if (!result)
        throw new InvalidOperationException(message);
}

sealed class MidtransHandler : HttpMessageHandler, IHttpClientFactory
{
    public int Calls { get; private set; }
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } =
        _ => throw new InvalidOperationException("Unexpected Midtrans request.");
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Respond(request);
    }
}

sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "MidtransContractSmoke";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
