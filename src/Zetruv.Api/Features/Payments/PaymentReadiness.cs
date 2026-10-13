using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

// A CMS payment method is display configuration only. It must never be assumed
// payable unless the selected provider explicitly supports that channel.
public interface ILivePaymentChannelGateway
{
    bool IsLiveConfigured { get; }
    bool SupportsChannel(string code);
}

public sealed record CheckoutPaymentOption(
    string Code,
    string Name,
    string? IconUrl)
{
    // Null means not quoted; never infer a zero-fee checkout on the client.
    public decimal? PaymentFee { get; init; }
}

public sealed record CheckoutPaymentReadinessResponse(
    bool CanAcceptLivePayments,
    string Status,
    IReadOnlyList<CheckoutPaymentOption> AvailableMethods)
{
    public bool IsSandbox { get; init; }
}

public sealed record CmsPaymentChannelReadiness(
    Guid Id,
    string Code,
    string Name,
    bool CmsEnabled,
    bool Operational,
    string? Reason);

public sealed record CmsPaymentReadinessResponse(
    string? Provider,
    bool CanAcceptLivePayments,
    string Status,
    IReadOnlyList<CmsPaymentChannelReadiness> ConfiguredMethods);

public sealed class PaymentReadinessService(
    ZetruvDbContext db,
    PaymentGatewayResolver resolver)
{
    public async Task<CmsPaymentReadinessResponse> GetAsync(CancellationToken ct)
    {
        var gateway = resolver.Resolve();
        var live = gateway is ILivePaymentChannelGateway
            { IsLiveConfigured: true };
        var channels = await db.SitePaymentMethods.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.IconUrl,
                x.IsActive
            })
            .ToListAsync(ct);

        var methods = channels.Select(x =>
        {
            var supported = live &&
                ((ILivePaymentChannelGateway)gateway!).SupportsChannel(x.Code);
            var operational = x.IsActive && supported;
            return new CmsPaymentChannelReadiness(
                x.Id,
                x.Code,
                x.Name,
                x.IsActive,
                operational,
                operational ? null
                    : !x.IsActive ? "Disabled in CMS."
                    : !live ? "No configured live payment-channel gateway."
                    : "Selected provider does not support this payment channel.");
        }).ToList();

        return new CmsPaymentReadinessResponse(
            gateway?.Name,
            methods.Any(x => x.Operational),
            !live ? gateway is null ? "Unconfigured" : "DemoOnly"
                : methods.Any(x => x.Operational) ? "Ready" : "NoSupportedMethods",
            methods);
    }
}

[ApiController]
[Route("api/v1/checkout/payment-options")]
public sealed class CheckoutPaymentOptionsController(
    PaymentReadinessService readiness,
    PaymentGatewayResolver gatewayResolver,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var current = await readiness.GetAsync(ct);
        Response.Headers.CacheControl = "no-store";
        var gateway = gatewayResolver.Resolve() as XenditPaymentGateway;
        var merchantAbsorbsFees = gateway is { IsLiveConfigured: true } &&
            configuration.GetValue<bool>("Payments:Xendit:MerchantAbsorbsFees");
        return Ok(new CheckoutPaymentReadinessResponse(
            current.CanAcceptLivePayments,
            current.Status,
            current.ConfiguredMethods
                .Where(x => x.Operational)
                .Select(x => new CheckoutPaymentOption(
                    x.Code, x.Name, null)
                {
                    PaymentFee = merchantAbsorbsFees ? 0m : null
                })
                .ToList())
        {
            IsSandbox = gateway?.IsSandbox == true
        });
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/payments/readiness")]
public sealed class CmsPaymentReadinessController(
    PaymentReadinessService readiness) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Ok(await readiness.GetAsync(ct));
    }
}
