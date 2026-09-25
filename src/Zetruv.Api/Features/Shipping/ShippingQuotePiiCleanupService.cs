using Microsoft.Extensions.Options;

namespace Zetruv.Api.Features.Shipping;

public sealed class ShippingQuotePiiCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ShippingOptions> options,
    ILogger<ShippingQuotePiiCleanupService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(
        Math.Clamp(options.Value.QuotePiiCleanupIntervalSeconds, 5, 3600));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ScrubAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScrubAsync(stoppingToken);
        }
    }

    private async Task ScrubAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ShippingService>();
            var affected = await service.ScrubExpiredQuotePiiAsync(
                DateTimeOffset.UtcNow,
                cancellationToken);

            if (affected > 0)
            {
                logger.LogInformation(
                    "Scrubbed PII from {Count} expired shipping quote(s).",
                    affected);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to scrub expired shipping quote PII.");
        }
    }
}
