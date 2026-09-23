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
}

public sealed record GameAccountDetailsResponse(
    string Rank, int? SkinCount, string Region, int? Level, string? AdditionalInfo);
public sealed record UpdateGameAccountDetailsRequest(
    [Required, MaxLength(100)] string Rank,
    [Range(0, 100000)] int? SkinCount,
    [Required, MaxLength(120)] string Region,
    [Range(1, 100000)] int? Level,
    [MaxLength(1000)] string? AdditionalInfo);

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
