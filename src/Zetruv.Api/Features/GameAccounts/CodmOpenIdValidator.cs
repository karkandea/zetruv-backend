using Zetruv.Api.Features.Orders;

namespace Zetruv.Api.Features.GameAccounts;

public sealed class CodmOpenIdValidator : IGameAccountValidator
{
    public const string ProviderCode = "codm-openid-format";

    public string Name => ProviderCode;

    public Task<GameAccountProviderResult> ValidateAsync(
        GameAccountProviderRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(request.GameSlug == DigiflazzAutoIdFulfillmentProvider.CodmGameSlug &&
            DigiflazzAutoIdFulfillmentProvider.TryGetOpenId(request.Fields, out _)
                ? GameAccountProviderResult.Valid()
                : GameAccountProviderResult.Invalid("Enter the OpenID from CODM Garena settings."));
}
