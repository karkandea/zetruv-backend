using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

public sealed record InitiatePaymentResponse(
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ProviderReference,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    string? QrString,
    DateTimeOffset? ExpiresAt,
    PaymentStatus PaymentStatus,
    bool IsRecovery);

public sealed record InitiatePaymentResult(
    InitiatePaymentResponse? Payment,
    string? Error,
    bool IsConfigurationError = false)
{
    public static InitiatePaymentResult Success(InitiatePaymentResponse payment) =>
        new(payment, null);

    public static InitiatePaymentResult Failure(
        string error,
        bool isConfigurationError = false) =>
        new(null, error, isConfigurationError);
}

public sealed record ReconcilePaymentResponse(
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ProviderReference,
    PaymentWebhookStatus WebhookStatus,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus);

public sealed record ReconcilePaymentResult(
    ReconcilePaymentResponse? Payment,
    string? Error,
    bool IsConfigurationError = false,
    bool IsNotFound = false,
    bool IsConflict = false)
{
    public static ReconcilePaymentResult Success(ReconcilePaymentResponse payment) =>
        new(payment, null);

    public static ReconcilePaymentResult Failure(
        string error,
        bool isConfigurationError = false,
        bool isNotFound = false,
        bool isConflict = false) =>
        new(null, error, isConfigurationError, isNotFound, isConflict);
}

