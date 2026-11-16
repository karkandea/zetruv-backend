using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Payments;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record AutoIdFulfillmentProviderRequest(
    Guid OrderId,
    string OrderNumber,
    Guid OrderItemId,
    string ProductName,
    string ProductSlug,
    string? VariantName,
    string? ZetruvSku,
    string ProviderSku,
    string? GameName,
    int Quantity,
    string? ValidationProvider,
    string? ValidationProviderReference,
    string? AccountDisplayName,
    string IdempotencyKey,
    IReadOnlyDictionary<string, string> DestinationFields);

public sealed record AutoIdFulfillmentProviderResult(
    bool IsSuccess,
    string? ProviderReference,
    string? Error,
    bool IsPending = false)
{
    public static AutoIdFulfillmentProviderResult Pending(string message) =>
        new(false, null, message, IsPending: true);

    public static AutoIdFulfillmentProviderResult Success(string providerReference) =>
        new(true, providerReference, null);

    public static AutoIdFulfillmentProviderResult Failure(string error) =>
        new(false, null, error);
}

public interface IAutoIdFulfillmentProvider
{
    string Name { get; }

    Task<AutoIdFulfillmentProviderResult> FulfillAsync(
        AutoIdFulfillmentProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MockAutoIdFulfillmentProvider : IAutoIdFulfillmentProvider
{
    public string Name => "mock";

    public Task<AutoIdFulfillmentProviderResult> FulfillAsync(
        AutoIdFulfillmentProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DestinationFields.TryGetValue("simulateFulfillmentFailure", out var failure) &&
            string.Equals(failure, "true", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                AutoIdFulfillmentProviderResult.Failure("Mock AUTO_ID fulfillment failure."));
        }

        return Task.FromResult(
            AutoIdFulfillmentProviderResult.Success(
                $"MOCK-FULFILL-{request.ProviderSku}-{request.OrderItemId:N}"));
    }
}

public sealed class AutoIdFulfillmentProviderResolver(
    IEnumerable<IAutoIdFulfillmentProvider> providers)
{
    public IAutoIdFulfillmentProvider? Resolve(string providerCode) =>
        providers.FirstOrDefault(x =>
            string.Equals(x.Name, providerCode, StringComparison.OrdinalIgnoreCase));
}

public sealed class FulfillmentExecutionService(
    ZetruvDbContext db,
    AutoIdFulfillmentProviderResolver resolver,
    AutoIdRuntimeProviderMappingService providerMappings,
    OrderFulfillmentService fulfillmentService,
    FulfillmentActivityService activities,
    GameVoucherCodeService gameVoucherCodes,
    IOptions<DigiflazzOptions> digiflazzOptions,
    PaymentGatewayResolver paymentGateways,
    ILogger<FulfillmentExecutionService> logger)
{
    public async Task ExecuteAutoItemsForOrderAsync(
        Guid orderId,
        FulfillmentExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        var itemIds = await db.OrderItems
            .AsNoTracking()
            .Where(x =>
                x.OrderId == orderId &&
                x.FulfillmentMethod == FulfillmentMethod.AUTO_ID &&
                x.DigiflazzSku == null &&
                x.FulfillmentStatus == FulfillmentStatus.Processing)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var itemId in itemIds)
        {
            await ExecuteAutoItemAsync(
                orderId,
                itemId,
                executionContext ?? FulfillmentExecutionContext.System,
                cancellationToken);
        }

        await gameVoucherCodes.AssignForPaidOrderAsync(
            orderId,
            executionContext ?? FulfillmentExecutionContext.System,
            cancellationToken);
    }

