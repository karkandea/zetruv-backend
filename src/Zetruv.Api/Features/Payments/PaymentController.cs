using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zetruv.Api.Features.Orders;

namespace Zetruv.Api.Features.Payments;

[ApiController]
[Route("api/v1/checkout/orders/{orderId:guid}/payment")]
public sealed class PaymentController(
    PaymentService paymentService,
    OrderAccessTokenService orderAccessTokens) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("payment-initiation")]
    public async Task<ActionResult<InitiatePaymentResponse>> Initiate(
        Guid orderId,
        [FromHeader(Name = "X-Order-Access-Token")] string? orderAccessToken,
        CancellationToken cancellationToken)
    {
        if (!orderAccessTokens.Validate(orderId, orderAccessToken))
        {
            return NotFound(new
            {
                message = "Order was not found or the access token is invalid."
            });
        }

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
