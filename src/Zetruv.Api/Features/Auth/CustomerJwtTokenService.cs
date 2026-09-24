using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Zetruv.Api.Features.Auth;

public sealed class CustomerJwtTokenService(
    IOptions<JwtOptions> jwt,
    IOptions<CustomerAuthOptions> customerOptions)
{
    public const string Audience = "zetruv-customers";
    public const string VersionClaim = "customer_token_version";

    public CustomerLoginResponse Create(CustomerUser customer)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(
            Math.Clamp(customerOptions.Value.JwtExpiryMinutes, 5, 1440));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, customer.Email),
            new Claim(VersionClaim, customer.TokenVersion.ToString()),
            new Claim("token_type", "customer")
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt.Value.Issuer,
            audience: Audience,
            claims: claims,
            expires: expiry.UtcDateTime,
            signingCredentials: credentials);
        return new CustomerLoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiry,
            new CustomerIdentity(customer.Id, customer.Name, customer.Email,
                customer.EmailVerifiedAt is not null));
    }
}
