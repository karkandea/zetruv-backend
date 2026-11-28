using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/orders/{orderId:guid}/invoice")]
public sealed class CustomerInvoiceController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Download(Guid orderId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId)) return Unauthorized();
        var invoice = await db.Set<OrderInvoice>().AsNoTracking()
            .Where(x => x.OrderId == orderId && db.Orders.Any(o => o.Id == x.OrderId && o.CustomerUserId == customerId))
            .Select(x => new { x.InvoiceNumber, x.Status, x.Pdf }).SingleOrDefaultAsync(ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == "Failed") return StatusCode(503, new { message = "Invoice generation failed." });
        if (invoice.Status != "Ready" || invoice.Pdf is null) return Conflict(new { message = "Invoice is preparing." });
        return File(invoice.Pdf, "application/pdf", invoice.InvoiceNumber + ".pdf");
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/orders/{orderId:guid}/invoice")]
public sealed class CmsInvoiceController(ZetruvDbContext db, ILogger<CmsInvoiceController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Download(Guid orderId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var invoice = await db.Set<OrderInvoice>().AsNoTracking().Where(x => x.OrderId == orderId)
            .Select(x => new { x.InvoiceNumber, x.Status, x.Pdf }).SingleOrDefaultAsync(ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == "Failed") return StatusCode(503, new { message = "Invoice generation failed." });
        if (invoice.Status != "Ready" || invoice.Pdf is null) return Conflict(new { message = "Invoice is preparing." });
        logger.LogInformation("CMS invoice downloaded for order {OrderId} by {Actor}", orderId, User.Identity?.Name);
        return File(invoice.Pdf, "application/pdf", invoice.InvoiceNumber + ".pdf");
    }

    [HttpPost("retry")]
    public async Task<IActionResult> Retry(Guid orderId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var now = DateTimeOffset.UtcNow;
        var count = await db.Set<OrderInvoice>().Where(x => x.OrderId == orderId && x.Status == "Failed")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Pending").SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.NextAttemptAt, now), ct);
        if (count == 0) return Conflict(new { message = "Only failed invoices can be retried." });
        logger.LogInformation("CMS invoice retry for order {OrderId} by {Actor}", orderId, User.Identity?.Name);
        return NoContent();
    }
}
