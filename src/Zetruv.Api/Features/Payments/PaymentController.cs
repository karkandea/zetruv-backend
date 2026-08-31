using Microsoft.AspNetCore.Mvc;

namespace Zetruv.Api.Features.Payments;

[ApiController]
[Route("api/v1/checkout/orders/{orderId:guid}/payment")]
public sealed class PaymentController(PaymentService paymentService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InitiatePaymentResponse>> Initiate(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.InitiateAsync(orderId, cancellationToken);
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

        return BadRequest(new { message = result.Error });
    }
}
