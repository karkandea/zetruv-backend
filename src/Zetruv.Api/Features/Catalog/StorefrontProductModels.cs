using System.ComponentModel.DataAnnotations;

namespace Zetruv.Api.Features.Catalog;

public sealed class GameAccountDetails
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Rank { get; set; } = "";
    public int? SkinCount { get; set; }
    public string Region { get; set; } = "";
    public int? Level { get; set; }
    public string? AdditionalInfo { get; set; }
    // Legacy scalar columns are retained for backwards-safe data migration.
    // New updates and reads use schema-validated JSON attributes.
    public string AttributesJson { get; set; } = "{}";
}

public sealed class ProductReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CustomerUserId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public bool IsApproved { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed record CreateProductReviewRequest(
    Guid OrderItemId, [Range(1, 5)] int Rating,
    [MaxLength(1000)] string? Comment);
