using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.IdentityModel.Tokens.Jwt;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Zetruv.Api.Features.Orders;

[ApiController]
[Route("api/v1/checkout")]
public sealed class CheckoutController(
    CheckoutService checkoutService,
    OrderAccessTokenService orderAccessTokens,
    ZetruvDbContext db) : ControllerBase
{
    [HttpPost("orders")]
    public async Task<ActionResult<CreateCheckoutOrderResponse>> CreateOrder(
        CreateCheckoutOrderRequest request,
        CancellationToken cancellationToken)
    {
        Guid? customerId = null;
        if (Request.Headers.ContainsKey("Authorization"))
        {
            var auth = await HttpContext.AuthenticateAsync(CustomerAuthConstants.Scheme);
            if (!auth.Succeeded)
                return Unauthorized(new { message = "A valid customer session is required." });
            if (!Guid.TryParse(auth.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
                return Unauthorized();
            var account = await db.CustomerUsers.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == userId && x.IsActive && x.EmailVerifiedAt != null, cancellationToken);
            if (account is null) return Unauthorized();
            // Do not allow an authenticated checkout to be credited to a different customer.
            if (!string.IsNullOrWhiteSpace(request.CustomerEmail) &&
                !string.Equals(request.CustomerEmail.Trim(), account.Email, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Checkout email must match the signed-in account." });
            customerId = userId;
            request = request with { CustomerEmail = account.Email,
                CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? account.Name : request.CustomerName };
        }
        var result = await checkoutService.CreateOrderAsync(request, customerId, cancellationToken);
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
