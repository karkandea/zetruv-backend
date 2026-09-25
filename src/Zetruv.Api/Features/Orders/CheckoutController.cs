using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        Response.Headers.CacheControl = "private, no-store";
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
            if (!string.IsNullOrWhiteSpace(request.CustomerEmail) &&
                !string.Equals(request.CustomerEmail.Trim(), account.Email, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Checkout email must match the signed-in account." });
            customerId = userId;
            request = request with
            {
                CustomerEmail = account.Email,
                CustomerName = string.IsNullOrWhiteSpace(request.CustomerName)
                    ? account.Name : request.CustomerName
            };
        }

        if (!Request.Headers.TryGetValue("Idempotency-Key", out var supplied))
            return await CreateFreshOrderAsync(request, customerId, null, null, cancellationToken);

        // The Figma checkout has a customer login gate. An idempotency key is
        // scoped to that VERIFIED principal; guests cannot use it to retrieve
        // another person's order by guessing an email and a key.
        if (!customerId.HasValue)
            return Unauthorized(new { message = "Sign in with a verified customer account for idempotent checkout." });
        if (!Guid.TryParse(supplied.ToString(), out var parsedKey) ||
            parsedKey.ToString("D")[14] != '4' ||
            !"89ab".Contains(parsedKey.ToString("D")[19]))
            return BadRequest(new { message = "Idempotency-Key must be a random UUID v4." });

        var scope = "customer-checkout:" + customerId.Value.ToString("N") + ":" + parsedKey.ToString("N");
        var keyHash = Sha256(scope);
        // The full normalized request is fingerprinted, never stored in plaintext.
        var requestHash = FingerprintRequest(parsedKey, request);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var acquired = false;
        try
        {
            // Session lock wraps the whole create flow; the service opens and
            // commits its own DB transaction on this SAME open connection.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock(hashtext({scope}))", cancellationToken);
            acquired = true;

            var existing = await db.Orders.AsNoTracking()
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x =>
                    x.CustomerUserId == customerId.Value &&
                    x.IdempotencyKeyHash == keyHash, cancellationToken);
            if (existing is not null)
            {
                if (existing.IdempotencyRequestHash != requestHash)
                    return Conflict(new { message = "Idempotency-Key was already used with a different checkout request." });
                var items = existing.Items
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new CheckoutOrderItemResponse(
                        x.ProductVariantId!.Value,
                        x.ProductName, x.ProductSlug, x.ProductKind,
                        x.FulfillmentMethod, x.FulfillmentStatus, x.VariantName ?? "",
                        x.ThumbnailUrl, x.GameName, x.UnitPrice, x.Quantity, x.LineTotal))
                    .ToList();
                var replay = new CreateCheckoutOrderResponse(
                    existing.Id, existing.OrderNumber, existing.Status,
                    existing.PaymentStatus, existing.Subtotal, existing.DiscountAmount,
                    existing.ShippingAmount, existing.GrandTotal, existing.Currency,
                    items, existing.CreatedAt)
                {
                    VoucherCode = existing.VoucherCode,
                    VoucherDiscountAmount = existing.VoucherDiscountAmount
                };
                return Ok(WithAccessToken(replay));
            }

            return await CreateFreshOrderAsync(
                request, customerId, keyHash, requestHash, cancellationToken);
        }
        finally
        {
            if (acquired)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_unlock(hashtext({scope}))", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<ActionResult<CreateCheckoutOrderResponse>> CreateFreshOrderAsync(
        CreateCheckoutOrderRequest request, Guid? customerId,
        string? keyHash, string? requestHash, CancellationToken cancellationToken)
    {
        var result = await checkoutService.CreateOrderAsync(
            request, customerId, cancellationToken, keyHash, requestHash);
        if (result.Order is null)
            return BadRequest(new { message = result.Error });
        return StatusCode(StatusCodes.Status201Created, WithAccessToken(result.Order));
    }

    private CreateCheckoutOrderResponse WithAccessToken(CreateCheckoutOrderResponse order)
    {
        var grant = orderAccessTokens.Issue(order.Id);
        return order with
        {
            OrderAccessToken = grant.Token,
            OrderAccessTokenExpiresAt = grant.ExpiresAt
        };
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // The random UUID key is never stored; its HMAC protects low-entropy
    // login credential values from offline guesses if the DB is compromised.
    private static string FingerprintRequest(Guid key, CreateCheckoutOrderRequest request)
    {
        using var hmac = new HMACSHA256(key.ToByteArray());
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    }
}
