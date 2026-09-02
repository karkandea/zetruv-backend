using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Auth
{
    public static class AuthPolicies
    {
        public const string CmsAdmin = "CmsAdmin";
    }

    public static class AdminRoles
    {
        public const string Admin = "Admin";
    }

    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; init; } = "zetruv-api";
        public string Audience { get; init; } = "zetruv-cms";
        public string Key { get; init; } = string.Empty;
        public int ExpiryMinutes { get; init; } = 480;
    }

    public sealed class AdminUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = string.Empty;
        public string NormalizedEmail { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = AdminRoles.Admin;
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class JwtTokenService(IOptions<JwtOptions> options)
    {
        private readonly JwtOptions _options = options.Value;

        public LoginResponse Create(AdminUser user)
        {
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiryMinutes);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                expires: expiresAt.UtcDateTime,
                signingCredentials: credentials);

            return new LoginResponse(
                new JwtSecurityTokenHandler().WriteToken(token),
                expiresAt,
                user.Email,
                user.Role);
        }
    }

    public sealed class AdminSeeder(
        ZetruvDbContext db,
        IConfiguration configuration,
        ILogger<AdminSeeder> logger)
    {
        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            var email = configuration["CmsAdmin:Email"]?.Trim();
            var password = configuration["CmsAdmin:Password"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogInformation(
                    "CMS admin seed skipped because CmsAdmin credentials are not configured.");
                return;
            }

            var normalizedEmail = email.ToUpperInvariant();
            var exists = await db.AdminUsers
                .AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

            if (exists)
            {
                return;
            }

            var user = new AdminUser
            {
                Email = email,
                NormalizedEmail = normalizedEmail
            };

            var hasher = new PasswordHasher<AdminUser>();
            user.PasswordHash = hasher.HashPassword(user, password);

            db.AdminUsers.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Seeded initial CMS admin account for {Email}.", email);
        }
    }

    public sealed record LoginRequest(
        [Required, EmailAddress] string Email,
        [Required, MinLength(8)] string Password);

    public sealed record LoginResponse(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        string Email,
        string Role);

    [ApiController]
    [Route("api/v1/cms/auth")]
    [Route("api/v1/admin/auth")]
    public sealed class AdminAuthController(
        ZetruvDbContext db,
        JwtTokenService tokens) : ControllerBase
    {
        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login(
            [FromBody] LoginRequest request,
            CancellationToken cancellationToken)
        {
            var normalizedEmail = request.Email.Trim().ToUpperInvariant();

            var user = await db.AdminUsers
                .SingleOrDefaultAsync(
                    x => x.NormalizedEmail == normalizedEmail && x.IsActive,
                    cancellationToken);

            if (user is null)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var hasher = new PasswordHasher<AdminUser>();
            var result = hasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            return Ok(tokens.Create(user));
        }
    }
}
