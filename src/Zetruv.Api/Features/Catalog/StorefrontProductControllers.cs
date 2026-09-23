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
        var details = await db.GameAccountDetails.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProductId == productId, ct);
        return details is null ? NotFound() : Ok(details);
    }

    [HttpPut("products/{productId:guid}/game-account-details")]
    public async Task<IActionResult> Upsert(
        Guid productId, UpdateGameAccountDetailsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Rank) || string.IsNullOrWhiteSpace(request.Region))
            return BadRequest(new { message = "Rank and region are required." });
        var valid = await db.Products.AnyAsync(x =>
            x.Id == productId && x.Kind == ProductKind.GameAccount, ct);
        if (!valid) return BadRequest(new { message = "Product must be a game account." });
        var details = await db.GameAccountDetails
            .SingleOrDefaultAsync(x => x.ProductId == productId, ct);
        if (details is null)
        {
            details = new GameAccountDetails { ProductId = productId };
            db.GameAccountDetails.Add(details);
        }
        details.Rank = request.Rank.Trim();
        details.SkinCount = request.SkinCount;
        details.Region = request.Region.Trim();
        details.Level = request.Level;
        details.AdditionalInfo = request.AdditionalInfo?.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(details);
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
