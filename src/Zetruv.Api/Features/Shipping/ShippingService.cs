using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Shipping;

public sealed record CreateShippingQuotesResult(
    CreateShippingQuotesResponse? Response,
    string? Error,
    bool IsConfigurationError = false)
{
    public static CreateShippingQuotesResult Success(CreateShippingQuotesResponse response) =>
        new(response, null);

    public static CreateShippingQuotesResult Failure(
        string error,
        bool isConfigurationError = false) =>
        new(null, error, isConfigurationError);
}

public sealed class ShippingService(
    ZetruvDbContext db,
    ShippingProviderResolver resolver)
{
    public async Task<CreateShippingQuotesResult> CreateQuotesAsync(
        CreateShippingQuotesRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = request.Items;
        if (items is null || items.Count == 0)
        {
            return CreateShippingQuotesResult.Failure("At least one merchandise item is required.");
        }

        var groupedItems = items
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new ShippingQuoteItemRequest(x.Key, x.Sum(i => i.Quantity)))
            .OrderBy(x => x.ProductVariantId)
            .ToList();

        if (groupedItems.Count > 50 || groupedItems.Any(x => x.Quantity is < 1 or > 99))
        {
            return CreateShippingQuotesResult.Failure(
                "Shipping quote supports up to 50 distinct variants and 99 units per variant.");
        }

        var variantIds = groupedItems.Select(x => x.ProductVariantId).ToArray();
        var variants = await db.ProductVariants
            .AsNoTracking()
            .Where(x => variantIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.WeightGrams,
                x.StockQuantity,
                x.IsActive,
                ProductName = x.Product.Name,
                ProductKind = x.Product.Kind,
                ProductIsActive = x.Product.IsActive,
                CategoryIsActive = x.Product.Category.IsActive
            })
            .ToListAsync(cancellationToken);

        if (variants.Count != variantIds.Length)
        {
            return CreateShippingQuotesResult.Failure(
                "One or more product variants do not exist.");
        }

        var variantById = variants.ToDictionary(x => x.Id);
        long totalWeightGrams = 0;

        foreach (var item in groupedItems)
        {
            var variant = variantById[item.ProductVariantId];

            if (variant.ProductKind != ProductKind.Merchandise)
            {
                return CreateShippingQuotesResult.Failure(
                    $"{variant.ProductName} is not a physical merchandise product.");
            }

            if (!variant.IsActive || !variant.ProductIsActive || !variant.CategoryIsActive)
            {
                return CreateShippingQuotesResult.Failure(
                    $"{variant.ProductName} is not available for shipping.");
            }

            if (!variant.WeightGrams.HasValue || variant.WeightGrams.Value <= 0)
            {
                return CreateShippingQuotesResult.Failure(
                    $"{variant.ProductName} does not have a valid shipping weight.");
            }

            if (variant.StockQuantity.HasValue && item.Quantity > variant.StockQuantity.Value)
            {
                return CreateShippingQuotesResult.Failure(
                    $"Insufficient stock for {variant.ProductName}.");
            }

            totalWeightGrams += (long)variant.WeightGrams.Value * item.Quantity;
            if (totalWeightGrams > int.MaxValue)
            {
                return CreateShippingQuotesResult.Failure(
                    "Shipping weight exceeds the supported limit.");
            }
        }

        var address = NormalizeAddress(request.Address);
        if (address is null)
        {
            return CreateShippingQuotesResult.Failure("Shipping address is incomplete.");
        }

        var provider = resolver.Resolve();
        if (provider is null)
        {
            return CreateShippingQuotesResult.Failure(
                "Shipping provider is not configured.",
                isConfigurationError: true);
        }

        var providerRates = await provider.QuoteAsync(
            new ShippingProviderQuoteRequest(
                (int)totalWeightGrams,
                address.City,
                address.Province,
                address.PostalCode),
            cancellationToken);

        if (providerRates.Count == 0)
        {
            return CreateShippingQuotesResult.Failure(
                "No shipping service is available for this destination.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(15);
        var fingerprint = CreateCartFingerprint(groupedItems);
        var quotes = new List<ShippingQuote>(providerRates.Count);

        foreach (var rate in providerRates)
        {
            if (string.IsNullOrWhiteSpace(rate.ServiceCode) ||
                string.IsNullOrWhiteSpace(rate.ServiceName) ||
                rate.Amount < 0)
            {
                continue;
            }

            quotes.Add(new ShippingQuote
            {
                Provider = provider.Name,
                ProviderReference = Clean(rate.ProviderReference),
                ServiceCode = rate.ServiceCode.Trim(),
                ServiceName = rate.ServiceName.Trim(),
                Amount = rate.Amount,
                Currency = "IDR",
                TotalWeightGrams = (int)totalWeightGrams,
                EtaMinDays = rate.EtaMinDays,
                EtaMaxDays = rate.EtaMaxDays,
                RecipientName = address.RecipientName,
                Phone = address.Phone,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                District = address.District,
                City = address.City,
                Province = address.Province,
                PostalCode = address.PostalCode,
                CartFingerprint = fingerprint,
                ExpiresAt = expiresAt,
                CreatedAt = now
            });
        }

        if (quotes.Count == 0)
        {
            return CreateShippingQuotesResult.Failure(
                "Shipping provider returned no valid rates.");
        }

        db.Set<ShippingQuote>().AddRange(quotes);
        await db.SaveChangesAsync(cancellationToken);

        return CreateShippingQuotesResult.Success(
            new CreateShippingQuotesResponse(
                quotes.Select(x => new ShippingRateResponse(
                    x.Id,
                    x.Provider,
                    x.ServiceCode,
                    x.ServiceName,
                    x.Amount,
                    x.Currency,
                    x.TotalWeightGrams,
                    x.EtaMinDays,
                    x.EtaMaxDays,
                    x.ExpiresAt))
                .ToList()));
    }

    public async Task<CheckoutShippingQuote?> GetCheckoutQuoteAsync(
        Guid quoteId,
        IReadOnlyList<ShippingQuoteItemRequest> merchandiseItems,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = CreateCartFingerprint(merchandiseItems);

        return await db.Set<ShippingQuote>()
            .AsNoTracking()
            .Where(x =>
                x.Id == quoteId &&
                x.OrderId == null &&
                x.ConsumedAt == null &&
                x.ExpiresAt > now &&
                x.CartFingerprint == fingerprint)
            .Select(x => new CheckoutShippingQuote(
                x.Id,
                x.Provider,
                x.ProviderReference,
                x.ServiceCode,
                x.ServiceName,
                x.Amount,
                x.Currency,
                x.TotalWeightGrams,
                x.EtaMinDays,
                x.EtaMaxDays,
                x.RecipientName,
                x.Phone,
                x.AddressLine1,
                x.AddressLine2,
                x.District,
                x.City,
                x.Province,
                x.PostalCode))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ClaimQuoteAsync(
        Guid quoteId,
        Guid orderId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var affected = await db.Set<ShippingQuote>()
            .Where(x =>
                x.Id == quoteId &&
                x.OrderId == null &&
                x.ConsumedAt == null &&
                x.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.OrderId, orderId)
                .SetProperty(x => x.ConsumedAt, now),
                cancellationToken);

        return affected == 1;
    }

    public static string CreateCartFingerprint(
        IEnumerable<ShippingQuoteItemRequest> items)
    {
        var canonical = string.Join(
            "|",
            items
                .GroupBy(x => x.ProductVariantId)
                .Select(x => new { Id = x.Key, Quantity = x.Sum(i => i.Quantity) })
                .OrderBy(x => x.Id)
                .Select(x => $"{x.Id:N}:{x.Quantity}"));

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static ShippingAddressRequest? NormalizeAddress(ShippingAddressRequest? address)
    {
        if (address is null)
        {
            return null;
        }

        var recipientName = Clean(address.RecipientName);
        var phone = Clean(address.Phone);
        var addressLine1 = Clean(address.AddressLine1);
        var district = Clean(address.District);
        var city = Clean(address.City);
        var province = Clean(address.Province);
        var postalCode = Clean(address.PostalCode);

        if (recipientName is null || phone is null || addressLine1 is null ||
            district is null || city is null || province is null || postalCode is null)
        {
            return null;
        }

        return new ShippingAddressRequest(
            recipientName,
            phone,
            addressLine1,
            Clean(address.AddressLine2),
            district,
            city,
            province,
            postalCode);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[ApiController]
[Route("api/v1/shipping")]
public sealed class ShippingController(ShippingService shippingService) : ControllerBase
{
    [HttpPost("quotes")]
    [EnableRateLimiting("shipping-quote")]
    public async Task<ActionResult<CreateShippingQuotesResponse>> CreateQuotes(
        CreateShippingQuotesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await shippingService.CreateQuotesAsync(request, cancellationToken);
        if (result.Response is not null)
        {
            return Ok(result.Response);
        }

        if (result.IsConfigurationError)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = result.Error });
        }

        return BadRequest(new { message = result.Error });
    }
}
