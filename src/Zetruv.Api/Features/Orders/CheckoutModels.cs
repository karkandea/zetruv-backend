using System.ComponentModel.DataAnnotations;
using Zetruv.Api.Features.Catalog;

namespace Zetruv.Api.Features.Orders;

public sealed record CheckoutItemRequest(
    Guid ProductVariantId,
    [property: Range(1, 99)] int Quantity,
    Guid? GameAccountValidationId = null);

public sealed record CreateCheckoutOrderRequest(
    [property: MaxLength(120)] string? CustomerName,
    [property: EmailAddress, MaxLength(320)] string? CustomerEmail,
    [property: MaxLength(50)] string? CustomerPhone,
    [property: Required, MinLength(1)] IReadOnlyList<CheckoutItemRequest> Items,
    Guid? ShippingQuoteId = null);

public sealed record CheckoutOrderItemResponse(
    Guid ProductVariantId,
    string ProductName,
    string ProductSlug,
    ProductKind ProductKind,
    string VariantName,
    string? ThumbnailUrl,
    string? GameName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

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
    DateTimeOffset CreatedAt);

public sealed record CreateCheckoutOrderResult(
    CreateCheckoutOrderResponse? Order,
    string? Error)
{
    public static CreateCheckoutOrderResult Success(CreateCheckoutOrderResponse order) =>
        new(order, null);

    public static CreateCheckoutOrderResult Failure(string error) =>
        new(null, error);
}
