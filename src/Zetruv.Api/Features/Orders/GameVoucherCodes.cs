using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public enum GameVoucherCodeStatus
{
    Available,
    Assigned,
    Revoked
}

public sealed class GameVoucherCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public string CodeHash { get; set; } = string.Empty;
    public string? EncryptedCode { get; set; }
    public GameVoucherCodeStatus Status { get; set; } = GameVoucherCodeStatus.Available;
    public Guid? OrderItemId { get; set; }
    public OrderItem? OrderItem { get; set; }
    public int RevealCount { get; set; }
    public DateTimeOffset? LastRevealedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ImportGameVoucherCodesRequest(
    [Required, MinLength(1), MaxLength(500)] IReadOnlyList<string> Codes);

public sealed record ImportGameVoucherCodesResponse(
    int Imported,
    int SkippedDuplicates,
    int SellableStock);

public sealed record CmsGameVoucherCodeResponse(
    Guid Id,
    GameVoucherCodeStatus Status,
    Guid? OrderItemId,
    int RevealCount,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? LastRevealedAt,
    DateTimeOffset CreatedAt);

public sealed record CmsGameVoucherInventoryResponse(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    int SellableStock,
    int AvailableCodeCount,
    int AssignedCodeCount,
    int RevokedCodeCount,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<CmsGameVoucherCodeResponse> Items);

public sealed record GameVoucherAssignmentResult(
    bool IsSuccess,
    string? Error,
    bool NotFound = false,
    bool Conflict = false,
    int AssignedCount = 0)
{
    public static GameVoucherAssignmentResult Success(int count) =>
        new(true, null, AssignedCount: count);

    public static GameVoucherAssignmentResult Missing() =>
        new(false, null, NotFound: true);

    public static GameVoucherAssignmentResult Failure(string error, bool conflict = false) =>
        new(false, error, Conflict: conflict);
}

public sealed record CustomerGameVoucherRevealResponse(
    Guid OrderId,
    Guid OrderItemId,
    IReadOnlyList<string> Codes,
    int RevealCount,
    DateTimeOffset RevealedAt);

public sealed record CustomerGameVoucherRevealResult(
    CustomerGameVoucherRevealResponse? Reveal,
    string? Error,
    bool NotFound = false,
    bool Conflict = false);

