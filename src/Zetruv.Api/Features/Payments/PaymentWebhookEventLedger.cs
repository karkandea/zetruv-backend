using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

public sealed record PaymentWebhookEventBeginResult(
    PaymentWebhookEvent? Event,
    ReconcilePaymentResult? Replay);

public sealed class PaymentWebhookEventLedger(ZetruvDbContext db)
{
    public static (int Key1, int Key2) LockKeys(string provider, string eventId)
    {
        var identity = $"{provider.Trim().ToLowerInvariant()}\n{eventId.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return (BitConverter.ToInt32(hash, 0), BitConverter.ToInt32(hash, 4));
    }

    public static string Fingerprint(PaymentWebhookNotification notification)
    {
        var canonical = string.Join(
            "\n",
            notification.ProviderReference.Trim(),
            notification.Status.ToString(),
            notification.Amount.ToString("0.############################", CultureInfo.InvariantCulture),
            notification.Currency.Trim().ToUpperInvariant());

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
    public async Task<PaymentWebhookEventBeginResult> BeginAsync(
        Guid orderId,
        string provider,
        PaymentWebhookNotification notification,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var eventId = notification.EventId!.Trim();

        var existing = await db.PaymentWebhookEvents
            .SingleOrDefaultAsync(x =>
                x.Provider == provider &&
                x.ProviderEventId == eventId,
                cancellationToken);

        if (existing is not null)
        {
            existing.DeliveryCount++;
            existing.LastReceivedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            if (!string.Equals(existing.ProviderReference, notification.ProviderReference, StringComparison.Ordinal) ||
                !string.Equals(existing.EventFingerprintSha256, fingerprint, StringComparison.Ordinal))
            {
                return new(existing, ReconcilePaymentResult.Failure(
                    "Provider webhook event ID was reused with a different payload.",
                    isConflict: true));
            }
            if (existing.Outcome == PaymentWebhookEventOutcome.Applied)
            {
                return new(
                    existing,
                    await BuildReplaySuccessAsync(existing, cancellationToken));
            }

            if (existing.Outcome == PaymentWebhookEventOutcome.Rejected)
            {
                return new(existing, ReconcilePaymentResult.Failure(
                    existing.ResultMessage ?? "Provider webhook event was previously rejected.",
                    existing.IsConfigurationError,
                    existing.IsNotFound,
                    existing.IsConflict));
            }

            return new(existing, null);
        }

        var webhookEvent = new PaymentWebhookEvent
        {
            OrderId = orderId,
            Provider = provider,
            ProviderEventId = eventId,
            ProviderReference = notification.ProviderReference,
            EventFingerprintSha256 = fingerprint,
            WebhookStatus = notification.Status,
            ReceivedAt = now,
            LastReceivedAt = now
        };

        db.PaymentWebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync(cancellationToken);
        return new(webhookEvent, null);
    }
    public async Task CompleteAsync(
        PaymentWebhookEvent webhookEvent,
        ReconcilePaymentResult result,
        CancellationToken cancellationToken)
    {
        webhookEvent.Outcome = result.Payment is not null
            ? PaymentWebhookEventOutcome.Applied
            : PaymentWebhookEventOutcome.Rejected;
        webhookEvent.CompletedAt = DateTimeOffset.UtcNow;
        webhookEvent.ResultMessage = CleanMessage(result.Error);
        webhookEvent.IsConfigurationError = result.IsConfigurationError;
        webhookEvent.IsNotFound = result.IsNotFound;
        webhookEvent.IsConflict = result.IsConflict;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ReconcilePaymentResult> BuildReplaySuccessAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        var payment = await db.PaymentTransactions
            .AsNoTracking()
            .Where(x =>
                x.OrderId == webhookEvent.OrderId &&
                x.Provider == webhookEvent.Provider &&
                x.ProviderReference == webhookEvent.ProviderReference)
            .Select(x => new ReconcilePaymentResponse(
                x.OrderId,
                x.Order.OrderNumber,
                x.Provider,
                x.ProviderReference!,
                webhookEvent.WebhookStatus,
                x.Order.PaymentStatus,
                x.Order.Status))
            .SingleOrDefaultAsync(cancellationToken);

        return payment is null
            ? ReconcilePaymentResult.Failure(
                "Payment transaction was not found for the processed webhook event.",
                isNotFound: true)
            : ReconcilePaymentResult.Success(payment);
    }

    private static string? CleanMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var trimmed = message.Trim();
        return trimmed[..Math.Min(trimmed.Length, 500)];
    }
}