    public async Task<ExecuteAutoFulfillmentResponse?> ExecuteAutoItemAsync(
        Guid orderId,
        Guid orderItemId,
        FulfillmentExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var (key1, key2) = PaymentService.PaymentLockKeys(orderId);
        var locked = false;
        try
        {
            await PaymentService.SetPaymentLockAsync(connection, true, key1, key2, cancellationToken);
            locked = true;
            return await ExecuteAutoItemLockedAsync(
                orderId,
                orderItemId,
                executionContext ?? FulfillmentExecutionContext.System,
                cancellationToken);
        }
        finally
        {
            try
            {
                if (locked)
                {
                    await PaymentService.SetPaymentLockAsync(
                        connection, false, key1, key2, CancellationToken.None);
                }
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

    private async Task<ExecuteAutoFulfillmentResponse?> ExecuteAutoItemLockedAsync(
        Guid orderId,
        Guid orderItemId,
        FulfillmentExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var order = await db.Orders
            .Include(x => x.Items)
                .ThenInclude(x => x.GameAccountValidation)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var item = order.Items.SingleOrDefault(x => x.Id == orderItemId);
        if (item is null)
        {
            return null;
        }

        if (item.FulfillmentMethod != FulfillmentMethod.AUTO_ID)
        {
            throw new InvalidOperationException("Only AUTO_ID items can be executed automatically.");
        }

        if (order.PaymentStatus != PaymentStatus.Paid ||
            order.Status == OrderStatus.Cancelled ||
            item.FulfillmentStatus is FulfillmentStatus.Completed or FulfillmentStatus.Cancelled)
        {
            throw new InvalidOperationException("AUTO_ID fulfillment is not executable in the current state.");
        }

        var now = DateTimeOffset.UtcNow;
        var digiflazz = item.DigiflazzSku is not null;
        var mappingResult = digiflazz
            ? AutoIdRuntimeProviderMappingResult.Success(new(
                DigiflazzAutoIdFulfillmentProvider.ProviderCode, item.DigiflazzSku!, false))
            : await providerMappings.ResolveAsync(item.ProductVariantId, cancellationToken);
        var mapping = mappingResult.Mapping;
        var provider = mapping is null ? null : resolver.Resolve(mapping.ProviderCode);

        if (digiflazz)
        {
            if (item.LastFulfillmentAttemptAt > now - DigiflazzAutoIdFulfillmentProvider.MinimumCheckInterval)
                return ToResponse(order, item);

            if (item.FulfillmentStatus == FulfillmentStatus.Failed)
                throw new InvalidOperationException("Digiflazz rejected this top up. Review it before trying a replacement.");

            if (order.PaidAt is null || order.PaidAt <= now - DigiflazzAutoIdFulfillmentProvider.MaximumOrderAge)
                return await FailAttemptAsync(order, item, executionContext, item.FulfillmentAttemptCount,
                    DigiflazzAutoIdFulfillmentProvider.ProviderCode,
                    "The Digiflazz status check window expired. Review this order in the Digiflazz dashboard.",
                    cancellationToken);

            var settings = digiflazzOptions.Value;
            if (!settings.IsReady || item.DigiflazzBuyerBinding != settings.BuyerBinding ||
                !paymentGateways.CanPayForDigiflazz(order.PaymentProvider, settings.Testing))
                throw new InvalidOperationException("The original Digiflazz Buyer account and payment mode must be available.");
        }
        else if (string.Equals(mapping?.ProviderCode, DigiflazzAutoIdFulfillmentProvider.ProviderCode,
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This order was created before Digiflazz checkout was configured.");
        }

        var previousStatus = item.FulfillmentStatus;
        if (item.FulfillmentStatus == FulfillmentStatus.Failed)
        {
            item.FulfillmentStatus = FulfillmentStatus.Processing;
            item.FulfillmentMessage = null;
            item.FulfilledAt = null;
        }

        var reference = $"ZTR-FULFILL-{item.Id:N}";
        if (digiflazz)
            item.FulfillmentReference = reference;
        item.FulfillmentStartedAt ??= now;
        item.FulfillmentAttemptCount++;
        item.LastFulfillmentAttemptAt = now;
        var attemptNumber = item.FulfillmentAttemptCount;
        activities.Create(
            item,
            FulfillmentActivityType.ProviderAttemptStarted,
            executionContext.Source,
            executionContext.Actor,
            now,
            previousStatus,
            FulfillmentStatus.Processing,
            attemptNumber,
            mapping?.ProviderCode);
        await db.SaveChangesAsync(cancellationToken);

        if (mapping is null)
        {
            return await FailAttemptAsync(
                order,
                item,
                executionContext,
                attemptNumber,
                null,
                mappingResult.Error ?? "AUTO_ID provider mapping is unavailable.",
                cancellationToken);
        }

        if (provider is null)
        {
            return await FailAttemptAsync(
                order,
                item,
                executionContext,
                attemptNumber,
                mapping.ProviderCode,
                $"AUTO_ID provider adapter '{mapping.ProviderCode}' is not configured.",
                cancellationToken);
        }

        if (item.GameAccountValidation is null)
        {
            return await FailAttemptAsync(
                order,
                item,
                executionContext,
                attemptNumber,
                provider.Name,
                "Verified game account destination is missing.",
                cancellationToken);
        }

        var destinationFields = ParseDestinationFields(item.GameAccountValidation.InputJson);
        if (destinationFields is null || destinationFields.Count == 0)
        {
            return await FailAttemptAsync(
                order,
                item,
                executionContext,
                attemptNumber,
                provider.Name,
                "Verified game account destination is invalid.",
                cancellationToken);
        }

        AutoIdFulfillmentProviderResult result;
        try
        {
            result = await provider.FulfillAsync(
                new AutoIdFulfillmentProviderRequest(
                    order.Id,
                    order.OrderNumber,
                    item.Id,
                    item.ProductName,
                    item.ProductSlug,
                    item.VariantName,
                    item.Sku,
                    mapping.ProviderSku,
                    item.GameName,
                    item.Quantity,
                    item.GameAccountValidation.Provider,
                    item.GameAccountValidation.ProviderReference,
                    item.GameAccountValidation.AccountDisplayName,
                    reference,
                    destinationFields),
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
                "AUTO_ID fulfillment provider {Provider} failed for order item {OrderItemId}.",
                provider.Name,
                item.Id);

            result = digiflazz
                ? AutoIdFulfillmentProviderResult.Pending("Digiflazz has not confirmed the top up yet.")
                : AutoIdFulfillmentProviderResult.Failure("AUTO_ID fulfillment provider is temporarily unavailable.");
        }

        var completedAt = DateTimeOffset.UtcNow;
        if (result.IsPending)
        {
            item.FulfillmentMessage = result.Error;
            activities.Create(
                item, FulfillmentActivityType.ProviderAttemptPending, executionContext.Source,
                executionContext.Actor, completedAt, FulfillmentStatus.Processing,
                FulfillmentStatus.Processing, attemptNumber, provider.Name, item.FulfillmentReference,
                item.FulfillmentMessage);
        }
        else if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.ProviderReference))
        {
            item.FulfillmentStatus = FulfillmentStatus.Completed;
            item.FulfillmentReference = result.ProviderReference.Trim();
            item.FulfillmentMessage = null;
            item.FulfilledAt = completedAt;
            activities.Create(
                item,
                FulfillmentActivityType.ProviderAttemptSucceeded,
                executionContext.Source,
                executionContext.Actor,
                completedAt,
                FulfillmentStatus.Processing,
                FulfillmentStatus.Completed,
                attemptNumber,
                provider.Name,
                item.FulfillmentReference);
        }
        else
        {
            Fail(
                item,
                string.IsNullOrWhiteSpace(result.Error)
                    ? "AUTO_ID fulfillment failed."
                    : result.Error.Trim());
            activities.Create(
                item,
                FulfillmentActivityType.ProviderAttemptFailed,
                executionContext.Source,
                executionContext.Actor,
                completedAt,
                FulfillmentStatus.Processing,
                FulfillmentStatus.Failed,
                attemptNumber,
                provider.Name,
                message: item.FulfillmentMessage);
        }

        fulfillmentService.RecalculateOrder(order, completedAt);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(order, item);
    }

    private async Task<ExecuteAutoFulfillmentResponse> FailAttemptAsync(
        Order order,
        OrderItem item,
        FulfillmentExecutionContext executionContext,
        int attemptNumber,
        string? provider,
        string message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        Fail(item, message);
        activities.Create(
            item,
            FulfillmentActivityType.ProviderAttemptFailed,
            executionContext.Source,
            executionContext.Actor,
            now,
            FulfillmentStatus.Processing,
            FulfillmentStatus.Failed,
            attemptNumber,
            provider,
            message: message);
        fulfillmentService.RecalculateOrder(order, now);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(order, item);
    }

    private static void Fail(OrderItem item, string message)
    {
        item.FulfillmentStatus = FulfillmentStatus.Failed;
        item.FulfillmentMessage = message;
        item.FulfilledAt = null;
    }

    private static IReadOnlyDictionary<string, string>? ParseDestinationFields(string inputJson)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson);
            return parsed is null
                ? null
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExecuteAutoFulfillmentResponse ToResponse(Order order, OrderItem item) =>
        new(
            order.Id,
            item.Id,
            item.FulfillmentStatus,
            item.FulfillmentReference,
            item.FulfillmentMessage,
            item.FulfillmentAttemptCount,
            order.Status);
}

public sealed class FulfillmentQueueService(ZetruvDbContext db)
{
    public async Task<FulfillmentQueueResponse> GetAsync(
        FulfillmentMethod? method,
        FulfillmentStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.OrderItems
            .AsNoTracking()
            .Where(x => x.Order.PaymentStatus == PaymentStatus.Paid);

        if (method.HasValue)
        {
            query = query.Where(x => x.FulfillmentMethod == method.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.FulfillmentStatus == status.Value);
        }
        else
        {
            query = query.Where(x =>
                x.FulfillmentStatus == FulfillmentStatus.Processing ||
                x.FulfillmentStatus == FulfillmentStatus.Failed);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.FulfillmentStatus == FulfillmentStatus.Failed)
            .ThenBy(x => x.FulfillmentStartedAt ?? x.Order.PaidAt ?? x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.OrderId,
                x.Order.OrderNumber,
                OrderItemId = x.Id,
                x.FulfillmentMethod,
                x.FulfillmentStatus,
                x.ProductName,
                x.ProductSlug,
                x.VariantName,
                x.Sku,
                x.GameName,
                x.Quantity,
                x.Order.CustomerName,
                x.Order.CustomerEmail,
                x.Order.CustomerPhone,
                AccountDisplayName = x.GameAccountValidation == null
                    ? null
                    : x.GameAccountValidation.AccountDisplayName,
                DestinationJson = x.GameAccountValidation == null
                    ? null
                    : x.GameAccountValidation.InputJson,
                HasManualLoginCredentials = x.ManualLoginCredential != null &&
                    x.ManualLoginCredential.EncryptedPayload != null,
                ManualLoginCredentialFieldsJson = x.ManualLoginCredential == null
                    ? null
                    : x.ManualLoginCredential.FieldNamesJson,
                ManualLoginCredentialExpiresAt = x.ManualLoginCredential == null
                    ? null
                    : (DateTimeOffset?)x.ManualLoginCredential.ExpiresAt,
                ManualLoginCredentialLastRevealedAt = x.ManualLoginCredential == null
                    ? null
                    : x.ManualLoginCredential.LastRevealedAt,
                x.FulfillmentReference,
                x.FulfillmentMessage,
                x.FulfillmentAttemptCount,
                x.LastFulfillmentAttemptAt,
                x.FulfillmentStartedAt,
                x.FulfilledAt,
                OrderCreatedAt = x.Order.CreatedAt,
                x.Order.PaidAt
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new FulfillmentQueueItemResponse(
            x.OrderId,
            x.OrderNumber,
            x.OrderItemId,
            x.FulfillmentMethod,
            x.FulfillmentStatus,
            x.ProductName,
            x.ProductSlug,
            x.VariantName,
            x.Sku,
            x.GameName,
            x.Quantity,
            x.CustomerName,
            x.CustomerEmail,
            x.CustomerPhone,
            x.AccountDisplayName,
            ParseDestinationFields(x.DestinationJson),
            x.HasManualLoginCredentials,
            x.HasManualLoginCredentials
                ? ManualLoginCredentialService.ParseFieldNames(
                    x.ManualLoginCredentialFieldsJson)
                : null,
            x.ManualLoginCredentialExpiresAt,
            x.ManualLoginCredentialLastRevealedAt,
            x.FulfillmentReference,
            x.FulfillmentMessage,
            x.FulfillmentAttemptCount,
            x.LastFulfillmentAttemptAt,
            x.FulfillmentStartedAt,
            x.FulfilledAt,
            x.OrderCreatedAt,
            x.PaidAt))
            .ToList();

        return new FulfillmentQueueResponse(
            items,
            page,
            pageSize,
            totalItems,
            (int)Math.Ceiling(totalItems / (double)pageSize));
    }

