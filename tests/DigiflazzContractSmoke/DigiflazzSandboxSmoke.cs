using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Orders;

internal static class DigiflazzSandboxSmoke
{
    public static async Task<int> RunAsync()
    {
        var username = Environment.GetEnvironmentVariable("DIGIFLAZZ_USERNAME");
        var developmentKey = Environment.GetEnvironmentVariable("DIGIFLAZZ_API_KEY");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(developmentKey))
        {
            Console.Error.WriteLine("Set DIGIFLAZZ_USERNAME and the Digiflazz Development API key in DIGIFLAZZ_API_KEY.");
            return 2;
        }

        var buyer = new DigiflazzAutoIdFulfillmentProvider(new SandboxHttpClientFactory(), Options.Create(
            new DigiflazzOptions { Enabled = true, Testing = true, Username = username, ApiKey = developmentKey }));
        // Digiflazz's prepaid development fixtures, published in the Buyer API Test Case documentation.
        var fixtures = new[]
        {
            new Fixture("Success", "087800001230", Pending: false, Success: true),
            new Fixture("Failure", "087800001232", Pending: false, Success: false),
            new Fixture("Pending to success", "087800001233", Pending: true, Success: true),
            new Fixture("Pending to failure", "087800001234", Pending: true, Success: false)
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var results = await Task.WhenAll(fixtures.Select(fixture => RunFixtureAsync(buyer, fixture, deadline.Token)));
            return results.All(passed => passed) ? 0 : 1;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Console.Error.WriteLine("Digiflazz Sandbox smoke exceeded five minutes.");
            return 1;
        }
    }

    private static async Task<bool> RunFixtureAsync(
        DigiflazzAutoIdFulfillmentProvider buyer, Fixture fixture, CancellationToken cancellationToken)
    {
        var itemId = Guid.NewGuid();
        var reference = $"ZTS-SMOKE-{itemId:N}";
        var request = new AutoIdFulfillmentProviderRequest(Guid.NewGuid(), "SANDBOX", itemId, "Sandbox fixture",
            "digiflazz-sandbox-fixture", null, null, "xld10", null, 1, null, null, null, reference,
            new Dictionary<string, string> { [DigiflazzAutoIdFulfillmentProvider.OpenIdField] = fixture.CustomerNumber });
        var result = await buyer.FulfillAsync(request, cancellationToken);
        if (fixture.Pending && !result.IsPending)
            return Report(fixture, reference, result, passed: false);

        if (fixture.Pending)
        {
            for (var attempt = 0; result.IsPending && attempt < 4; attempt++)
            {
                await Task.Delay(DigiflazzAutoIdFulfillmentProvider.MinimumCheckInterval, cancellationToken);
                result = await buyer.FulfillAsync(request, cancellationToken);
            }
        }

        // Reject configuration and routing errors as invalid smoke runs, even for decline fixtures.
        var passed = !result.IsPending && (fixture.Success
            ? result.IsSuccess && result.ProviderReference == reference
            : !result.IsSuccess && result.Error == "Digiflazz rejected the top up (code 02).");
        return Report(fixture, reference, result, passed);
    }

    private static bool Report(Fixture fixture, string reference, AutoIdFulfillmentProviderResult result, bool passed)
    {
        var status = result.IsSuccess ? "Success" : result.Error;
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {fixture.Name} [{reference}] {status}");
        return passed;
    }

    private sealed record Fixture(string Name, string CustomerNumber, bool Pending, bool Success);

    private sealed class SandboxHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://api.digiflazz.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }
}
