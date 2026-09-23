using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Home;

public sealed record LeaderboardEntry(int Rank, string DisplayName, decimal Amount);
public sealed record LeaderboardResponse(
    IReadOnlyList<LeaderboardEntry> Podium,
    IReadOnlyList<LeaderboardEntry> Weekly,
    IReadOnlyList<LeaderboardEntry> Monthly,
    string Currency,
    string TimeZone,
    DateTimeOffset WeekStartsAt,
    DateTimeOffset MonthStartsAt);

[ApiController]
[Route("api/v1/leaderboard")]
public sealed class LeaderboardController(ZetruvDbContext db) : ControllerBase
{
    private static readonly TimeZoneInfo Jakarta =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta");

    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<LeaderboardResponse>> Get(CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jakarta);
        var monday = local.Date.AddDays(-((int)local.DayOfWeek + 6) % 7);
        var firstOfMonth = new DateTime(local.Year, local.Month, 1);
        var weekStart = new DateTimeOffset(monday, Jakarta.GetUtcOffset(monday));
        var monthStart = new DateTimeOffset(firstOfMonth, Jakarta.GetUtcOffset(firstOfMonth));
        var week = await Load(weekStart.ToUniversalTime(), ct);
        var month = await Load(monthStart.ToUniversalTime(), ct);
        return Ok(new LeaderboardResponse(week.Take(3).ToList(), week, month,
            "IDR", "Asia/Jakarta", weekStart, monthStart));
    }

    private async Task<IReadOnlyList<LeaderboardEntry>> Load(
        DateTimeOffset periodStart, CancellationToken ct)
    {
        // Only paid, non-cancelled, non-refunded orders belonging to verified customer accounts.
        // Exclude shipping so rankings reflect product spend after discounts.
        var top = await (
            from order in db.Orders.AsNoTracking()
            join customer in db.CustomerUsers.AsNoTracking()
                on order.CustomerUserId equals (Guid?)customer.Id
            where customer.IsActive && customer.EmailVerifiedAt != null &&
                order.PaymentStatus == PaymentStatus.Paid &&
                order.Status != OrderStatus.Cancelled &&
                order.PaidAt != null && order.PaidAt >= periodStart
            group order by new { customer.Id, customer.Name } into grouped
            select new
            {
                CustomerId = grouped.Key.Id,
                Name = grouped.Key.Name,
                Amount = grouped.Sum(o => o.Subtotal - o.DiscountAmount)
            })
            .Where(x => x.Amount > 0)
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.CustomerId)
            .Take(10)
            .ToListAsync(ct);

        return top.Select((x, rank) => new LeaderboardEntry(rank + 1,
            MaskName(x.Name), x.Amount)).ToList();
    }

    private static string MaskName(string name)
    {
        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        // Never expose a complete personal name or email publicly.
        return first.Length == 0 ? "Player" :
            first.Length == 1 ? first + "***" :
            first[..Math.Min(2, first.Length)] + "***";
    }
}
