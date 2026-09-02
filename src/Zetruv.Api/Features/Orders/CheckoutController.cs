using Microsoft.AspNetCore.Mvc;

namespace Zetruv.Api.Features.Orders;

[ApiController]
[Route("api/v1/checkout")]
public sealed class CheckoutController(
    CheckoutService checkoutService,
    OrderAccessTokenService orderAccessTokens) : ControllerBase
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

        var grant = orderAccessTokens.Issue(result.Order.Id);
        var response = result.Order with
        {
            OrderAccessToken = grant.Token,
            OrderAccessTokenExpiresAt = grant.ExpiresAt
        };

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
