using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Orders;

const string reference = "ZTR-FULFILL-CODM-SMOKE";
const string openId = "0012345678910";
const string sku = "codm-test-only";
var options = new DigiflazzOptions
{
    Enabled = true,
    Username = "smoke-user",
    ApiKey = "smoke-key",
    MaxPrice = 4500
};
var handler = new DigiflazzHandler();
var provider = new DigiflazzAutoIdFulfillmentProvider(handler, Options.Create(options));
var request = new AutoIdFulfillmentProviderRequest(Guid.NewGuid(), "TEST-ORDER", Guid.NewGuid(),
    "CODM Garena", "call-of-duty-mobile-via-id", "31 CP", "CODM-31", sku, "CODM", 1,
    null, null, null, reference, new Dictionary<string, string> { ["openid"] = openId });

handler.Responses.Enqueue(() => Reply("Pending", "03"));
handler.Responses.Enqueue(() => throw new HttpRequestException("Synthetic network interruption"));
handler.Responses.Enqueue(() => Reply("Sukses", "00", "different-reference"));
handler.Responses.Enqueue(() => Reply("Gagal", "49"));
handler.Responses.Enqueue(() => Reply("Sukses", "00"));
handler.Responses.Enqueue(() => Reply("Gagal", "54"));
for (var i = 0; i < 4; i++)
{
    Check((await provider.FulfillAsync(request)).IsPending,
        "Pending, transport errors, mismatched replies, and duplicate references remain Processing.");
}
Check(await provider.FulfillAsync(request) is { IsSuccess: true, ProviderReference: reference },
    "Matching Digiflazz success completes the top up.");
Check(await provider.FulfillAsync(request) is { IsSuccess: false, IsPending: false },
    "A confirmed rejection fails fulfillment.");
Check(await provider.FulfillAsync(request with { Quantity = 2 }) is { IsSuccess: false, IsPending: false },
    "An order line cannot be silently fulfilled with fewer packages than purchased.");
options.Enabled = false;
Check((await provider.FulfillAsync(request)).IsPending && handler.Payloads.Count == 6,
    "Disabled and invalid requests do not contact Digiflazz.");

foreach (var body in handler.Payloads)
{
    Check(body.GetProperty("ref_id").GetString() == reference &&
        body.GetProperty("customer_no").GetString() == openId &&
        body.GetProperty("buyer_sku_code").GetString() == sku,
        "Each status check retains the original reference, OpenID and SKU.");
    Check(body.GetProperty("username").GetString() == "smoke-user" &&
        body.GetProperty("sign").GetString() == "3f73da22bb53047e739f3ddf7d668f97" &&
        body.GetProperty("testing").GetBoolean() && body.GetProperty("max_price").GetInt32() == 4500,
        "The request uses Digiflazz signing, test mode and the configured IDR price cap.");
}

Console.WriteLine("PASS: Digiflazz Buyer API contract (synthetic HTTP responses)");

HttpResponseMessage Reply(string status, string rc, string replyReference = reference) =>
    new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            data = new { status, rc, ref_id = replyReference, customer_no = openId, buyer_sku_code = sku }
        })
    };

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class DigiflazzHandler : HttpMessageHandler, IHttpClientFactory
{
    public Queue<Func<HttpResponseMessage>> Responses { get; } = new();
    public List<JsonElement> Payloads { get; } = [];

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false)
    {
        BaseAddress = new Uri("https://api.digiflazz.com/")
    };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != "/v1/transaction")
            throw new InvalidOperationException("The Buyer API transaction endpoint changed.");

        Payloads.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken));
        return Responses.Dequeue()();
    }
}