public sealed class GameVoucherCodeProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[]? key;

    public GameVoucherCodeProtector(IConfiguration configuration)
    {
        var configured = configuration["ManualLogin:EncryptionKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var master = Convert.FromBase64String(configured);
        if (master.Length != 32)
        {
            throw new FormatException(
                "ManualLogin:EncryptionKey must be a base64-encoded 32-byte key.");
        }

        key = HMACSHA256.HashData(
            master,
            Encoding.UTF8.GetBytes("Zetruv.GameVoucherCode.v1"));
        CryptographicOperations.ZeroMemory(master);
    }

    public bool IsConfigured => key is not null;

    public string Hash(string code)
    {
        EnsureConfigured();
        return Convert.ToHexString(HMACSHA256.HashData(
            key!,
            Encoding.UTF8.GetBytes(code)));
    }

    public string Protect(Guid codeId, string code)
    {
        EnsureConfigured();
        var plaintext = Encoding.UTF8.GetBytes(code);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var aad = Encoding.UTF8.GetBytes($"game-voucher:{codeId:N}");

        using var aes = new AesGcm(key!, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        var envelope = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        envelope[0] = 1;
        Buffer.BlockCopy(nonce, 0, envelope, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, envelope, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(
            ciphertext,
            0,
            envelope,
            1 + NonceSize + TagSize,
            ciphertext.Length);
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String(envelope);
    }

    public string Unprotect(Guid codeId, string protectedCode)
    {
        EnsureConfigured();
        var envelope = Convert.FromBase64String(protectedCode);
        if (envelope.Length <= 1 + NonceSize + TagSize || envelope[0] != 1)
        {
            throw new CryptographicException("Unsupported voucher code payload.");
        }

        var nonce = envelope.AsSpan(1, NonceSize);
        var tag = envelope.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = envelope.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        var aad = Encoding.UTF8.GetBytes($"game-voucher:{codeId:N}");

        using var aes = new AesGcm(key!, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string? NormalizeCode(string? value)
    {
        var code = value?.Trim();
        if (string.IsNullOrWhiteSpace(code) ||
            code.Length is < 4 or > 500 ||
            code.Any(char.IsControl))
        {
            return null;
        }

        return code;
    }

    private void EnsureConfigured()
    {
        if (key is null)
        {
            throw new InvalidOperationException(
                "Voucher code encryption is unavailable because ManualLogin:EncryptionKey is not configured.");
        }
    }
}

public sealed class GameVoucherCodeService(
    ZetruvDbContext db,
    GameVoucherCodeProtector protector,
    OrderFulfillmentService fulfillmentService,
    FulfillmentActivityService activities)
{
    private const string ShortageReference = "GAME_VOUCHER_CODE_SHORTAGE";

    public async Task<GameVoucherAssignmentResult> AssignForPaidOrderAsync(
        Guid orderId,
        FulfillmentExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        var itemIds = await db.OrderItems
            .AsNoTracking()
            .Where(x =>
                x.OrderId == orderId &&
                x.ProductKind == ProductKind.GameVoucher &&
                x.FulfillmentStatus == FulfillmentStatus.Processing)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assigned = 0;
        foreach (var itemId in itemIds)
        {
            var result = await AssignItemAsync(
                orderId,
                itemId,
                allowFailedRetry: false,
                executionContext ?? FulfillmentExecutionContext.System,
                cancellationToken);
            if (result.IsSuccess)
            {
                assigned += result.AssignedCount;
            }
        }

        return GameVoucherAssignmentResult.Success(assigned);
    }

    public async Task<GameVoucherAssignmentResult> AssignItemAsync(
        Guid orderId,
        Guid orderItemId,
        bool allowFailedRetry,
        FulfillmentExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        var candidate = await db.OrderItems
            .AsNoTracking()
            .Where(x => x.Id == orderItemId && x.OrderId == orderId)
            .Select(x => new { x.ProductKind, x.ProductVariantId })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return GameVoucherAssignmentResult.Missing();
        }

        if (candidate.ProductKind != ProductKind.GameVoucher ||
            !candidate.ProductVariantId.HasValue)
        {
            return GameVoucherAssignmentResult.Failure(
                "Only Game Voucher order items can allocate voucher codes.",
                conflict: true);
        }

        if (!protector.IsConfigured)
        {
            return GameVoucherAssignmentResult.Failure(
                "Voucher code encryption is not configured.",
                conflict: true);
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"game-voucher:{candidate.ProductVariantId.Value:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))",
            cancellationToken);

        db.ChangeTracker.Clear();
        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return GameVoucherAssignmentResult.Missing();
        }

        var item = order.Items.SingleOrDefault(x => x.Id == orderItemId);
        if (item is null || !item.ProductVariantId.HasValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return GameVoucherAssignmentResult.Missing();
        }

        if (order.PaymentStatus != PaymentStatus.Paid ||
            order.Status == OrderStatus.Cancelled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return GameVoucherAssignmentResult.Failure(
                "Game Voucher codes can only be allocated to a paid active order.",
                conflict: true);
        }

        if (item.FulfillmentStatus == FulfillmentStatus.Completed)
        {
            var alreadyAssigned = await db.GameVoucherCodes
                .CountAsync(x =>
                    x.OrderItemId == item.Id &&
                    x.Status == GameVoucherCodeStatus.Assigned,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return GameVoucherAssignmentResult.Success(alreadyAssigned);
        }

        var isRetry = item.FulfillmentStatus == FulfillmentStatus.Failed;
        if (isRetry && (!allowFailedRetry ||
            item.FulfillmentReference != ShortageReference))
        {
            await transaction.RollbackAsync(cancellationToken);
            return GameVoucherAssignmentResult.Failure(
                "This failed fulfillment is not eligible for voucher-code retry.",
                conflict: true);
        }

        if (item.FulfillmentStatus is not (FulfillmentStatus.Processing or FulfillmentStatus.Failed))
        {
            await transaction.RollbackAsync(cancellationToken);
            return GameVoucherAssignmentResult.Failure(
                "Game Voucher fulfillment is not executable in the current state.",
                conflict: true);
        }

        var availableCodes = await db.GameVoucherCodes
            .Where(x =>
                x.ProductVariantId == item.ProductVariantId.Value &&
                x.Status == GameVoucherCodeStatus.Available)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(item.Quantity)
            .ToListAsync(cancellationToken);

        if (availableCodes.Count < item.Quantity)
        {
            var nowFailed = DateTimeOffset.UtcNow;
            var previous = item.FulfillmentStatus;
            item.FulfillmentStatus = FulfillmentStatus.Failed;
            item.FulfillmentReference = ShortageReference;
            item.FulfillmentMessage =
                "Paid Game Voucher order could not allocate enough encrypted voucher codes.";
            item.FulfillmentAttemptCount++;
            item.LastFulfillmentAttemptAt = nowFailed;
            activities.Create(
                item,
                FulfillmentActivityType.VoucherCodeAllocationFailed,
                (executionContext ?? FulfillmentExecutionContext.System).Source,
                (executionContext ?? FulfillmentExecutionContext.System).Actor,
                nowFailed,
                previous,
                FulfillmentStatus.Failed,
                item.FulfillmentAttemptCount,
                message: "Voucher code inventory shortage.");
            fulfillmentService.RecalculateOrder(order, nowFailed);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return GameVoucherAssignmentResult.Failure(
                "Voucher code inventory is insufficient for this paid order.");
        }

        if (isRetry)
        {
            var reacquired = await db.ProductVariants
                .Where(x =>
                    x.Id == item.ProductVariantId.Value &&
                    x.StockQuantity != null &&
                    x.StockQuantity >= item.Quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.StockQuantity,
                        x => x.StockQuantity - item.Quantity),
                    cancellationToken);
            if (reacquired != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return GameVoucherAssignmentResult.Failure(
                    "Import enough new sellable voucher codes before retrying allocation.",
                    conflict: true);
            }
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var code in availableCodes)
        {
            code.Status = GameVoucherCodeStatus.Assigned;
            code.OrderItemId = item.Id;
            code.AssignedAt = now;
            code.UpdatedAt = now;
        }

        var fromStatus = item.FulfillmentStatus;
        item.FulfillmentStatus = FulfillmentStatus.Completed;
        item.FulfillmentReference = $"GAME_VOUCHER_CODES:{item.Quantity}";
        item.FulfillmentMessage = null;
        item.FulfillmentStartedAt ??= order.PaidAt ?? now;
        item.FulfilledAt = now;
        item.FulfillmentAttemptCount++;
        item.LastFulfillmentAttemptAt = now;

        var context = executionContext ?? FulfillmentExecutionContext.System;
        activities.Create(
            item,
            FulfillmentActivityType.VoucherCodesAssigned,
            context.Source,
            context.Actor,
            now,
            fromStatus,
            FulfillmentStatus.Completed,
            item.FulfillmentAttemptCount,
            providerReference: item.FulfillmentReference,
            message: $"{item.Quantity} voucher code(s) assigned securely.");

        fulfillmentService.RecalculateOrder(order, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return GameVoucherAssignmentResult.Success(item.Quantity);
    }

    public async Task<CustomerGameVoucherRevealResult> RevealAsync(
        Guid customerId,
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken = default)
    {
        if (!protector.IsConfigured)
        {
            return new(null, "Voucher code encryption is not configured.", Conflict: true);
        }

        var item = await db.OrderItems
            .Include(x => x.Order)
            .SingleOrDefaultAsync(x =>
                x.Id == orderItemId &&
                x.OrderId == orderId &&
                x.Order.CustomerUserId == customerId,
                cancellationToken);

        if (item is null || item.ProductKind != ProductKind.GameVoucher)
        {
            return new(null, null, NotFound: true);
        }

        if (item.Order.PaymentStatus != PaymentStatus.Paid ||
            item.FulfillmentStatus != FulfillmentStatus.Completed)
        {
            return new(
                null,
                "Voucher codes are available only after paid fulfillment is completed.",
                Conflict: true);
        }

        var assigned = await db.GameVoucherCodes
            .Where(x =>
                x.OrderItemId == item.Id &&
                x.Status == GameVoucherCodeStatus.Assigned)
            .OrderBy(x => x.AssignedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (assigned.Count != item.Quantity ||
            assigned.Any(x => string.IsNullOrWhiteSpace(x.EncryptedCode)))
        {
            return new(
                null,
                "Voucher code fulfillment is incomplete. Contact support.",
                Conflict: true);
        }

        var values = assigned
            .Select(x => protector.Unprotect(x.Id, x.EncryptedCode!))
            .ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var code in assigned)
        {
            code.RevealCount++;
            code.LastRevealedAt = now;
            code.UpdatedAt = now;
        }

        activities.Create(
            item,
            FulfillmentActivityType.VoucherCodesRevealed,
            FulfillmentActivitySource.System,
            new FulfillmentActor(customerId.ToString(), item.Order.CustomerEmail),
            now,
            item.FulfillmentStatus,
            item.FulfillmentStatus,
            message: $"{assigned.Count} voucher code(s) revealed to owning customer.");
        await db.SaveChangesAsync(cancellationToken);

        return new(
            new CustomerGameVoucherRevealResponse(
                orderId,
                orderItemId,
                values,
                assigned.Max(x => x.RevealCount),
                now),
            null);
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/catalog/products/{productId:guid}/variants/{variantId:guid}/voucher-codes")]
public sealed class CmsGameVoucherInventoryController(
    ZetruvDbContext db,
    GameVoucherCodeProtector protector) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CmsGameVoucherInventoryResponse>> Get(
        Guid productId,
        Guid variantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var variant = await db.ProductVariants
            .AsNoTracking()
            .Where(x =>
                x.Id == variantId &&
                x.ProductId == productId &&
                x.Product.Kind == ProductKind.GameVoucher)
            .Select(x => new { x.Id, x.Sku, x.StockQuantity })
            .SingleOrDefaultAsync(cancellationToken);
        if (variant is null)
        {
            return NotFound();
        }

        var query = db.GameVoucherCodes
            .AsNoTracking()
            .Where(x => x.ProductVariantId == variantId);
        var total = await query.CountAsync(cancellationToken);
        var available = await query.CountAsync(
            x => x.Status == GameVoucherCodeStatus.Available,
            cancellationToken);
        var assigned = await query.CountAsync(
            x => x.Status == GameVoucherCodeStatus.Assigned,
            cancellationToken);
        var revoked = total - available - assigned;
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CmsGameVoucherCodeResponse(
                x.Id,
                x.Status,
                x.OrderItemId,
                x.RevealCount,
                x.AssignedAt,
                x.LastRevealedAt,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(new CmsGameVoucherInventoryResponse(
            productId,
            variantId,
            variant.Sku,
            variant.StockQuantity ?? 0,
            available,
            assigned,
            revoked,
            page,
            pageSize,
            total,
            (int)Math.Ceiling(total / (double)pageSize),
            items));
    }

    [HttpPost]
    public async Task<ActionResult<ImportGameVoucherCodesResponse>> Import(
        Guid productId,
        Guid variantId,
        ImportGameVoucherCodesRequest request,
        CancellationToken cancellationToken)
    {
        if (!protector.IsConfigured)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Voucher code encryption is not configured." });
        }

        var normalized = request.Codes
            .Select(GameVoucherCodeProtector.NormalizeCode)
            .ToList();
        if (normalized.Any(x => x is null))
        {
            return BadRequest(new
            {
                message = "Each voucher code must contain 4-500 printable characters."
            });
        }

        var distinct = normalized!
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requestDuplicates = normalized.Count - distinct.Count;
        var hashes = distinct.ToDictionary(
            x => x,
            x => protector.Hash(x),
            StringComparer.Ordinal);

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        // CodeHash is globally unique. Serialize CMS imports across variants first,
        // then take the per-variant lock used by other inventory mutations.
        const string globalImportLock = "game-voucher-import-global";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({globalImportLock}))",
            cancellationToken);
        var lockKey = $"game-voucher:{variantId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))",
            cancellationToken);

        var variant = await db.ProductVariants
            .Include(x => x.Product)
            .SingleOrDefaultAsync(
                x => x.Id == variantId && x.ProductId == productId,
                cancellationToken);
        if (variant is null || variant.Product.Kind != ProductKind.GameVoucher)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound();
        }

        var hashValues = hashes.Values.ToArray();
        var existing = await db.GameVoucherCodes
            .AsNoTracking()
            .Where(x => hashValues.Contains(x.CodeHash))
            .Select(x => x.CodeHash)
            .ToHashSetAsync(cancellationToken);

        var imported = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var value in distinct)
        {
            var hash = hashes[value];
            if (existing.Contains(hash))
            {
                continue;
            }

            var entity = new GameVoucherCode
            {
                ProductVariantId = variantId,
                CodeHash = hash,
                Status = GameVoucherCodeStatus.Available,
                CreatedAt = now,
                UpdatedAt = now
            };
            entity.EncryptedCode = protector.Protect(entity.Id, value);
            db.GameVoucherCodes.Add(entity);
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (imported > 0)
        {
            await db.ProductVariants
                .Where(x => x.Id == variantId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.StockQuantity,
                        x => (x.StockQuantity ?? 0) + imported)
                    .SetProperty(x => x.UpdatedAt, now),
                    cancellationToken);
        }

        var sellableStock = await db.ProductVariants
            .AsNoTracking()
            .Where(x => x.Id == variantId)
            .Select(x => x.StockQuantity ?? 0)
            .SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new ImportGameVoucherCodesResponse(
            imported,
            requestDuplicates + existing.Count,
            sellableStock));
    }

    [HttpDelete("{codeId:guid}")]
    public async Task<IActionResult> Revoke(
        Guid productId,
        Guid variantId,
        Guid codeId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"game-voucher:{variantId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))",
            cancellationToken);

        var variant = await db.ProductVariants
            .Include(x => x.Product)
            .SingleOrDefaultAsync(
                x => x.Id == variantId && x.ProductId == productId,
                cancellationToken);
        if (variant is null || variant.Product.Kind != ProductKind.GameVoucher)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound();
        }

        var code = await db.GameVoucherCodes
            .SingleOrDefaultAsync(
                x => x.Id == codeId && x.ProductVariantId == variantId,
                cancellationToken);
        if (code is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound();
        }

        if (code.Status != GameVoucherCodeStatus.Available)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new
            {
                message = "Only an unassigned voucher code can be revoked."
            });
        }

        var now = DateTimeOffset.UtcNow;
        var stockRemoved = await db.ProductVariants
            .Where(x =>
                x.Id == variantId &&
                x.StockQuantity != null &&
                x.StockQuantity > 0)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.StockQuantity, x => x.StockQuantity - 1)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);
        if (stockRemoved != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new
            {
                message = "All unassigned codes are committed to pending orders. Cancel or expire those reservations before revoking inventory."
            });
        }

        code.Status = GameVoucherCodeStatus.Revoked;
        code.EncryptedCode = null;
        code.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/orders")]
public sealed class CmsGameVoucherAssignmentController(
    GameVoucherCodeService service) : ControllerBase
{
    [HttpPost("{orderId:guid}/items/{orderItemId:guid}/game-voucher-assign")]
    public async Task<IActionResult> Retry(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        var result = await service.AssignItemAsync(
            orderId,
            orderItemId,
            allowFailedRetry: true,
            FulfillmentExecutionContext.Admin(User),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(new { assignedCount = result.AssignedCount });
        }

        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Conflict
            ? Conflict(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }
}

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/orders")]
public sealed class CustomerGameVoucherCodesController(
    GameVoucherCodeService service) : ControllerBase
{
    private Guid CustomerId => Guid.Parse(
        User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("{orderId:guid}/items/{orderItemId:guid}/voucher-codes")]
    public async Task<IActionResult> Reveal(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        var result = await service.RevealAsync(
            CustomerId,
            orderId,
            orderItemId,
            cancellationToken);

        if (result.Reveal is not null)
        {
            Response.Headers.CacheControl = "private, no-store";
            return Ok(result.Reveal);
        }

        if (result.NotFound)
        {
            return NotFound();
        }

        return Conflict(new { message = result.Error });
    }
}
