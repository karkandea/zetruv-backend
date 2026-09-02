using System.ComponentModel.DataAnnotations;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Features.Shipping;

namespace Zetruv.Api.Features.Orders;

public enum OrderStatus
{
    Pending,
    Processing,
    Completed,
    Cancelled
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public enum PaymentTransactionType
{
    Payment,
    Refund
}

public enum PaymentTransactionStatus
{
    Pending,
    Succeeded,
    Failed
}

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = "IDR";
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<PaymentTransaction> Transactions { get; set; } = [];
    public ShippingQuote? ShippingQuote { get; set; }
    public Shipment? Shipment { get; set; }
}

public sealed class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public ProductKind ProductKind { get; set; }
    public string? VariantName { get; set; }
    public string? Sku { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? GameName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public GameAccountValidation? GameAccountValidation { get; set; }
}

public sealed class PaymentTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public PaymentTransactionType Type { get; set; } = PaymentTransactionType.Payment;
    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IDR";
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record RecentPurchaseResponse(
    Guid OrderItemId,
    Guid? ProductId,
    string ProductName,
    string ProductSlug,
    ProductKind ProductKind,
    string? VariantName,
    string? ThumbnailUrl,
    string? GameName,
    decimal UnitPrice,
    DateTimeOffset PurchasedAt);

public sealed record OrderListItemResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    string? CustomerName,
    decimal GrandTotal,
    string Currency,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CompletedAt);

public sealed record OrderItemResponse(
    Guid Id,
    Guid? ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string ProductSlug,
    ProductKind ProductKind,
    string? VariantName,
    string? Sku,
    string? ThumbnailUrl,
    string? GameName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record PaymentTransactionResponse(
    Guid Id,
    string Provider,
    string? ProviderReference,
    PaymentTransactionType Type,
    PaymentTransactionStatus Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset CreatedAt);

public sealed record OrderDetailResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal GrandTotal,
    string Currency,
    string? PaymentProvider,
    string? PaymentReference,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ShipmentAdminResponse? Shipment,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<PaymentTransactionResponse> Transactions);

public sealed record OrderPageResponse(
    IReadOnlyList<OrderListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record UpdateOrderStatusRequest(OrderStatus Status);
public sealed record UpdatePaymentStatusRequest(PaymentStatus Status);
