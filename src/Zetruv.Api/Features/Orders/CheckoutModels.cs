using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Zetruv.Api.Features.Catalog;

namespace Zetruv.Api.Features.Orders;

public sealed record CheckoutItemRequest(
    Guid ProductVariantId,
    [Range(1, 99)] int Quantity,
    Guid? GameAccountValidationId = null,
    IReadOnlyDictionary<string, string>? LoginCredentials = null,
    [Range(typeof(decimal), "0", "999999999999")] decimal? ExpectedUnitPrice = null,
    bool AcknowledgePriceChange = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? DeliveryDetails = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CartLineId = null);

public sealed record CreateCheckoutOrderRequest(
    [MaxLength(120)] string? CustomerName,
    [EmailAddress, MaxLength(320)] string? CustomerEmail,
    [MaxLength(50)] string? CustomerPhone,
    [Required, MinLength(1)] IReadOnlyList<CheckoutItemRequest> Items,
    Guid? ShippingQuoteId = null,
    [MaxLength(32)] string? VoucherCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [MaxLength(64)] string? CartRevision = null);

public sealed record CheckoutOrderItemResponse(
    Guid ProductVariantId,
    string ProductName,
    string ProductSlug,
    ProductKind ProductKind,
    FulfillmentMethod FulfillmentMethod,
    FulfillmentStatus FulfillmentStatus,
    string VariantName,
    string? ThumbnailUrl,
    string? GameName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string? VariantIconUrl = null,
    string? RankFrom = null,
    string? RankTo = null,
    string? RankFromIconUrl = null,
    string? RankToIconUrl = null);

public sealed record CreateCheckoutOrderResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal GrandTotal,
    string Currency,
    IReadOnlyList<CheckoutOrderItemResponse> Items,
    DateTimeOffset CreatedAt,
    string? OrderAccessToken = null,
    DateTimeOffset? OrderAccessTokenExpiresAt = null)
{
    public string? VoucherCode { get; init; }
    public decimal VoucherDiscountAmount { get; init; }
}

public sealed record CheckoutPriceChangeResponse(
    Guid ProductVariantId,
    decimal ExpectedUnitPrice,
    decimal CurrentUnitPrice);

public sealed record CreateCheckoutOrderResult(
    CreateCheckoutOrderResponse? Order,
    string? Error)
{
    public static CreateCheckoutOrderResult Success(CreateCheckoutOrderResponse order) =>
        new(order, null);

    public IReadOnlyList<CheckoutPriceChangeResponse>? PriceChanges { get; init; }
    public bool CartChanged { get; init; }
    public static CreateCheckoutOrderResult CartConflict() => new(null, "Cart changed. Reload and review before checkout.") { CartChanged = true };

    public static CreateCheckoutOrderResult Failure(string error) =>
        new(null, error);

    public static CreateCheckoutOrderResult PricesChanged(IReadOnlyList<CheckoutPriceChangeResponse> changes) =>
        new(null, "One or more product prices changed. Review the latest prices before checkout.")
        { PriceChanges = changes };
}
