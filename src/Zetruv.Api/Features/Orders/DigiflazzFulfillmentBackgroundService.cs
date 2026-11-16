using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Payments;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class DigiflazzFulfillmentBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<DigiflazzOptions> options,
    ILogger<DigiflazzFulfillmentBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.IsReady)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ZetruvDbContext>();
                var execution = scope.ServiceProvider.GetRequiredService<FulfillmentExecutionService>();
                var paymentGateways = scope.ServiceProvider.GetRequiredService<PaymentGatewayResolver>();
                var mock = paymentGateways.CanPayForDigiflazz("mock", settings.Testing);
                var midtrans = paymentGateways.CanPayForDigiflazz(MidtransPaymentGateway.ProviderCode, settings.Testing);
                var cutoff = DateTimeOffset.UtcNow - DigiflazzAutoIdFulfillmentProvider.MinimumCheckInterval;
                var buyerBinding = settings.BuyerBinding;
                var items = await db.OrderItems.AsNoTracking()
                    .Where(x => x.DigiflazzSku != null && x.DigiflazzBuyerBinding == buyerBinding &&
                        x.FulfillmentStatus == FulfillmentStatus.Processing &&
                        x.Order.PaymentStatus == PaymentStatus.Paid && x.Order.Status != OrderStatus.Cancelled &&
                        ((mock && x.Order.PaymentProvider == "mock") || (midtrans && x.Order.PaymentProvider == MidtransPaymentGateway.ProviderCode)) &&
                        (x.LastFulfillmentAttemptAt == null || x.LastFulfillmentAttemptAt <= cutoff))
                    .OrderBy(x => x.LastFulfillmentAttemptAt ?? x.Order.PaidAt ?? x.CreatedAt)
                    .Take(20)
                    .Select(x => new { x.OrderId, x.Id })
                    .ToListAsync(stoppingToken);

                foreach (var item in items)
                {
                    try
                    {
                        await execution.ExecuteAutoItemAsync(
                            item.OrderId, item.Id, FulfillmentExecutionContext.Background, stoppingToken);
                    }
                    catch (InvalidOperationException exception)
                    {
                        logger.LogWarning(exception, "Could not process Digiflazz fulfillment for order item {OrderItemId}.", item.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Digiflazz fulfillment cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
