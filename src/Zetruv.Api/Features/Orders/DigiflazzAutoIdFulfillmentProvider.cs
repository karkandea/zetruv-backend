using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Zetruv.Api.Features.Orders;

public sealed class DigiflazzOptions
{
    public const string SectionName = "Fulfillment:Digiflazz";

    public bool Enabled { get; set; }
    public bool Testing { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int MaxPrice { get; set; }

    public bool IsReady => Enabled && !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(ApiKey) && (Testing || MaxPrice > 0);

    public string BuyerBinding => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{Testing}:{Username.Trim()}")));
}

public sealed class DigiflazzAutoIdFulfillmentProvider(
    IHttpClientFactory clients,
    IOptions<DigiflazzOptions> options) : IAutoIdFulfillmentProvider
{
    public const string ProviderCode = "digiflazz";
    public const string CodmGameSlug = "call-of-duty-mobile";
    public const string OpenIdField = "openid";
    public const string HttpClientName = "zetruv-digiflazz";
    public static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromMinutes(1);
    // Digiflazz can create a new transaction when a reference is older than 90 days.
    public static readonly TimeSpan MaximumOrderAge = TimeSpan.FromDays(89);

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Name => ProviderCode;

    public async Task<AutoIdFulfillmentProviderResult> FulfillAsync(
        AutoIdFulfillmentProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsReady)
            return AutoIdFulfillmentProviderResult.Pending("Digiflazz is temporarily unavailable.");

        if (request.Quantity != 1 || !TryGetOpenId(request.DestinationFields, out var openId))
            return AutoIdFulfillmentProviderResult.Failure("A single CODM package and a valid OpenID are required.");

        var username = settings.Username.Trim();
        var signature = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(
            username + settings.ApiKey.Trim() + request.IdempotencyKey)));
        var payload = new TopUpRequest(
            username, request.ProviderSku, openId, request.IdempotencyKey, signature,
            settings.Testing, settings.MaxPrice > 0 ? settings.MaxPrice : null);

        try
        {
            using var http = clients.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync("v1/transaction", payload, WireJson, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Unconfirmed();

            var envelope = await response.Content.ReadFromJsonAsync<TopUpResponse>(WireJson, cancellationToken);
            var data = envelope?.Data;
            if (data is null || data.RefId != request.IdempotencyKey ||
                data.CustomerNo != openId || data.BuyerSkuCode != request.ProviderSku)
                return Unconfirmed();

            return (data.Status?.Trim().ToLowerInvariant(), data.Rc) switch
            {
                ("sukses", "00") => AutoIdFulfillmentProviderResult.Success(request.IdempotencyKey),
                ("pending", _) => AutoIdFulfillmentProviderResult.Pending("Digiflazz is processing the top up."),
                // Timeout, duplicate reference and rate-limit responses do not confirm delivery or rejection.
                ("gagal", "01" or "47" or "49" or "70" or "85") => Unconfirmed(),
                ("gagal", { Length: 2 } code) when code.All(char.IsAsciiDigit) =>
                    AutoIdFulfillmentProviderResult.Failure($"Digiflazz rejected the top up (code {code})."),
                _ => Unconfirmed()
            };
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return Unconfirmed();
        }
    }

    public static bool TryGetOpenId(IReadOnlyDictionary<string, string> fields, out string openId)
    {
        openId = fields.TryGetValue(OpenIdField, out var value) ? value.Trim() : string.Empty;
        return openId.Length is > 0 and <= 64 && openId.All(char.IsAsciiLetterOrDigit);
    }

    private static AutoIdFulfillmentProviderResult Unconfirmed() =>
        AutoIdFulfillmentProviderResult.Pending("Digiflazz has not confirmed the top up yet.");

    private sealed record TopUpRequest(
        string Username,
        string BuyerSkuCode,
        string CustomerNo,
        string RefId,
        string Sign,
        bool Testing,
        int? MaxPrice);

    private sealed record TopUpResponse(TopUpResponseData? Data);

    private sealed record TopUpResponseData(
        string? RefId, string? CustomerNo, string? BuyerSkuCode, string? Rc, string? Status);
}
