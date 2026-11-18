using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        if (review.IsApproved == request.IsApproved && review.ReviewedAt.HasValue)
            return NoContent();
        review.IsApproved = request.IsApproved;
        review.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("reviews/moderation")]
    public async Task<IActionResult> ReviewQueue(
        [FromQuery] ProductReviewStatus? status,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        [FromQuery, Range(1, 50)] int limit = 20,
        CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "private, no-store";
        var reviews = db.ProductReviews.AsNoTracking();
        reviews = status switch
        {
            ProductReviewStatus.Pending => reviews.Where(x => !x.IsApproved && x.ReviewedAt == null),
            ProductReviewStatus.Published => reviews.Where(x => x.IsApproved),
            ProductReviewStatus.Hidden => reviews.Where(x => !x.IsApproved && x.ReviewedAt != null),
            _ => reviews
        };

        var rows = await (from review in reviews
            join product in db.Products on review.ProductId equals product.Id
            join item in db.OrderItems on review.OrderItemId equals item.Id
            orderby review.CreatedAt descending, review.Id descending
            select new
            {
                review.Id, review.Rating, review.Comment, review.CreatedAt, review.ReviewedAt,
                ProductName = product.Name,
                item.Order.OrderNumber,
                Status = review.IsApproved ? ProductReviewStatus.Published :
                    review.ReviewedAt == null ? ProductReviewStatus.Pending : ProductReviewStatus.Hidden
            }).Skip(offset).Take(limit + 1).ToListAsync(ct);

        return Ok(new { items = rows.Take(limit), hasMore = rows.Count > limit });
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
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId))
            return Unauthorized();
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
        var comment = request.Comment?.Trim();
        db.ProductReviews.Add(new ProductReview
        {
            OrderItemId = request.OrderItemId,
            ProductId = item.ProductId,
            CustomerUserId = customerId,
            Rating = request.Rating,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
            IsApproved = false
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_product_reviews_OrderItemId"
        })
        {
            return Conflict(new { message = "This order item already has a review." });
        }
        return Accepted(new { message = "Review received and pending moderation." });
    }
}
