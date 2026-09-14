using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record AutoIdFulfillmentProviderRequest(
    Guid OrderId,
    string OrderNumber,
    Guid OrderItemId,
    string ProductName,
    string ProductSlug,
    string? VariantName,
    string? Sku,
    string? GameName,
    int Quantity,
    string? ValidationProvider,
    string? ValidationProviderReference,
    string? AccountDisplayName,
    IReadOnlyDictionary<string, string> DestinationFields);

public sealed record AutoIdFulfillmentProviderResult(
    bool IsSuccess,
    string? ProviderReference,
    string? Error)
{
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
                $"MOCK-FULFILL-{request.OrderItemId:N}-{Guid.NewGuid():N}"));
    }
}

public sealed class AutoIdFulfillmentProviderResolver(
    IEnumerable<IAutoIdFulfillmentProvider> providers,
    IConfiguration configuration)
{
    public IAutoIdFulfillmentProvider? Resolve()
    {
        var configured = configuration["Fulfillment:AutoId:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return providers.FirstOrDefault(x =>
            string.Equals(x.Name, configured, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class FulfillmentExecutionService(
    ZetruvDbContext db,
    AutoIdFulfillmentProviderResolver resolver,
    OrderFulfillmentService fulfillmentService,
    ILogger<FulfillmentExecutionService> logger)
{
    public async Task ExecuteAutoItemsForOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var itemIds = await db.OrderItems
            .AsNoTracking()
            .Where(x =>
                x.OrderId == orderId &&
                x.FulfillmentMethod == FulfillmentMethod.AUTO_ID &&
                x.FulfillmentStatus == FulfillmentStatus.Processing)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var itemId in itemIds)
        {
            await ExecuteAutoItemAsync(orderId, itemId, cancellationToken);
        }
    }

    public async Task<ExecuteAutoFulfillmentResponse?> ExecuteAutoItemAsync(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken = default)
    {
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

        if (item.FulfillmentStatus == FulfillmentStatus.Failed)
        {
            item.FulfillmentStatus = FulfillmentStatus.Processing;
            item.FulfillmentMessage = null;
            item.FulfilledAt = null;
        }

        var now = DateTimeOffset.UtcNow;
        item.FulfillmentStartedAt ??= now;
        item.FulfillmentAttemptCount++;
        item.LastFulfillmentAttemptAt = now;

        var provider = resolver.Resolve();
        if (provider is null)
        {
            Fail(item, "AUTO_ID fulfillment provider is not configured.");
            fulfillmentService.RecalculateOrder(order, now);
            await db.SaveChangesAsync(cancellationToken);
            return ToResponse(order, item);
        }

        if (item.GameAccountValidation is null)
        {
            Fail(item, "Verified game account destination is missing.");
            fulfillmentService.RecalculateOrder(order, now);
            await db.SaveChangesAsync(cancellationToken);
            return ToResponse(order, item);
        }

        var destinationFields = ParseDestinationFields(item.GameAccountValidation.InputJson);
        if (destinationFields is null || destinationFields.Count == 0)
        {
            Fail(item, "Verified game account destination is invalid.");
            fulfillmentService.RecalculateOrder(order, now);
            await db.SaveChangesAsync(cancellationToken);
            return ToResponse(order, item);
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
                    item.GameName,
                    item.Quantity,
                    item.GameAccountValidation.Provider,
                    item.GameAccountValidation.ProviderReference,
                    item.GameAccountValidation.AccountDisplayName,
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

            result = AutoIdFulfillmentProviderResult.Failure(
                "AUTO_ID fulfillment provider is temporarily unavailable.");
        }

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.ProviderReference))
        {
            item.FulfillmentStatus = FulfillmentStatus.Completed;
            item.FulfillmentReference = result.ProviderReference.Trim();
            item.FulfillmentMessage = null;
            item.FulfilledAt = DateTimeOffset.UtcNow;
        }
        else
        {
            Fail(
                item,
                string.IsNullOrWhiteSpace(result.Error)
                    ? "AUTO_ID fulfillment failed."
                    : result.Error.Trim());
        }

        fulfillmentService.RecalculateOrder(order, DateTimeOffset.UtcNow);
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
            return JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson);
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
            return JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson);
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
                cancellationToken);

            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
