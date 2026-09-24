using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/catalog")]
public sealed class CmsGameAccountDetailsController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet("products/{productId:guid}/game-account-details")]
    public async Task<IActionResult> Get(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(x => x.Id == productId && x.Kind == ProductKind.GameAccount)
            .Select(x => new { x.Id, x.GameId }).SingleOrDefaultAsync(ct);
        if (product is null) return NotFound();
        var details = await db.GameAccountDetails.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProductId == productId, ct);
        var schema = product.GameId.HasValue
            ? await db.GameAccountAttributeDefinitions.AsNoTracking()
                .Where(x => x.GameId == product.GameId.Value)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Key).ToListAsync(ct)
            : [];
        return Ok(new GameAccountDetailsEditorResponse(productId, product.GameId,
            details is null ? [] : GameAccountAttributeRules.ParseValues(details.AttributesJson),
            schema.Select(GameAccountAttributeRules.ToResponse).ToList(),
            product.GameId is null));
    }

    [HttpPut("products/{productId:guid}/game-account-details")]
    public async Task<IActionResult> Upsert(
        Guid productId, UpdateGameAccountDetailsRequest request, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(x => x.Id == productId && x.Kind == ProductKind.GameAccount)
            .Select(x => new { x.Id, x.GameId }).SingleOrDefaultAsync(ct);
        if (product is null) return NotFound();
        if (!product.GameId.HasValue)
            return Conflict(new { message = "Legacy account listing has no game; create a game-linked listing to edit attributes." });
        if (request.Attributes is null)
            return BadRequest(new { message = "Attributes object is required." });

        var schema = await db.GameAccountAttributeDefinitions.AsNoTracking()
            .Where(x => x.GameId == product.GameId.Value).ToListAsync(ct);
        var error = GameAccountAttributeRules.NormalizeValues(
            schema, request.Attributes, out var normalizedJson);
        if (error is not null) return BadRequest(new { message = error });

        var details = await db.GameAccountDetails.SingleOrDefaultAsync(
            x => x.ProductId == productId, ct);
        if (details is null)
        {
            details = new GameAccountDetails { ProductId = productId };
            db.GameAccountDetails.Add(details);
        }
        // The submitted active fields replace the active portion of a listing.
        // Inactive values stay archived for a possible CMS reactivation.
        var activeKeys = schema.Where(x => x.IsActive).Select(x => x.Key).ToHashSet();
        var retained = GameAccountAttributeRules.ParseValues(details.AttributesJson)
            .Where(x => !activeKeys.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value);
        foreach (var (key, value) in GameAccountAttributeRules.ParseValues(normalizedJson))
            retained[key] = value;
        details.AttributesJson = System.Text.Json.JsonSerializer.Serialize(retained);
        await db.SaveChangesAsync(ct);
        return Ok(new GameAccountDetailsEditorResponse(productId, product.GameId,
            GameAccountAttributeRules.ParseValues(details.AttributesJson),
            schema.OrderBy(x => x.SortOrder).ThenBy(x => x.Key)
                .Select(GameAccountAttributeRules.ToResponse).ToList(), false));
    }

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews(CancellationToken ct) =>
        Ok(await db.ProductReviews.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id, x.ProductId, x.OrderItemId, x.Rating, x.Comment,
                x.IsApproved, x.CreatedAt, x.ReviewedAt
            }).Take(200).ToListAsync(ct));

    [HttpPut("reviews/{id:guid}/approval")]
    public async Task<IActionResult> ReviewApproval(
        Guid id, [FromBody] ReviewApprovalRequest request, CancellationToken ct)
    {
        var review = await db.ProductReviews.FindAsync([id], ct);
        if (review is null) return NotFound();
        review.IsApproved = request.IsApproved;
        review.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record ReviewApprovalRequest(bool IsApproved);

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/reviews")]
public sealed class CustomerReviewsController(ZetruvDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateProductReviewRequest request, CancellationToken ct)
    {
        var customerId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var item = await db.OrderItems.AsNoTracking()
            .Where(x => x.Id == request.OrderItemId &&
                x.Order.CustomerUserId == customerId &&
                x.Order.Status == OrderStatus.Completed &&
                x.Order.PaymentStatus == PaymentStatus.Paid &&
                x.ProductId != null)
            .Select(x => new { x.Id, ProductId = x.ProductId!.Value })
            .SingleOrDefaultAsync(ct);
        if (item is null)
            return BadRequest(new { message = "A completed paid purchase is required before reviewing." });
        if (await db.ProductReviews.AnyAsync(x => x.OrderItemId == request.OrderItemId, ct))
            return Conflict(new { message = "This order item already has a review." });
        db.ProductReviews.Add(new ProductReview
        {
            OrderItemId = request.OrderItemId,
            ProductId = item.ProductId,
            CustomerUserId = customerId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim(),
            IsApproved = false
        });
        await db.SaveChangesAsync(ct);
        return Accepted(new { message = "Review received and pending moderation." });
    }
}