    private static IReadOnlyDictionary<string, string>? ParseDestinationFields(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson);
            return parsed is null
                ? null
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/fulfillment")]
public sealed class CmsFulfillmentController(
    FulfillmentQueueService queueService,
    FulfillmentExecutionService executionService,
    FulfillmentActivityService activityService,
    ManualLoginCredentialService manualLoginCredentials) : ControllerBase
{
    [HttpGet("queue")]
    public async Task<ActionResult<FulfillmentQueueResponse>> GetQueue(
        [FromQuery] FulfillmentMethod? method,
        [FromQuery] FulfillmentStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await queueService.GetAsync(method, status, page, pageSize, cancellationToken));

    [HttpGet("orders/{orderId:guid}/items/{orderItemId:guid}/manual-login-credentials")]
    public async Task<ActionResult<ManualLoginCredentialRevealResponse>> RevealManualLoginCredentials(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        var result = await manualLoginCredentials.RevealAsync(
            orderId,
            orderItemId,
            FulfillmentExecutionContext.Admin(User),
            cancellationToken);

        if (result.Credentials is not null)
        {
            return Ok(result.Credentials);
        }

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Gone)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                new { message = result.Error });
        }

        return Conflict(new { message = result.Error });
    }

    [HttpPost("orders/{orderId:guid}/items/{orderItemId:guid}/execute")]
    public async Task<ActionResult<ExecuteAutoFulfillmentResponse>> ExecuteAuto(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await executionService.ExecuteAutoItemAsync(
                orderId,
                orderItemId,
                FulfillmentExecutionContext.Admin(User),
                cancellationToken);

            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
    [HttpGet("orders/{orderId:guid}/items/{orderItemId:guid}/activity")]
    public async Task<ActionResult<IReadOnlyList<FulfillmentActivityResponse>>> GetActivity(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken)
    {
        var exists = await activityService.GetAsync(orderId, orderItemId, cancellationToken);
        return Ok(exists);
    }

}
