using System.ComponentModel.DataAnnotations;

namespace Zetruv.Api.Features.Catalog;

public enum ProductKind
{
    TopUpGame,
    TopUpLogin,
    GameVoucher,
    Joki,
    Merchandise,
    GameAccount
}

public sealed class CatalogCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public ProductKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Product> Products { get; set; } = [];
}

public sealed class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPopular { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Product> Products { get; set; } = [];
}

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public CatalogCategory Category { get; set; } = null!;
    public Guid? GameId { get; set; }
    public Game? Game { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public ProductKind Kind { get; set; }
    public bool RequiresGameAccountValidation { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ProductVariant> Variants { get; set; } = [];
    public ICollection<ProductImage> Images { get; set; } = [];
}

public sealed class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int? StockQuantity { get; set; }
    public int? WeightGrams { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Promotion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsFlashSale { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<PromotionItem> Items { get; set; } = [];
}

public sealed class PromotionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public decimal SalePrice { get; set; }
    public int SortOrder { get; set; }
}

public sealed record CategoryResponse(
    Guid Id,
    string Key,
    string Name,
    string Slug,
    string? Description,
    string? IconUrl,
    ProductKind Kind,
    int SortOrder);

public sealed record GameResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Publisher,
    string? ImageUrl,
    bool IsPopular,
    int SortOrder);

public sealed record ProductListItemResponse(
    Guid Id,
    string Name,
    string Slug,
    ProductKind Kind,
    string? ThumbnailUrl,
    string CategorySlug,
    string? GameName,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool IsFeatured);

public sealed record ProductVariantResponse(
    Guid Id,
    string Name,
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    int? StockQuantity,
    int? WeightGrams,
    int SortOrder);

public sealed record ProductImageResponse(
    Guid Id,
    string Url,
    string? AltText,
    int SortOrder);

public sealed record ProductDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    ProductKind Kind,
    string? ShortDescription,
    string? Description,
    string? ThumbnailUrl,
    bool RequiresGameAccountValidation,
    bool IsFeatured,
    CategoryResponse Category,
    GameResponse? Game,
    IReadOnlyList<ProductVariantResponse> Variants,
    IReadOnlyList<ProductImageResponse> Images);

public sealed record ProductPageResponse(
    IReadOnlyList<ProductListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record FlashSaleItemResponse(
    Guid PromotionItemId,
    Guid ProductId,
    Guid VariantId,
    string ProductName,
    string ProductSlug,
    string VariantName,
    string? ThumbnailUrl,
    string? GameName,
    decimal OriginalPrice,
    decimal SalePrice,
    int SortOrder);

public sealed record FlashSaleResponse(
    Guid Id,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<FlashSaleItemResponse> Items);

public sealed record UpsertCategoryRequest(
    [Required, MaxLength(80)] string Key,
    [Required, MaxLength(120)] string Name,
    [Required, MaxLength(160)] string Slug,
    [MaxLength(500)] string? Description,
    [MaxLength(1000)] string? IconUrl,
    ProductKind Kind,
    bool IsActive,
    int SortOrder);

public sealed record UpsertGameRequest(
    [Required, MaxLength(120)] string Name,
    [Required, MaxLength(160)] string Slug,
    [MaxLength(120)] string? Publisher,
    [MaxLength(1000)] string? ImageUrl,
    bool IsActive,
    bool IsPopular,
    int SortOrder);

public sealed record UpsertProductRequest(
    Guid CategoryId,
    Guid? GameId,
    [Required, MaxLength(180)] string Name,
    [Required, MaxLength(220)] string Slug,
    [MaxLength(500)] string? ShortDescription,
    string? Description,
    [MaxLength(1000)] string? ThumbnailUrl,
    ProductKind Kind,
    bool RequiresGameAccountValidation,
    bool IsActive,
    bool IsFeatured,
    int SortOrder);

public sealed record UpsertVariantRequest(
    [Required, MaxLength(180)] string Name,
    [Required, MaxLength(100)] string Sku,
    [Range(typeof(decimal), "0", "9999999999999999")] decimal Price,
    decimal? CompareAtPrice,
    int? StockQuantity,
    int? WeightGrams,
    bool IsActive,
    int SortOrder);

public sealed record UpsertImageRequest(
    [Required, MaxLength(1000)] string Url,
    [MaxLength(250)] string? AltText,
    int SortOrder);

public sealed record PromotionItemRequest(
    Guid ProductVariantId,
    [Range(typeof(decimal), "0", "9999999999999999")] decimal SalePrice,
    int SortOrder);

public sealed record UpsertPromotionRequest(
    [Required, MaxLength(160)] string Name,
    [Required, MaxLength(180)] string Slug,
    bool IsFlashSale,
    bool IsActive,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<PromotionItemRequest> Items);