public sealed class PaymentService(
    ZetruvDbContext db,
    PaymentGatewayResolver gatewayResolver,
    InventoryReservationService inventoryReservations,
    OrderFulfillmentService fulfillmentService,
    FulfillmentExecutionService executionService,
    IOptions<PaymentReconciliationOptions> reconciliationOptions,
    ILogger<PaymentService> logger)
{
    public async Task<InitiatePaymentResult> InitiateAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var (lockKey1, lockKey2) = PaymentLockKeys(orderId);
        await SetPaymentLockAsync(
            connection,
            acquire: true,
            lockKey1,
            lockKey2,
            cancellationToken);

        try
        {
            return await InitiateLockedAsync(orderId, cancellationToken);
        }
        finally
        {
            try
            {
                await SetPaymentLockAsync(
                    connection,
                    acquire: false,
                    lockKey1,
                    lockKey2,
                    CancellationToken.None);
            }
            finally
            {
                if (openedHere)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
    }

    private async Task<InitiatePaymentResult> InitiateLockedAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(x => x.Items)
                .ThenInclude(x => x.ManualLoginCredential)
            .Include(x => x.Transactions)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return InitiatePaymentResult.Failure("Order was not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return InitiatePaymentResult.Failure("Cancelled orders cannot be paid.");
        }

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return InitiatePaymentResult.Failure("Order is already paid.");
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            return InitiatePaymentResult.Failure("Refunded orders cannot initiate a new payment.");
        }

        if (order.GrandTotal <= 0)
        {
            return InitiatePaymentResult.Failure("Order total must be greater than zero.");
        }

        var now = DateTimeOffset.UtcNow;
        var unavailableManualLogin = order.Items.FirstOrDefault(x =>
            x.FulfillmentMethod == FulfillmentMethod.MANUAL_LOGIN &&
            !ManualLoginCredentialService.IsUsable(x.ManualLoginCredential, now));
        if (unavailableManualLogin is not null)
        {
            return InitiatePaymentResult.Failure(
                $"Login credentials for {unavailableManualLogin.ProductName} expired or were cleared. Create a new order before paying.");
        }

        var pendingPayments = order.Transactions
            .Where(x =>
                x.Type == PaymentTransactionType.Payment &&
                x.Status == PaymentTransactionStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var expiredPayments = pendingPayments
            .Where(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value <= now)
            .ToList();

        foreach (var expired in expiredPayments)
        {
            expired.Status = PaymentTransactionStatus.Failed;
            expired.ProcessedAt ??= now;
            expired.UpdatedAt = now;
        }

        var activePayment = pendingPayments.FirstOrDefault(x =>
            x.Status == PaymentTransactionStatus.Pending &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now));

        if (activePayment is not null)
        {
            order.PaymentProvider = activePayment.Provider;
            order.PaymentReference = activePayment.ProviderReference;
            order.PaymentStatus = PaymentStatus.Pending;
            order.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            return InitiatePaymentResult.Success(
                ToInitiateResponse(order, activePayment, isRecovery: true));
        }

        if (expiredPayments.Count > 0 && order.PaymentStatus == PaymentStatus.Pending)
        {
            order.PaymentStatus = PaymentStatus.Failed;
            order.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
        }

        var gateway = gatewayResolver.Resolve();
        if (gateway is null)
        {
            return InitiatePaymentResult.Failure(
                "Payment provider is not configured.",
                isConfigurationError: true);
        }

        var reservation = await inventoryReservations.ReserveAsync(order, cancellationToken);
        if (!reservation.IsSuccess)
        {
            return InitiatePaymentResult.Failure(
                reservation.Error ?? "Inventory could not be reserved.");
        }

        var gatewayResult = await gateway.CreatePaymentAsync(
            new PaymentGatewayCreateRequest(
                order.Id,
                order.OrderNumber,
                order.GrandTotal,
                order.Currency,
                order.CustomerName,
                order.CustomerEmail,
                order.CustomerPhone),
            cancellationToken);

        if (!gatewayResult.IsSuccess ||
            string.IsNullOrWhiteSpace(gatewayResult.ProviderReference))
        {
            await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
            return InitiatePaymentResult.Failure(
                gatewayResult.Error ?? "Payment provider failed to create a payment.");
        }

        if (gatewayResult.ExpiresAt.HasValue && gatewayResult.ExpiresAt.Value <= now)
        {
            await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
            return InitiatePaymentResult.Failure(
                "Payment provider returned an already expired payment session.");
        }

        var paymentTransaction = new PaymentTransaction
        {
            OrderId = order.Id,
            Provider = gateway.Name,
            ProviderReference = gatewayResult.ProviderReference.Trim(),
            Type = PaymentTransactionType.Payment,
            Status = PaymentTransactionStatus.Pending,
            Amount = order.GrandTotal,
            Currency = order.Currency,
            PaymentUrl = Clean(gatewayResult.PaymentUrl),
            QrString = Clean(gatewayResult.QrString, trim: false),
            ExpiresAt = gatewayResult.ExpiresAt,
            NextReconciliationAt = now.AddSeconds(
                Math.Clamp(reconciliationOptions.Value.InitialDelaySeconds, 5, 3600)),
            CreatedAt = now,
            UpdatedAt = now
        };

        order.PaymentProvider = gateway.Name;
        order.PaymentReference = paymentTransaction.ProviderReference;
        order.PaymentStatus = PaymentStatus.Pending;
        order.UpdatedAt = now;

        db.PaymentTransactions.Add(paymentTransaction);
        await db.SaveChangesAsync(cancellationToken);

        return InitiatePaymentResult.Success(
            ToInitiateResponse(order, paymentTransaction, isRecovery: false));
    }

    public async Task<ReconcilePaymentResult> ReconcileWebhookAsync(
        string provider,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var gateway = gatewayResolver.ResolveByName(provider);
        if (gateway is null)
        {
            return ReconcilePaymentResult.Failure(
                "Payment provider is not supported.",
                isConfigurationError: true);
        }

        var parsed = await gateway.ParseWebhookAsync(rawBody, headers, cancellationToken);
        if (parsed.Notification is null)
        {
            return ReconcilePaymentResult.Failure(
                parsed.Error ?? "Webhook could not be verified.",
                parsed.IsConfigurationError);
        }

        return await ApplyNotificationWithOrderLockAsync(
            gateway.Name,
            parsed.Notification,
            cancellationToken);
    }

    private async Task<ReconcilePaymentResult> ApplyNotificationWithOrderLockAsync(
        string gatewayName,
        PaymentWebhookNotification notification,
        CancellationToken cancellationToken)
    {
        var orderId = await db.PaymentTransactions
            .AsNoTracking()
            .Where(x =>
                x.Provider == gatewayName &&
                x.ProviderReference == notification.ProviderReference &&
                x.Type == PaymentTransactionType.Payment)
            .Select(x => (Guid?)x.OrderId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!orderId.HasValue)
        {
            return ReconcilePaymentResult.Failure(
                "Payment transaction was not found.",
                isNotFound: true);
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var (lockKey1, lockKey2) = PaymentLockKeys(orderId.Value);
        await SetPaymentLockAsync(
            connection,
            acquire: true,
            lockKey1,
            lockKey2,
            cancellationToken);

        try
        {
            db.ChangeTracker.Clear();
            return await ApplyNotificationAsync(
                gatewayName,
                notification,
                cancellationToken);
        }
        finally
        {
            try
            {
                await SetPaymentLockAsync(
                    connection,
                    acquire: false,
                    lockKey1,
                    lockKey2,
                    CancellationToken.None);
            }
            finally
            {
                if (openedHere)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
    }

    private async Task<ReconcilePaymentResult> ApplyNotificationAsync(
        string gatewayName,
        PaymentWebhookNotification notification,
        CancellationToken cancellationToken)
    {
        var paymentTransaction = await db.PaymentTransactions
            .Include(x => x.Order)
                .ThenInclude(x => x.Items)
                    .ThenInclude(x => x.ManualLoginCredential)
            .SingleOrDefaultAsync(x =>
                x.Provider == gatewayName &&
                x.ProviderReference == notification.ProviderReference &&
                x.Type == PaymentTransactionType.Payment,
                cancellationToken);

        if (paymentTransaction is null)
        {
            return ReconcilePaymentResult.Failure(
                "Payment transaction was not found.",
                isNotFound: true);
        }

        var order = paymentTransaction.Order;
        if (paymentTransaction.Amount != notification.Amount ||
            !string.Equals(
                paymentTransaction.Currency,
                notification.Currency,
                StringComparison.OrdinalIgnoreCase))
        {
            return ReconcilePaymentResult.Failure(
                "Provider amount or currency does not match the payment transaction.");
        }

        var now = DateTimeOffset.UtcNow;

        switch (notification.Status)
        {
            case PaymentWebhookStatus.Pending:
                break;

            case PaymentWebhookStatus.Paid:
                if (paymentTransaction.Status == PaymentTransactionStatus.Succeeded &&
                    order.PaymentStatus == PaymentStatus.Paid)
                {
                    paymentTransaction.NextReconciliationAt = null;
                    paymentTransaction.ReconciliationMessage = null;
                    await db.SaveChangesAsync(cancellationToken);
                    return ReconcilePaymentResult.Success(
                        new ReconcilePaymentResponse(
                            order.Id,
                            order.OrderNumber,
                            gatewayName,
                            notification.ProviderReference,
                            notification.Status,
                            order.PaymentStatus,
                            order.Status));
                }

                if (order.PaymentStatus == PaymentStatus.Refunded)
                {
                    return ReconcilePaymentResult.Failure(
                        "A refunded order cannot transition back to paid.");
                }

                if (order.Status == OrderStatus.Cancelled)
                {
                    return ReconcilePaymentResult.Failure(
                        "A cancelled order cannot transition to paid.",
                        isConflict: true);
                }

                if (order.PaymentStatus == PaymentStatus.Paid &&
                    paymentTransaction.Status != PaymentTransactionStatus.Succeeded)
                {
                    return ReconcilePaymentResult.Failure(
                        "Order is already paid by another payment transaction. Manual reconciliation is required.",
                        isConflict: true);
                }

                await using (var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken))
                {
                    var inventory = await inventoryReservations.EnsureConsumedForPaidAsync(
                        order,
                        cancellationToken);

                    if (!inventory.IsSuccess)
                    {
                        await dbTransaction.RollbackAsync(cancellationToken);
                        return ReconcilePaymentResult.Failure(
                            inventory.Error ?? "Inventory could not be secured for the paid order.",
                            isConflict: true);
                    }

                    paymentTransaction.Status = PaymentTransactionStatus.Succeeded;
                    paymentTransaction.ProcessedAt ??= now;
                    paymentTransaction.NextReconciliationAt = null;
                    paymentTransaction.ReconciliationMessage = null;
                    paymentTransaction.UpdatedAt = now;
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.PaymentProvider = paymentTransaction.Provider;
                    order.PaymentReference = paymentTransaction.ProviderReference;
                    order.PaidAt ??= now;
                    fulfillmentService.StartPaidOrder(order, now);

                    await db.PaymentTransactions
                        .Where(x =>
                            x.OrderId == order.Id &&
                            x.Id != paymentTransaction.Id &&
                            x.Type == PaymentTransactionType.Payment &&
                            x.Status == PaymentTransactionStatus.Pending)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.Status, PaymentTransactionStatus.Failed)
                            .SetProperty(x => x.ProcessedAt, now)
                            .SetProperty(x => x.NextReconciliationAt, (DateTimeOffset?)null)
                            .SetProperty(x => x.ReconciliationMessage,
                                "Superseded by another succeeded payment transaction.")
                            .SetProperty(x => x.UpdatedAt, now),
                            cancellationToken);

                    await db.SaveChangesAsync(cancellationToken);
                    await dbTransaction.CommitAsync(cancellationToken);
                }

                try
                {
                    await executionService.ExecuteAutoItemsForOrderAsync(
                        order.Id,
                        FulfillmentExecutionContext.Payment,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Automatic fulfillment dispatch failed after payment for order {OrderId}.",
                        order.Id);
                }
                break;

            case PaymentWebhookStatus.Failed:
                if (paymentTransaction.Status != PaymentTransactionStatus.Succeeded)
                {
                    paymentTransaction.Status = PaymentTransactionStatus.Failed;
                    paymentTransaction.ProcessedAt ??= now;
                    paymentTransaction.NextReconciliationAt = null;
                    paymentTransaction.ReconciliationMessage = null;
                    paymentTransaction.UpdatedAt = now;
                }

                var hasSucceededPayment = await db.PaymentTransactions
                    .AnyAsync(x =>
                        x.OrderId == order.Id &&
                        x.Type == PaymentTransactionType.Payment &&
                        x.Status == PaymentTransactionStatus.Succeeded,
                        cancellationToken);

                var hasOtherPendingPayment = await db.PaymentTransactions
                    .AnyAsync(x =>
                        x.OrderId == order.Id &&
                        x.Id != paymentTransaction.Id &&
                        x.Type == PaymentTransactionType.Payment &&
                        x.Status == PaymentTransactionStatus.Pending &&
                        (!x.ExpiresAt.HasValue || x.ExpiresAt > now),
                        cancellationToken);

                if (!hasSucceededPayment)
                {
                    order.PaymentStatus = hasOtherPendingPayment
                        ? PaymentStatus.Pending
                        : PaymentStatus.Failed;
                    order.UpdatedAt = now;
                }

                await db.SaveChangesAsync(cancellationToken);
                if (!hasSucceededPayment && !hasOtherPendingPayment)
                {
                    await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
                }
                break;

            case PaymentWebhookStatus.Refunded:
                if (order.PaymentStatus != PaymentStatus.Paid &&
                    order.PaymentStatus != PaymentStatus.Refunded)
                {
                    return ReconcilePaymentResult.Failure(
                        "Only a paid order can be marked as refunded.");
                }

                paymentTransaction.NextReconciliationAt = null;
                paymentTransaction.ReconciliationMessage = null;
                order.PaymentStatus = PaymentStatus.Refunded;
                order.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                break;
        }

        return ReconcilePaymentResult.Success(
            new ReconcilePaymentResponse(
                order.Id,
                order.OrderNumber,
                gatewayName,
                notification.ProviderReference,
                notification.Status,
                order.PaymentStatus,
                order.Status));
    }

    public async Task<int> ReconcileDueAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = reconciliationOptions.Value;
        if (!settings.Enabled)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var initialDelay = TimeSpan.FromSeconds(
            Math.Clamp(settings.InitialDelaySeconds, 5, 3600));
        var batchSize = Math.Clamp(settings.MaxBatchSize, 1, 200);

        var dueIds = await db.PaymentTransactions
            .AsNoTracking()
            .Where(x =>
                x.Type == PaymentTransactionType.Payment &&
                x.Status == PaymentTransactionStatus.Pending &&
                x.ProviderReference != null &&
                ((x.NextReconciliationAt.HasValue && x.NextReconciliationAt <= now) ||
                 (!x.NextReconciliationAt.HasValue && x.CreatedAt <= now - initialDelay)))
            .OrderBy(x => x.NextReconciliationAt ?? x.CreatedAt)
            .ThenBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var transactionId in dueIds)
        {
            try
            {
                var result = await ReconcileTransactionAsync(transactionId, cancellationToken);
                if (result.Reconciliation is null)
                {
                    logger.LogWarning(
                        "Payment reconciliation failed for transaction {TransactionId}: {Error}",
                        transactionId,
                        result.Error);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Payment reconciliation crashed for transaction {TransactionId}.",
                    transactionId);
            }
        }

        return dueIds.Count;
    }

    public async Task<PaymentReconciliationAttemptResult> ReconcileTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var orderId = await db.PaymentTransactions
            .AsNoTracking()
            .Where(x => x.Id == transactionId)
            .Select(x => (Guid?)x.OrderId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!orderId.HasValue)
        {
            return PaymentReconciliationAttemptResult.Failure(
                "Payment transaction was not found.",
                isNotFound: true);
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var (lockKey1, lockKey2) = PaymentLockKeys(orderId.Value);
        await SetPaymentLockAsync(
            connection,
            acquire: true,
            lockKey1,
            lockKey2,
            cancellationToken);

        try
        {
            db.ChangeTracker.Clear();
            return await ReconcileTransactionLockedAsync(transactionId, cancellationToken);
        }
        finally
        {
            try
            {
                await SetPaymentLockAsync(
                    connection,
                    acquire: false,
                    lockKey1,
                    lockKey2,
                    CancellationToken.None);
            }
            finally
            {
                if (openedHere)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
    }

    private async Task<PaymentReconciliationAttemptResult> ReconcileTransactionLockedAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.PaymentTransactions
            .AsNoTracking()
            .Where(x => x.Id == transactionId)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.Order.OrderNumber,
                x.Provider,
                x.ProviderReference,
                x.Type,
                x.Status,
                x.Amount,
                x.Currency,
                x.ReconciliationAttemptCount
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return PaymentReconciliationAttemptResult.Failure(
                "Payment transaction was not found.",
                isNotFound: true);
        }

        if (snapshot.Type != PaymentTransactionType.Payment)
        {
            return PaymentReconciliationAttemptResult.Failure(
                "Only payment transactions can be reconciled with the provider.",
                isConflict: true);
        }

        if (snapshot.Status != PaymentTransactionStatus.Pending)
        {
            return PaymentReconciliationAttemptResult.Failure(
                "Only pending payment transactions require provider reconciliation.",
                isConflict: true);
        }

        if (string.IsNullOrWhiteSpace(snapshot.ProviderReference))
        {
            return PaymentReconciliationAttemptResult.Failure(
                "Payment transaction does not have a provider reference.",
                isConflict: true);
        }

        var now = DateTimeOffset.UtcNow;
        var gateway = gatewayResolver.ResolveByName(snapshot.Provider);
        if (gateway is null)
        {
            const string error = "Payment provider adapter is not configured for reconciliation.";
            await RecordReconciliationAttemptAsync(
                snapshot.Id,
                now,
                NextReconciliationAt(now, snapshot.ReconciliationAttemptCount + 1),
                error,
                cancellationToken);
            return PaymentReconciliationAttemptResult.Failure(
                error,
                isConfigurationError: true);
        }

        PaymentGatewayStatusResult providerResult;
        try
        {
            providerResult = await gateway.GetPaymentStatusAsync(
                new PaymentGatewayStatusRequest(
                    snapshot.OrderId,
                    snapshot.OrderNumber,
                    snapshot.ProviderReference,
                    snapshot.Amount,
                    snapshot.Currency),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Payment provider {Provider} status query failed for transaction {TransactionId}.",
                gateway.Name,
                snapshot.Id);

            const string error = "Payment provider status query is temporarily unavailable.";
            await RecordReconciliationAttemptAsync(
                snapshot.Id,
                now,
                NextReconciliationAt(now, snapshot.ReconciliationAttemptCount + 1),
                error,
                cancellationToken);
            return PaymentReconciliationAttemptResult.Failure(error);
        }

        if (providerResult.Notification is null)
        {
            var error = providerResult.Error ?? "Payment provider did not return a transaction status.";
            await RecordReconciliationAttemptAsync(
                snapshot.Id,
                now,
                NextReconciliationAt(now, snapshot.ReconciliationAttemptCount + 1),
                error,
                cancellationToken);
            return PaymentReconciliationAttemptResult.Failure(
                error,
                providerResult.IsConfigurationError);
        }

        var notification = providerResult.Notification;
        if (!string.Equals(
                notification.ProviderReference,
                snapshot.ProviderReference,
                StringComparison.Ordinal) ||
            notification.Amount != snapshot.Amount ||
            !string.Equals(notification.Currency, snapshot.Currency, StringComparison.OrdinalIgnoreCase))
        {
            const string error = "Payment provider status response does not match the payment transaction.";
            await RecordReconciliationAttemptAsync(
                snapshot.Id,
                now,
                nextAttemptAt: null,
                error,
                cancellationToken);
            return PaymentReconciliationAttemptResult.Failure(error, isConflict: true);
        }

        var application = await ApplyNotificationAsync(
            gateway.Name,
            notification,
            cancellationToken);

        var terminal = notification.Status != PaymentWebhookStatus.Pending;
        var applicationFailed = application.Payment is null;
        var message = application.Error ?? $"Provider status: {notification.Status}.";
        DateTimeOffset? nextAttemptAt = terminal || applicationFailed
            ? null
            : NextReconciliationAt(now, snapshot.ReconciliationAttemptCount + 1);

        await RecordReconciliationAttemptAsync(
            snapshot.Id,
            now,
            nextAttemptAt,
            message,
            cancellationToken);

        if (applicationFailed)
        {
            return PaymentReconciliationAttemptResult.Failure(
                application.Error ?? "Payment provider status could not be applied.",
                application.IsConfigurationError,
                application.IsNotFound,
                application.IsConflict);
        }

        var response = await BuildReconciliationResponseAsync(
            snapshot.Id,
            notification.Status,
            cancellationToken);

        return response is null
            ? PaymentReconciliationAttemptResult.Failure(
                "Payment transaction was not found after reconciliation.",
                isNotFound: true)
            : PaymentReconciliationAttemptResult.Success(response);
    }

    private async Task RecordReconciliationAttemptAsync(
        Guid transactionId,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string? message,
        CancellationToken cancellationToken)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim()[..Math.Min(message.Trim().Length, 500)];

        await db.PaymentTransactions
            .Where(x => x.Id == transactionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ReconciliationAttemptCount, x => x.ReconciliationAttemptCount + 1)
                .SetProperty(x => x.LastReconciliationAttemptAt, attemptedAt)
                .SetProperty(x => x.NextReconciliationAt, nextAttemptAt)
                .SetProperty(x => x.ReconciliationMessage, safeMessage)
                .SetProperty(x => x.UpdatedAt, attemptedAt),
                cancellationToken);
    }

    private async Task<PaymentReconciliationAttemptResponse?> BuildReconciliationResponseAsync(
        Guid transactionId,
        PaymentWebhookStatus providerStatus,
        CancellationToken cancellationToken) =>
        await db.PaymentTransactions
            .AsNoTracking()
            .Where(x => x.Id == transactionId)
            .Select(x => new PaymentReconciliationAttemptResponse(
                x.Id,
                x.OrderId,
                x.Order.OrderNumber,
                x.Provider,
                x.ProviderReference!,
                providerStatus,
                x.Status,
                x.Order.PaymentStatus,
                x.Order.Status,
                x.ReconciliationAttemptCount,
                x.LastReconciliationAttemptAt!.Value,
                x.NextReconciliationAt,
                x.ReconciliationMessage))
            .SingleOrDefaultAsync(cancellationToken);

    private DateTimeOffset NextReconciliationAt(DateTimeOffset now, int attemptNumber)
    {
        var settings = reconciliationOptions.Value;
        var baseSeconds = Math.Clamp(settings.BaseBackoffSeconds, 5, 3600);
        var maxSeconds = Math.Clamp(settings.MaxBackoffSeconds, baseSeconds, 86400);
        var exponent = Math.Clamp(attemptNumber - 1, 0, 8);
        var delaySeconds = Math.Min(maxSeconds, baseSeconds * (1 << exponent));
        return now.AddSeconds(delaySeconds);
    }

    private static InitiatePaymentResponse ToInitiateResponse(
        Order order,
        PaymentTransaction transaction,
        bool isRecovery) =>
        new(
            order.Id,
            order.OrderNumber,
            transaction.Provider,
            transaction.ProviderReference ?? string.Empty,
            transaction.Amount,
            transaction.Currency,
            transaction.PaymentUrl,
            transaction.QrString,
            transaction.ExpiresAt,
            order.PaymentStatus,
            isRecovery);

    private static (int Key1, int Key2) PaymentLockKeys(Guid orderId)
    {
        var bytes = orderId.ToByteArray();
        return (
            BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 8),
            BitConverter.ToInt32(bytes, 4) ^ BitConverter.ToInt32(bytes, 12));
    }

    private static async Task SetPaymentLockAsync(
        DbConnection connection,
        bool acquire,
        int key1,
        int key2,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = acquire
            ? "SELECT pg_advisory_lock(@key1, @key2)"
            : "SELECT pg_advisory_unlock(@key1, @key2)";

        var first = command.CreateParameter();
        first.ParameterName = "@key1";
        first.Value = key1;
        command.Parameters.Add(first);

        var second = command.CreateParameter();
        second.ParameterName = "@key2";
        second.Value = key2;
        command.Parameters.Add(second);

        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string? Clean(string? value, bool trim = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return trim ? value.Trim() : value;
    }
}
