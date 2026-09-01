using System.ComponentModel.DataAnnotations;
using Zetruv.Api.Features.Orders;

namespace Zetruv.Api.Features.Shipping;

public enum ShipmentStatus
{
    Pending,
    ReadyToShip,
    Shipped,
    Delivered,
    Cancelled
}

public sealed class ShippingQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IDR";
    public int TotalWeightGrams { get; set; }
    public int? EtaMinDays { get; set; }
    public int? EtaMaxDays { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CartFingerprint { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "IDR";
    public int TotalWeightGrams { get; set; }
    public int? EtaMinDays { get; set; }
    public int? EtaMaxDays { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed record ShippingQuoteItemRequest(
    Guid ProductVariantId,
    [property: Range(1, 99)] int Quantity);

public sealed record ShippingAddressRequest(
    [property: Required, MaxLength(120)] string RecipientName,
    [property: Required, MaxLength(50)] string Phone,
    [property: Required, MaxLength(250)] string AddressLine1,
    [property: MaxLength(250)] string? AddressLine2,
    [property: Required, MaxLength(120)] string District,
    [property: Required, MaxLength(120)] string City,
    [property: Required, MaxLength(120)] string Province,
    [property: Required, RegularExpression("^[0-9]{5}$")] string PostalCode);

public sealed record CreateShippingQuotesRequest(
    [property: Required] ShippingAddressRequest Address,
    [property: Required, MinLength(1)] IReadOnlyList<ShippingQuoteItemRequest> Items);

public sealed record ShippingRateResponse(
    Guid QuoteId,
    string Provider,
    string ServiceCode,
    string ServiceName,
    decimal Amount,
    string Currency,
    int TotalWeightGrams,
    int? EtaMinDays,
    int? EtaMaxDays,
    DateTimeOffset ExpiresAt);

public sealed record CreateShippingQuotesResponse(
    IReadOnlyList<ShippingRateResponse> Rates);

public sealed record ShippingProviderQuoteRequest(
    int TotalWeightGrams,
    string City,
    string Province,
    string PostalCode);

public sealed record ShippingProviderRate(
    string ServiceCode,
    string ServiceName,
    decimal Amount,
    int? EtaMinDays,
    int? EtaMaxDays,
    string? ProviderReference = null);

public interface IShippingProvider
{
    string Name { get; }

    Task<IReadOnlyList<ShippingProviderRate>> QuoteAsync(
        ShippingProviderQuoteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MockShippingProvider : IShippingProvider
{
    public string Name => "mock";

    public Task<IReadOnlyList<ShippingProviderRate>> QuoteAsync(
        ShippingProviderQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var billableKg = Math.Max(1m, Math.Ceiling(request.TotalWeightGrams / 1000m));

        IReadOnlyList<ShippingProviderRate> rates =
        [
            new(
                "REG",
                "Regular",
                12_000m + (5_000m * billableKg),
                2,
                4,
                $"MOCK-REG-{Guid.NewGuid():N}"),
            new(
                "EXP",
                "Express",
                20_000m + (8_000m * billableKg),
                1,
                2,
                $"MOCK-EXP-{Guid.NewGuid():N}")
        ];

        return Task.FromResult(rates);
    }
}

public sealed class ShippingProviderResolver(
    IEnumerable<IShippingProvider> providers,
    IConfiguration configuration)
{
    public IShippingProvider? Resolve()
    {
        var configuredProvider = configuration["Shipping:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredProvider))
        {
            return null;
        }

        return providers.FirstOrDefault(x =>
            string.Equals(x.Name, configuredProvider, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record CheckoutShippingQuote(
    Guid Id,
    string Provider,
    string? ProviderReference,
    string ServiceCode,
    string ServiceName,
    decimal Amount,
    string Currency,
    int TotalWeightGrams,
    int? EtaMinDays,
    int? EtaMaxDays,
    string RecipientName,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string Province,
    string PostalCode);

public sealed record ShipmentAdminResponse(
    ShipmentStatus Status,
    string Provider,
    string ServiceCode,
    string ServiceName,
    string? TrackingNumber,
    decimal Cost,
    string Currency,
    int TotalWeightGrams,
    int? EtaMinDays,
    int? EtaMaxDays,
    string RecipientName,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string Province,
    string PostalCode,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt);

public sealed record ShipmentTrackingResponse(
    ShipmentStatus Status,
    string Provider,
    string ServiceCode,
    string ServiceName,
    string? TrackingNumber,
    int? EtaMinDays,
    int? EtaMaxDays,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt);
