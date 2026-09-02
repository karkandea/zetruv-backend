using Microsoft.AspNetCore.Mvc;

namespace Zetruv.Api.Features.Payments;

[ApiController]
[Route("api/v1/payments/webhooks/{provider}")]
public sealed class PaymentWebhookController(PaymentService paymentService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReconcilePaymentResponse>> Receive(
        string provider,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(
            x => x.Key,
            x => x.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var result = await paymentService.ReconcileWebhookAsync(
            provider,
            rawBody,
            headers,
            cancellationToken);

        if (result.Payment is not null)
        {
            return Ok(result.Payment);
        }

        if (result.IsConfigurationError)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = result.Error });
        }

        if (result.IsNotFound)
        {
            return NotFound(new { message = result.Error });
        }

        if (result.IsConflict)
        {
            return Conflict(new { message = result.Error });
        }

        return BadRequest(new { message = result.Error });
    }
}
