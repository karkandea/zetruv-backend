using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

/// <summary>
/// Public, non-sensitive eligibility signal for the customer storefront.
/// A published SKU is not itself proof of provider authorization or operator capacity.
/// </summary>
public sealed record ManualLoginReadinessResponse(
    Guid ProductId, bool Available, string Status, string FulfillmentMode);

public sealed class ManualLoginReadinessService(
    IConfiguration configuration,
    ManualLoginCredentialProtector protector)
{
    public bool IsReady(Guid productId)
    {
        // Real authorization must be recorded by operations after confirming the
        // game's terms and the exact supported fulfillment process. NEVER
        // enable globally based only on product.IsActive or encryption readiness.
        var approved = configuration.GetSection("ManualLogin:AuthorizedProductIds")
            .GetChildren().Any(x => Guid.TryParse(x.Value, out var id) && id == productId);
        return approved &&
               configuration.GetValue<bool>("ManualLogin:ProviderAuthorizationConfirmed") &&
               configuration.GetValue<bool>("ManualLogin:OperatorsReady") &&
               protector.IsConfigured;
    }
}

[ApiController]
[Route("api/v1/checkout/manual-login/readiness")]
public sealed class ManualLoginReadinessController(
    ZetruvDbContext db,
    ManualLoginReadinessService readiness) : ControllerBase
{
    [HttpGet("{productId:guid}")]
    public async Task<ActionResult<ManualLoginReadinessResponse>> Get(
        Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new {
                x.Id, x.Kind, x.FulfillmentMethod, x.IsActive,
                CategoryActive = x.Category.IsActive,
                GameActive = x.Game == null || x.Game.IsActive,
                HasActiveVariant = x.Variants.Any(v => v.IsActive &&
                    (!v.StockQuantity.HasValue || v.StockQuantity.Value > 0)),
                HasInputSchema = x.InputFields.Any(f => f.Scope == ProductInputFieldScope.LoginCredential)
            }).SingleOrDefaultAsync(ct);
        if (product is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        var eligible = product.Kind == ProductKind.TopUpLogin &&
            product.FulfillmentMethod == FulfillmentMethod.MANUAL_LOGIN &&
            product.IsActive && product.CategoryActive && product.GameActive &&
            product.HasActiveVariant && product.HasInputSchema && readiness.IsReady(product.Id);
        return Ok(new ManualLoginReadinessResponse(
            product.Id, eligible,
            eligible ? "Ready" : "NotReady",
            eligible ? "CmsManualAfterPaid" : "None"));
    }
}
