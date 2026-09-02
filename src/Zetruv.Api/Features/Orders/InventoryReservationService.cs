using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record InventoryReservationResult(bool IsSuccess, string? Error)
{
    public static InventoryReservationResult Success() => new(true, null);
    public static InventoryReservationResult Failure(string error) => new(false, error);
}

public sealed class InventoryReservationService(ZetruvDbContext db)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public async Task<InventoryReservationResult> ReserveAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        await ReleaseExpiredAsync(cancellationToken);

        var reservations = await db.InventoryReservations
            .Where(x => x.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        if (reservations.Any(x => x.Status == InventoryReservationStatus.Active))
        {
            return InventoryReservationResult.Success();
        }

        var reservationsByVariant = reservations.ToDictionary(x => x.ProductVariantId);
        var lines = GetInventoryLines(order);

        if (lines.Count == 0)
        {
            return InventoryReservationResult.Success();
        }

        var variantIds = lines.Select(x => x.ProductVariantId).ToArray();
        var stockByVariant = await db.ProductVariants
            .AsNoTracking()
            .Where(x => variantIds.Contains(x.Id))
            .Select(x => new { x.Id, x.StockQuantity })
            .ToDictionaryAsync(x => x.Id, x => x.StockQuantity, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var line in lines)
        {
            if (!stockByVariant.TryGetValue(line.ProductVariantId, out var stock))
            {
                await transaction.RollbackAsync(cancellationToken);
                return InventoryReservationResult.Failure("A product variant no longer exists.");
            }

            // Null stock means unlimited / non-stock-tracked digital inventory.
            if (!stock.HasValue)
            {
                continue;
            }

            var affected = await db.ProductVariants
                .Where(x =>
                    x.Id == line.ProductVariantId &&
                    x.StockQuantity != null &&
                    x.StockQuantity >= line.Quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.StockQuantity,
                        x => x.StockQuantity - line.Quantity),
                    cancellationToken);

            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return InventoryReservationResult.Failure("Stock changed and is no longer sufficient.");
            }

            if (reservationsByVariant.TryGetValue(line.ProductVariantId, out var existing))
            {
                existing.Quantity = line.Quantity;
                existing.Status = InventoryReservationStatus.Active;
                existing.ExpiresAt = now.Add(DefaultTtl);
                existing.UpdatedAt = now;
            }
            else
            {
                db.InventoryReservations.Add(new InventoryReservation
                {
                    OrderId = order.Id,
                    ProductVariantId = line.ProductVariantId,
                    Quantity = line.Quantity,
                    Status = InventoryReservationStatus.Active,
                    ExpiresAt = now.Add(DefaultTtl),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InventoryReservationResult.Success();
    }

    /// <summary>
    /// Guarantees that stock-backed order lines are owned by this paid order.
    /// Active reservations are atomically consumed. Released reservations are
    /// re-acquired only when stock is still available, preventing a late paid
    /// webhook from overselling inventory.
    /// </summary>
    public async Task<InventoryReservationResult> EnsureConsumedForPaidAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        var lines = GetInventoryLines(order);
        if (lines.Count == 0)
        {
            return InventoryReservationResult.Success();
        }

        var variantIds = lines.Select(x => x.ProductVariantId).ToArray();
        var stockByVariant = await db.ProductVariants
            .AsNoTracking()
            .Where(x => variantIds.Contains(x.Id))
            .Select(x => new { x.Id, x.StockQuantity })
            .ToDictionaryAsync(x => x.Id, x => x.StockQuantity, cancellationToken);

        var reservationByVariant = await db.InventoryReservations
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id && variantIds.Contains(x.ProductVariantId))
            .ToDictionaryAsync(x => x.ProductVariantId, cancellationToken);

        await using var ownedTransaction = await BeginOwnedTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var line in lines)
        {
            if (!stockByVariant.TryGetValue(line.ProductVariantId, out var stock))
            {
                await RollbackOwnedAsync(ownedTransaction, cancellationToken);
                return InventoryReservationResult.Failure("A product variant no longer exists.");
            }

            if (!stock.HasValue)
            {
                continue;
            }

            reservationByVariant.TryGetValue(line.ProductVariantId, out var reservation);

            if (reservation?.Status == InventoryReservationStatus.Consumed)
            {
                continue;
            }

            if (reservation?.Status == InventoryReservationStatus.Active)
            {
                var consumed = await db.InventoryReservations
                    .Where(x =>
                        x.Id == reservation.Id &&
                        x.Status == InventoryReservationStatus.Active)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, InventoryReservationStatus.Consumed)
                        .SetProperty(x => x.UpdatedAt, now),
                        cancellationToken);

                if (consumed == 1)
                {
                    continue;
                }

                reservation = await db.InventoryReservations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == reservation.Id, cancellationToken);

                if (reservation?.Status == InventoryReservationStatus.Consumed)
                {
                    continue;
                }
            }

            // Reservation was released (or missing). Re-acquire stock atomically.
            var stockAcquired = await db.ProductVariants
                .Where(x =>
                    x.Id == line.ProductVariantId &&
                    x.StockQuantity != null &&
                    x.StockQuantity >= line.Quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.StockQuantity,
                        x => x.StockQuantity - line.Quantity),
                    cancellationToken);

            if (stockAcquired != 1)
            {
                await RollbackOwnedAsync(ownedTransaction, cancellationToken);
                return InventoryReservationResult.Failure(
                    "Payment was confirmed after the inventory reservation expired, and stock is no longer available.");
            }

            if (reservation is not null)
            {
                var markedConsumed = await db.InventoryReservations
                    .Where(x =>
                        x.Id == reservation.Id &&
                        x.Status == InventoryReservationStatus.Released)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Quantity, line.Quantity)
                        .SetProperty(x => x.Status, InventoryReservationStatus.Consumed)
                        .SetProperty(x => x.UpdatedAt, now),
                        cancellationToken);

                if (markedConsumed != 1)
                {
                    await RollbackOwnedAsync(ownedTransaction, cancellationToken);
                    return InventoryReservationResult.Failure(
                        "Inventory reservation changed while reconciling the paid order.");
                }
            }
            else
            {
                db.InventoryReservations.Add(new InventoryReservation
                {
                    OrderId = order.Id,
                    ProductVariantId = line.ProductVariantId,
                    Quantity = line.Quantity,
                    Status = InventoryReservationStatus.Consumed,
                    ExpiresAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }

        return InventoryReservationResult.Success();
    }

    public async Task<InventoryReservationResult> EnsureConsumedForPaidAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        return order is null
            ? InventoryReservationResult.Failure("Order was not found.")
            : await EnsureConsumedForPaidAsync(order, cancellationToken);
    }

    public async Task ConsumeAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.InventoryReservations
            .Where(x => x.OrderId == orderId && x.Status == InventoryReservationStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, InventoryReservationStatus.Consumed)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);
    }

    public async Task ReleaseAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var reservations = await db.InventoryReservations
            .AsNoTracking()
            .Where(x => x.OrderId == orderId && x.Status == InventoryReservationStatus.Active)
            .Select(x => new ReservationReleaseCandidate(
                x.Id,
                x.ProductVariantId,
                x.Quantity))
            .ToListAsync(cancellationToken);

        await ReleaseReservationsAsync(
            reservations,
            requireExpired: false,
            cutoff: null,
            cancellationToken);
    }

    public async Task ReleaseExpiredAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await db.InventoryReservations
            .AsNoTracking()
            .Where(x => x.Status == InventoryReservationStatus.Active && x.ExpiresAt <= now)
            .Select(x => new ReservationReleaseCandidate(
                x.Id,
                x.ProductVariantId,
                x.Quantity))
            .ToListAsync(cancellationToken);

        await ReleaseReservationsAsync(
            expired,
            requireExpired: true,
            cutoff: now,
            cancellationToken);
    }

    private async Task ReleaseReservationsAsync(
        IReadOnlyList<ReservationReleaseCandidate> reservations,
        bool requireExpired,
        DateTimeOffset? cutoff,
        CancellationToken cancellationToken)
    {
        if (reservations.Count == 0)
        {
            return;
        }

        await using var ownedTransaction = await BeginOwnedTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var reservation in reservations)
        {
            var query = db.InventoryReservations.Where(x =>
                x.Id == reservation.Id &&
                x.Status == InventoryReservationStatus.Active);

            if (requireExpired && cutoff.HasValue)
            {
                var expiresAt = cutoff.Value;
                query = query.Where(x => x.ExpiresAt <= expiresAt);
            }

            // Claim the release first. Only the instance that changes Active -> Released
            // is allowed to restore stock, so concurrent cleanup cannot double-release.
            var claimed = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, InventoryReservationStatus.Released)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

            if (claimed != 1)
            {
                continue;
            }

            await db.ProductVariants
                .Where(x => x.Id == reservation.ProductVariantId && x.StockQuantity != null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.StockQuantity,
                        x => x.StockQuantity + reservation.Quantity),
                    cancellationToken);
        }

        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<IDbContextTransaction?> BeginOwnedTransactionAsync(
        CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task RollbackOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static List<InventoryLine> GetInventoryLines(Order order) =>
        order.Items
            .Where(x => x.ProductVariantId.HasValue)
            .GroupBy(x => x.ProductVariantId!.Value)
            .Select(x => new InventoryLine(
                x.Key,
                x.Sum(i => i.Quantity)))
            .ToList();

    private sealed record InventoryLine(Guid ProductVariantId, int Quantity);

    private sealed record ReservationReleaseCandidate(
        Guid Id,
        Guid ProductVariantId,
        int Quantity);
}
