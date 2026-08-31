using Microsoft.AspNetCore.Mvc;

namespace Zetruv.Api.Features.Orders;

[ApiController]
[Route("api/v1/checkout")]
public sealed class CheckoutController(CheckoutService checkoutService) : ControllerBase
{
    [HttpPost("orders")]
    public async Task<ActionResult<CreateCheckoutOrderResponse>> CreateOrder(
        CreateCheckoutOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await checkoutService.CreateOrderAsync(request, cancellationToken);
        if (result.Order is null)
        {
            return BadRequest(new { message = result.Error });
        }

        return StatusCode(StatusCodes.Status201Created, result.Order);
    }
}
