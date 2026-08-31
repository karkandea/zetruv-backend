using Microsoft.EntityFrameworkCore;
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

        var existing = await db.InventoryReservations
            .Where(x => x.OrderId == order.Id && x.Status == InventoryReservationStatus.Active)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            return InventoryReservationResult.Success();
        }

        var lines = order.Items
            .Where(x => x.ProductVariantId.HasValue)
            .GroupBy(x => x.ProductVariantId!.Value)
            .Select(x => new
            {
                ProductVariantId = x.Key,
                Quantity = x.Sum(i => i.Quantity)
            })
            .ToList();

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

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InventoryReservationResult.Success();
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
            .Where(x => x.OrderId == orderId && x.Status == InventoryReservationStatus.Active)
            .ToListAsync(cancellationToken);

        await ReleaseReservationsAsync(reservations, cancellationToken);
    }

    public async Task ReleaseExpiredAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await db.InventoryReservations
            .Where(x => x.Status == InventoryReservationStatus.Active && x.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        await ReleaseReservationsAsync(expired, cancellationToken);
    }

    private async Task ReleaseReservationsAsync(
        IReadOnlyList<InventoryReservation> reservations,
        CancellationToken cancellationToken)
    {
        if (reservations.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var reservation in reservations)
        {
            await db.ProductVariants
                .Where(x => x.Id == reservation.ProductVariantId && x.StockQuantity != null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.StockQuantity,
                        x => x.StockQuantity + reservation.Quantity),
                    cancellationToken);

            reservation.Status = InventoryReservationStatus.Released;
            reservation.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
