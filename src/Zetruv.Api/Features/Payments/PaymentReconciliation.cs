using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

public sealed class PaymentReconciliationOptions
{
    public const string SectionName = "Payments:Reconciliation";

    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 60;
    public int BaseBackoffSeconds { get; set; } = 60;
    public int MaxBackoffSeconds { get; set; } = 900;
    public int MaxBatchSize { get; set; } = 50;
}

public sealed record PaymentReconciliationAttemptResponse(
    Guid TransactionId,
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ProviderReference,
    PaymentWebhookStatus? ProviderStatus,
    PaymentTransactionStatus TransactionStatus,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus,
    int AttemptCount,
    DateTimeOffset LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    string? Message);

public sealed record PaymentReconciliationAttemptResult(
    PaymentReconciliationAttemptResponse? Reconciliation,
    string? Error,
    bool IsConfigurationError = false,
    bool IsNotFound = false,
    bool IsConflict = false)
{
    public static PaymentReconciliationAttemptResult Success(
        PaymentReconciliationAttemptResponse reconciliation) =>
        new(reconciliation, null);

    public static PaymentReconciliationAttemptResult Failure(
        string error,
        bool isConfigurationError = false,
        bool isNotFound = false,
        bool isConflict = false) =>
        new(null, error, isConfigurationError, isNotFound, isConflict);
}

public sealed class PaymentReconciliationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentReconciliationOptions> options,
    ILogger<PaymentReconciliationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PaymentService>();
                var processed = await service.ReconcileDueAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation(
                        "Payment reconciliation processed {Count} due transactions.",
                        processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Payment reconciliation cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/payments")]
public sealed class CmsPaymentReconciliationController(PaymentService paymentService) : ControllerBase
{
    [HttpPost("transactions/{transactionId:guid}/reconcile")]
    public async Task<ActionResult<PaymentReconciliationAttemptResponse>> Reconcile(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.ReconcileTransactionAsync(
            transactionId,
            cancellationToken);

        if (result.Reconciliation is not null)
        {
            return Ok(result.Reconciliation);
        }

        if (result.IsNotFound)
        {
            return NotFound(new { message = result.Error });
        }

        if (result.IsConfigurationError)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = result.Error });
        }

        if (result.IsConflict)
        {
            return Conflict(new { message = result.Error });
        }

        return BadRequest(new { message = result.Error });
    }
}
