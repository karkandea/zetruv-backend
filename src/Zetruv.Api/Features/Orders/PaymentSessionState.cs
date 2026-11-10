namespace Zetruv.Api.Features.Orders;

// The order's payment aggregate is intentionally separate from its latest
// provider session. An expired attempt does not make the order itself Failed.
public sealed record PaymentSessionSnapshot(
    PaymentTransactionStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset CreatedAt);

public static class PaymentSessionState
{
    public static string Resolve(
        PaymentStatus paymentStatus,
        OrderStatus orderStatus,
        IEnumerable<PaymentSessionSnapshot> sessions,
        DateTimeOffset now)
    {
        if (paymentStatus == PaymentStatus.Paid) return "Paid";
        if (paymentStatus == PaymentStatus.Refunded) return "Refunded";
        if (orderStatus == OrderStatus.Cancelled) return "Cancelled";

        var attempts = sessions.OrderByDescending(x => x.CreatedAt).ToList();
        if (attempts.Any(x => x.Status == PaymentTransactionStatus.Pending &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now))) return "Pending";

        var latest = attempts.FirstOrDefault();
        if (latest is null) return paymentStatus == PaymentStatus.Failed ? "Failed" : "NotStarted";

        if (latest.Status == PaymentTransactionStatus.Expired ||
            (latest.ExpiresAt.HasValue && latest.ExpiresAt.Value <= now &&
             (latest.Status == PaymentTransactionStatus.Pending ||
              (latest.Status == PaymentTransactionStatus.Failed &&
               (!latest.ProcessedAt.HasValue || latest.ProcessedAt >= latest.ExpiresAt)))))
            return "Expired";

        return latest.Status == PaymentTransactionStatus.Failed || paymentStatus == PaymentStatus.Failed
            ? "Failed"
            : "Pending";
    }
}
