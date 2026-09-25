using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public enum DiscountVoucherType { Fixed, Percentage }

// An issued order claims capacity. Failed payment can retry that same order;
// cancellation of an unpaid order releases capacity. This prevents oversubscription.
public sealed class DiscountVoucher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(32)] public string Code { get; set; } = "";
    public DiscountVoucherType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public decimal MinimumSpend { get; set; }
    public ProductKind? ApplicableKind { get; set; }
    public int? MaxUses { get; set; }
    public int? MaxUsesPerCustomer { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DiscountVoucherRedemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoucherId { get; set; }
    public DiscountVoucher Voucher { get; set; } = null!;
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    [MaxLength(64)] public string CustomerKey { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTimeOffset ClaimedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }
}

public sealed record UpsertDiscountVoucherRequest(
    [Required, MaxLength(32)] string Code,
    DiscountVoucherType Type,
    decimal Value,
    decimal? MaximumDiscount,
    decimal MinimumSpend,
    ProductKind? ApplicableKind,
    int? MaxUses,
    int? MaxUsesPerCustomer,
    bool IsActive,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record DiscountVoucherResponse(
    Guid Id, string Code, DiscountVoucherType Type, decimal Value,
    decimal? MaximumDiscount, decimal MinimumSpend, ProductKind? ApplicableKind,
    int? MaxUses, int? MaxUsesPerCustomer, int UsedCount, bool IsActive,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool HasClaims { get; init; }
}

public sealed record PreviewDiscountVoucherRequest(
    [Required] string Code,
    [Required, MinLength(1)] IReadOnlyList<CartItemInput> Items,
    string? CustomerEmail = null,
    string? CustomerPhone = null);

public sealed record PreviewDiscountVoucherResponse(
    string Code, decimal Subtotal, decimal EligibleSubtotal,
    decimal DiscountAmount, decimal Total);

public sealed record VoucherEvaluation(
    DiscountVoucher? Voucher, decimal Amount, string? Error)
{
    public bool IsSuccess => Voucher is not null && Error is null;
    public static VoucherEvaluation Failure(string message) => new(null, 0, message);
}

public sealed class DiscountVoucherService(ZetruvDbContext db)
{
    public static string Normalize(string? code) =>
        (code ?? "").Trim().ToUpperInvariant();

    public static string? ValidateConfiguration(UpsertDiscountVoucherRequest request)
    {
        var code = Normalize(request.Code);
        if (code.Length is < 4 or > 32 ||
            code.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '-' or '_')))
            return "Code must be 4–32 characters (A–Z, 0–9, - or _).";
        if (!Enum.IsDefined(request.Type)) return "Invalid discount type.";
        if (request.Value <= 0 || request.MinimumSpend < 0 ||
            request.MaximumDiscount is <= 0 ||
            request.MaxUses is <= 0 || request.MaxUsesPerCustomer is <= 0)
            return "Amounts and configured usage limits must be positive.";
        if (request.Type == DiscountVoucherType.Percentage && request.Value > 100)
            return "Percentage must be between 0 and 100.";
        if (request.Type == DiscountVoucherType.Fixed && request.MaximumDiscount.HasValue)
            return "Maximum discount is only available for percentage vouchers.";
        if (request.EndsAt <= request.StartsAt)
            return "End date must be later than start date.";
        if (request.ApplicableKind.HasValue && !Enum.IsDefined(request.ApplicableKind.Value))
            return "Invalid product kind.";
        return null;
    }

    public static string? CustomerKey(string? email, string? phone)
    {
        var identity = !string.IsNullOrWhiteSpace(email)
            ? "email:" + email.Trim().ToLowerInvariant()
            : !string.IsNullOrWhiteSpace(phone)
                ? "phone:" + new string(phone.Where(char.IsAsciiDigit).ToArray())
                : null;
        return identity is null ? null :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public async Task<VoucherEvaluation> EvaluateAsync(
        string? code, string? customerKey, decimal payable,
        IReadOnlyDictionary<ProductKind, decimal> eligibleByKind,
        DateTimeOffset now, CancellationToken ct)
    {
        var normalized = Normalize(code);
        var voucher = await db.DiscountVouchers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Code == normalized, ct);
        if (voucher is null || !voucher.IsActive)
            return VoucherEvaluation.Failure("Voucher code is invalid or inactive.");
        if (now < voucher.StartsAt)
            return VoucherEvaluation.Failure("Voucher is not active yet.");
        if (now >= voucher.EndsAt)
            return VoucherEvaluation.Failure("Voucher has expired.");
        var eligible = voucher.ApplicableKind.HasValue
            ? eligibleByKind.GetValueOrDefault(voucher.ApplicableKind.Value)
            : eligibleByKind.Values.Sum();
        if (eligible <= 0)
            return VoucherEvaluation.Failure("Voucher is not eligible for these products.");
        if (eligible < voucher.MinimumSpend)
            return VoucherEvaluation.Failure("Minimum eligible spend has not been reached.");
        if (voucher.MaxUses.HasValue && voucher.UsedCount >= voucher.MaxUses.Value)
            return VoucherEvaluation.Failure("Voucher usage limit has been reached.");
        if (voucher.MaxUsesPerCustomer.HasValue)
        {
            if (customerKey is null)
                return VoucherEvaluation.Failure("Contact information is required to use this voucher.");
            var count = await db.DiscountVoucherRedemptions.AsNoTracking()
                .CountAsync(x => x.VoucherId == voucher.Id &&
                    x.CustomerKey == customerKey && x.ReleasedAt == null, ct);
            if (count >= voucher.MaxUsesPerCustomer.Value)
                return VoucherEvaluation.Failure("Voucher redemption limit reached for this customer.");
        }
        var amount = voucher.Type == DiscountVoucherType.Percentage
            ? decimal.Round(eligible * voucher.Value / 100m, 0, MidpointRounding.AwayFromZero)
            : voucher.Value;
        if (voucher.MaximumDiscount.HasValue)
            amount = Math.Min(amount, voucher.MaximumDiscount.Value);
        amount = Math.Min(amount, Math.Min(eligible, payable));
        if (amount >= payable)
            return VoucherEvaluation.Failure("Voucher would make the payment total zero. This checkout requires a positive amount.");
        return amount <= 0 ? VoucherEvaluation.Failure("Voucher has no applicable discount.")
            : new VoucherEvaluation(voucher, amount, null);
    }

    // Must run inside the order-creation transaction. Serializes claims per code,
    // including the per-customer check, across concurrent app instances.
    public async Task<VoucherEvaluation> ClaimAsync(
        string code, string? customerKey, decimal payable,
        IReadOnlyDictionary<ProductKind, decimal> eligibleByKind,
        Order order, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = Normalize(code);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        var evaluation = await EvaluateAsync(normalized, customerKey, payable, eligibleByKind, now, ct);
        if (!evaluation.IsSuccess) return evaluation;
        var voucher = await db.DiscountVouchers.SingleAsync(x => x.Id == evaluation.Voucher!.Id, ct);
        voucher.UsedCount++;
        voucher.UpdatedAt = now;
        order.VoucherCode = voucher.Code;
        order.VoucherDiscountAmount = evaluation.Amount;
        db.DiscountVoucherRedemptions.Add(new DiscountVoucherRedemption
        {
            Voucher = voucher,
            Order = order,
            CustomerKey = customerKey ?? "",
            Amount = evaluation.Amount,
            ClaimedAt = now
        });
        return evaluation;
    }

    public async Task ReleaseUnpaidCancellationAsync(Guid orderId, CancellationToken ct)
    {
        var item = await db.DiscountVoucherRedemptions
            .AsNoTracking().Where(x => x.OrderId == orderId)
            .Select(x => new { x.VoucherId, x.Voucher.Code, x.ReleasedAt, x.Order.PaymentStatus }).SingleOrDefaultAsync(ct);
        if (item is null || item.ReleasedAt.HasValue ||
            item.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.Failed))
            return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({item.Code}))", ct);
        var changed = await db.DiscountVoucherRedemptions
            .Where(x => x.OrderId == orderId && x.ReleasedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow), ct);
        if (changed == 1)
            await db.DiscountVouchers.Where(x => x.Id == item.VoucherId && x.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedCount, x => x.UsedCount - 1), ct);
        await tx.CommitAsync(ct);
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/discount-vouchers")]
public sealed class CmsDiscountVouchersController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DiscountVoucherResponse>>> List(CancellationToken ct) =>
        Ok(await db.DiscountVouchers.AsNoTracking().OrderByDescending(x => x.CreatedAt)
            .Select(x => new DiscountVoucherResponse(x.Id, x.Code, x.Type, x.Value,
                x.MaximumDiscount, x.MinimumSpend, x.ApplicableKind,
                x.MaxUses, x.MaxUsesPerCustomer, x.UsedCount, x.IsActive,
                x.StartsAt, x.EndsAt, x.CreatedAt, x.UpdatedAt)
            {
                HasClaims = db.DiscountVoucherRedemptions.Any(r => r.VoucherId == x.Id)
            }).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(UpsertDiscountVoucherRequest request, CancellationToken ct)
    {
        var problem = DiscountVoucherService.ValidateConfiguration(request);
        if (problem is not null) return BadRequest(new { message = problem });
        var code = DiscountVoucherService.Normalize(request.Code);
        if (await db.DiscountVouchers.AnyAsync(x => x.Code == code, ct))
            return Conflict(new { message = "Voucher code already exists." });
        var voucher = new DiscountVoucher();
        Apply(voucher, request);
        db.DiscountVouchers.Add(voucher);
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/cms/discount-vouchers/{voucher.Id}", voucher.Id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpsertDiscountVoucherRequest request, CancellationToken ct)
    {
        var problem = DiscountVoucherService.ValidateConfiguration(request);
        if (problem is not null) return BadRequest(new { message = problem });
        var voucher = await db.DiscountVouchers.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (voucher is null) return NotFound();
        var code = DiscountVoucherService.Normalize(request.Code);
        if (await db.DiscountVouchers.AnyAsync(x => x.Id != id && x.Code == code, ct))
            return Conflict(new { message = "Voucher code already exists." });
        var hasClaims = await db.DiscountVoucherRedemptions.AnyAsync(x => x.VoucherId == id, ct);
        if (hasClaims &&
            (voucher.Code != code || voucher.Type != request.Type ||
             voucher.Value != request.Value || voucher.MaximumDiscount != request.MaximumDiscount ||
             voucher.MinimumSpend != request.MinimumSpend ||
             voucher.ApplicableKind != request.ApplicableKind))
            return Conflict(new { message = "Cannot change code or discount rules after an order has claimed the voucher. Disable it and create another code." });
        if (request.MaxUses.HasValue && request.MaxUses < voucher.UsedCount)
            return Conflict(new { message = "Usage limit cannot be below claimed uses." });
        if (hasClaims && voucher.MaxUsesPerCustomer != request.MaxUsesPerCustomer)
            return Conflict(new { message = "Per-customer usage rules cannot change after voucher claims. Disable and create another code." });
        Apply(voucher, request);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        var voucher = await db.DiscountVouchers.FindAsync([id], ct);
        if (voucher is null) return NotFound();
        voucher.IsActive = false;
        voucher.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(DiscountVoucher voucher, UpsertDiscountVoucherRequest request)
    {
        voucher.Code = DiscountVoucherService.Normalize(request.Code);
        voucher.Type = request.Type;
        voucher.Value = request.Value;
        voucher.MaximumDiscount = request.MaximumDiscount;
        voucher.MinimumSpend = request.MinimumSpend;
        voucher.ApplicableKind = request.ApplicableKind;
        voucher.MaxUses = request.MaxUses;
        voucher.MaxUsesPerCustomer = request.MaxUsesPerCustomer;
        voucher.IsActive = request.IsActive;
        voucher.StartsAt = request.StartsAt;
        voucher.EndsAt = request.EndsAt;
        voucher.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

[ApiController]
[Route("api/v1/checkout/vouchers")]
public sealed class PublicDiscountVoucherController(ZetruvDbContext db, DiscountVoucherService vouchers) : ControllerBase
{
    [HttpPost("preview")]
    [EnableRateLimiting("voucher-preview")]
    public async Task<IActionResult> Preview(PreviewDiscountVoucherRequest request, CancellationToken ct)
    {
        var inputs = request.Items;
        if (inputs is null || inputs.Count is < 1 or > 50 ||
            inputs.Any(x => x.Quantity is < 1 or > 99))
            return BadRequest(new { message = "Provide 1–50 valid cart items." });
        var ids = inputs.Select(x => x.ProductVariantId).Distinct().ToArray();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new {
                x.Id, x.Price, x.StockQuantity, x.IsActive,
                Kind = x.Product.Kind,
                ProductActive = x.Product.IsActive && x.Product.Category.IsActive &&
                    (x.Product.Game == null || x.Product.Game.IsActive)
            }).ToListAsync(ct);
        if (variants.Count != ids.Length)
            return BadRequest(new { message = "Cart contains an unknown SKU." });
        var byId = variants.ToDictionary(x => x.Id);
        if (inputs.Any(x => !byId[x.ProductVariantId].IsActive ||
            !byId[x.ProductVariantId].ProductActive ||
            (byId[x.ProductVariantId].StockQuantity.HasValue &&
             byId[x.ProductVariantId].StockQuantity.GetValueOrDefault() <
                 inputs.Where(i => i.ProductVariantId == x.ProductVariantId).Sum(i => i.Quantity))))
            return BadRequest(new { message = "One or more cart items are unavailable." });
        var now = DateTimeOffset.UtcNow;
        var salePrices = await db.PromotionItems.AsNoTracking()
            .Where(x => ids.Contains(x.ProductVariantId) &&
                x.Promotion.IsActive && x.Promotion.IsFlashSale &&
                x.Promotion.StartsAt <= now && x.Promotion.EndsAt >= now &&
                x.SalePrice >= 0 && x.SalePrice <= x.ProductVariant.Price)
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new { x.Key, SalePrice = x.Min(i => i.SalePrice) })
            .ToDictionaryAsync(x => x.Key, x => x.SalePrice, ct);
        var eligible = new Dictionary<ProductKind, decimal>();
        foreach (var item in inputs)
        {
            var v = byId[item.ProductVariantId];
            var unit = salePrices.TryGetValue(v.Id, out var sale) ? sale : v.Price;
            eligible[v.Kind] = eligible.GetValueOrDefault(v.Kind) + unit * item.Quantity;
        }
        var subtotal = eligible.Values.Sum();
        var evaluation = await vouchers.EvaluateAsync(
            request.Code, DiscountVoucherService.CustomerKey(request.CustomerEmail, request.CustomerPhone),
            subtotal, eligible, now, ct);
        if (!evaluation.IsSuccess) return BadRequest(new { message = evaluation.Error });
        return Ok(new PreviewDiscountVoucherResponse(
            evaluation.Voucher!.Code, subtotal,
            evaluation.Voucher.ApplicableKind.HasValue
                ? eligible.GetValueOrDefault(evaluation.Voucher.ApplicableKind.Value) : subtotal,
            evaluation.Amount, subtotal - evaluation.Amount));
    }
}
