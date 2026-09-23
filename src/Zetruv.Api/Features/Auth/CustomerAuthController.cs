using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Auth;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("customer-auth")]
public sealed class CustomerAuthController(
    ZetruvDbContext db,
    CustomerJwtTokenService tokens,
    CustomerEmailSender emails,
    IOptions<CustomerAuthOptions> config,
    ILogger<CustomerAuthController> logger) : ControllerBase
{
    private readonly CustomerAuthOptions _options = config.Value;
    private static readonly PasswordHasher<CustomerUser> Hasher = new();

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string TokenHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool ValidPassword(string password) =>
        password is { Length: >= 8 and <= 256 }
        && password.Any(c => c is >= 'A' and <= 'Z')
        && password.Any(c => c is >= '0' and <= '9');

    private static object PasswordError() => new
    {
        code = "INVALID_PASSWORD",
        message = "Password must contain at least 8 characters, one uppercase letter and one number."
    };

    private IActionResult EmailUnavailable() => StatusCode(503, new
    {
        code = "EMAIL_DELIVERY_UNAVAILABLE",
        message = "Email delivery is temporarily unavailable. Please try again later."
    });

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] CustomerRegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { code = "INVALID_NAME", message = "Name is required." });
        if (!ValidPassword(request.Password))
            return BadRequest(PasswordError());
        if (!emails.IsReady) return EmailUnavailable();

        var normalized = Normalize(request.Email);
        if (await db.CustomerUsers.AnyAsync(x => x.NormalizedEmail == normalized, ct))
            return Conflict(new { code = "EMAIL_ALREADY_REGISTERED",
                message = "This email is already registered. Sign in or reset your password." });

        var customer = new CustomerUser
        {
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalized
        };
        customer.PasswordHash = Hasher.HashPassword(customer, request.Password);
        db.CustomerUsers.Add(customer);
        db.CustomerPasswordHistories.Add(new CustomerPasswordHistory
        {
            CustomerUserId = customer.Id,
            PasswordHash = customer.PasswordHash
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { code = "EMAIL_ALREADY_REGISTERED",
                message = "This email is already registered. Sign in or reset your password." });
        }

        if (!await IssueEmailAsync(customer, CustomerAuthTokenPurpose.EmailVerification, ct))
            return EmailUnavailable();
        var registrationToken = await CreateRegistrationTokenAsync(customer, ct);

        return Accepted(new { message = "Check your email to verify your account.",
            registrationToken,
            resendAfterSeconds = _options.ResendCooldownSeconds,
            verificationExpiresInHours = _options.VerificationLifetimeHours });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] CustomerLoginRequest request, CancellationToken ct)
    {
        var customer = await db.CustomerUsers.SingleOrDefaultAsync(
            x => x.NormalizedEmail == Normalize(request.Email) && x.IsActive, ct);
        var check = customer is null ? PasswordVerificationResult.Failed :
            Hasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password);
        if (check == PasswordVerificationResult.Failed)
            return Unauthorized(new { code = "INVALID_CREDENTIALS",
                message = "Invalid email or password." });

        if (customer!.EmailVerifiedAt is null)
            return StatusCode(403, new { code = "EMAIL_NOT_VERIFIED",
                message = "Verify your email before signing in." });

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            customer.PasswordHash = Hasher.HashPassword(customer, request.Password);
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Ok(tokens.Create(customer));
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(
        [FromBody] CustomerEmailRequest request, CancellationToken ct)
    {
        if (!emails.IsReady) return EmailUnavailable();
        var customer = await db.CustomerUsers.SingleOrDefaultAsync(
            x => x.NormalizedEmail == Normalize(request.Email) && x.IsActive
                 && x.EmailVerifiedAt == null, ct);
        if (customer is not null
            && !WithinCooldown(customer.LastVerificationEmailSentAt))
            await IssueEmailAsync(customer, CustomerAuthTokenPurpose.EmailVerification, ct);
        return Accepted(new { message = "If verification is pending, a new email is on its way.",
            resendAfterSeconds = _options.ResendCooldownSeconds });
    }

    [HttpPost("change-registration-email")]
    public async Task<IActionResult> ChangeRegistrationEmail(
        [FromBody] CustomerChangePendingEmailRequest request, CancellationToken ct)
    {
        if (!emails.IsReady) return EmailUnavailable();
        var token = await FindTokenAsync(
            request.RegistrationToken, CustomerAuthTokenPurpose.RegistrationEdit, ct);
        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= DateTimeOffset.UtcNow)
            return BadRequest(new { code = "REGISTRATION_SESSION_INVALID",
                message = "This registration session is no longer valid. Start again." });

        var newEmail = request.NewEmail.Trim();
        var normalized = Normalize(newEmail);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM customer_users WHERE \"Id\" = {token.CustomerUserId} FOR UPDATE",
                ct);
            var customer = await db.CustomerUsers.SingleAsync(
                x => x.Id == token.CustomerUserId, ct);
            if (!customer.IsActive || customer.EmailVerifiedAt is not null)
                return BadRequest(new { code = "REGISTRATION_SESSION_INVALID",
                    message = "This registration session is no longer valid. Start again." });
            if (customer.NormalizedEmail == normalized)
                return BadRequest(new { code = "EMAIL_UNCHANGED",
                    message = "Enter a different email address." });
            if (await db.CustomerUsers.AnyAsync(
                x => x.NormalizedEmail == normalized && x.Id != customer.Id, ct))
                return Conflict(new { code = "EMAIL_ALREADY_REGISTERED",
                    message = "This email is already registered." });

            var now = DateTimeOffset.UtcNow;
            var claimed = await db.CustomerAuthTokens.Where(x => x.Id == token.Id
                    && x.ConsumedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);
            if (claimed != 1)
                return BadRequest(new { code = "REGISTRATION_SESSION_INVALID",
                    message = "This registration session is no longer valid. Start again." });

            await db.CustomerAuthTokens.Where(x => x.CustomerUserId == customer.Id
                    && x.ConsumedAt == null
                    && (x.Purpose == CustomerAuthTokenPurpose.EmailVerification
                        || x.Purpose == CustomerAuthTokenPurpose.RegistrationEdit))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);
            customer.Email = newEmail;
            customer.NormalizedEmail = normalized;
            customer.LastVerificationEmailSentAt = null;
            customer.UpdatedAt = now;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return Conflict(new { code = "EMAIL_ALREADY_REGISTERED",
                    message = "This email is already registered." });
            }
            await transaction.CommitAsync(ct);
        }
        var updatedCustomer = await db.CustomerUsers.SingleAsync(
            x => x.Id == token.CustomerUserId, ct);
        if (!await IssueEmailAsync(
                updatedCustomer, CustomerAuthTokenPurpose.EmailVerification, ct))
            return EmailUnavailable();
        var newRegistrationToken = await CreateRegistrationTokenAsync(updatedCustomer, ct);
        return Accepted(new { message = "Check your new email to verify your account.",
            registrationToken = newRegistrationToken,
            resendAfterSeconds = _options.ResendCooldownSeconds,
            verificationExpiresInHours = _options.VerificationLifetimeHours });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] CustomerVerifyRequest request, CancellationToken ct)
    {
        var token = await FindTokenAsync(request.Token, CustomerAuthTokenPurpose.EmailVerification, ct);
        if (token is null || token.ConsumedAt is not null)
            return BadRequest(new { code = "VERIFICATION_LINK_INVALID",
                message = "This verification link is no longer valid." });
        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
            return BadRequest(new { code = "VERIFICATION_LINK_EXPIRED",
                message = "This verification link has expired. Request a new email." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM customer_users WHERE \"Id\" = {token.CustomerUserId} FOR UPDATE",
            ct);
        var now = DateTimeOffset.UtcNow;
        var updated = await db.CustomerAuthTokens
            .Where(x => x.Id == token.Id && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);
        if (updated != 1)
            return BadRequest(new { code = "VERIFICATION_LINK_INVALID",
                message = "This verification link is no longer valid." });

        var customer = await db.CustomerUsers.SingleAsync(x => x.Id == token.CustomerUserId, ct);
        if (!customer.IsActive)
            return BadRequest(new { code = "VERIFICATION_LINK_INVALID",
                message = "This verification link is no longer valid." });
        customer.EmailVerifiedAt ??= now;
        customer.UpdatedAt = now;
        await db.CustomerAuthTokens
            .Where(x => x.CustomerUserId == customer.Id && x.ConsumedAt == null
                && (x.Purpose == CustomerAuthTokenPurpose.EmailVerification
                    || x.Purpose == CustomerAuthTokenPurpose.RegistrationEdit))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(tokens.Create(customer));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] CustomerEmailRequest request, CancellationToken ct)
    {
        if (!emails.IsReady) return EmailUnavailable();
        var customer = await db.CustomerUsers.SingleOrDefaultAsync(
            x => x.NormalizedEmail == Normalize(request.Email)
                && x.IsActive && x.EmailVerifiedAt != null, ct);
        if (customer is not null && !WithinCooldown(customer.LastPasswordResetEmailSentAt))
            await IssueEmailAsync(customer, CustomerAuthTokenPurpose.PasswordReset, ct);
        return Accepted(new { message = "If an account exists, a reset link is on its way.",
            resendAfterSeconds = _options.ResendCooldownSeconds,
            linkExpiresInMinutes = _options.ResetLifetimeMinutes });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] CustomerResetRequest request, CancellationToken ct)
    {
        if (!ValidPassword(request.NewPassword)) return BadRequest(PasswordError());
        if (request.NewPassword != request.ConfirmPassword)
            return BadRequest(new { code = "PASSWORD_MISMATCH",
                message = "The passwords do not match." });

        var token = await FindTokenAsync(request.Token, CustomerAuthTokenPurpose.PasswordReset, ct);
        if (token is null || token.ConsumedAt is not null)
            return BadRequest(new { code = "RESET_LINK_INVALID",
                message = "This reset link is no longer valid." });
        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
            return BadRequest(new { code = "RESET_LINK_EXPIRED",
                message = "This reset link has expired. Request a new one." });

        var customer = await db.CustomerUsers.SingleAsync(x => x.Id == token.CustomerUserId, ct);
        if (!customer.IsActive || customer.EmailVerifiedAt is null)
            return BadRequest(new { code = "RESET_LINK_INVALID",
                message = "This reset link is no longer valid." });
        var previous = await db.CustomerPasswordHistories
            .Where(x => x.CustomerUserId == customer.Id)
            .OrderByDescending(x => x.CreatedAt).Take(5)
            .Select(x => x.PasswordHash).ToListAsync(ct);
        if (Hasher.VerifyHashedPassword(customer, customer.PasswordHash, request.NewPassword)
                != PasswordVerificationResult.Failed ||
            previous.Any(hash => Hasher.VerifyHashedPassword(customer, hash, request.NewPassword)
                != PasswordVerificationResult.Failed))
            return BadRequest(new { code = "PASSWORD_REUSED",
                message = "Choose a password you have not used before." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var updated = await db.CustomerAuthTokens
            .Where(x => x.Id == token.Id && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);
        if (updated != 1)
            return BadRequest(new { code = "RESET_LINK_INVALID",
                message = "This reset link is no longer valid." });

        customer.PasswordHash = Hasher.HashPassword(customer, request.NewPassword);
        customer.TokenVersion++;
        customer.UpdatedAt = now;
        db.CustomerPasswordHistories.Add(new CustomerPasswordHistory
        {
            CustomerUserId = customer.Id, PasswordHash = customer.PasswordHash, CreatedAt = now
        });
        await db.CustomerAuthTokens
            .Where(x => x.CustomerUserId == customer.Id && x.Purpose ==
                CustomerAuthTokenPurpose.PasswordReset && x.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { message = "Password updated. Sign in with your new password." });
    }

    [Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await db.CustomerUsers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return Ok(new CustomerIdentity(user.Id, user.Name, user.Email, user.EmailVerifiedAt is not null));
    }

    private bool WithinCooldown(DateTimeOffset? sentAt) =>
        sentAt.HasValue && sentAt.Value.AddSeconds(
            Math.Max(1, _options.ResendCooldownSeconds)) > DateTimeOffset.UtcNow;

    private async Task<CustomerAuthToken?> FindTokenAsync(
        string raw, CustomerAuthTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 256) return null;
        var hash = TokenHash(raw);
        return await db.CustomerAuthTokens.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == purpose, ct);
    }

    private async Task<string> CreateRegistrationTokenAsync(
        CustomerUser customer, CancellationToken ct)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        db.CustomerAuthTokens.Add(new CustomerAuthToken
        {
            CustomerUserId = customer.Id,
            Purpose = CustomerAuthTokenPurpose.RegistrationEdit,
            TokenHash = TokenHash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(
                Math.Clamp(_options.VerificationLifetimeHours, 1, 168))
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    private async Task<bool> IssueEmailAsync(
        CustomerUser customer, CustomerAuthTokenPurpose purpose, CancellationToken ct)
    {
        await using var issuance = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM customer_users WHERE \"Id\" = {customer.Id} FOR UPDATE", ct);
        var lastSent = await db.CustomerUsers.AsNoTracking()
            .Where(x => x.Id == customer.Id)
            .Select(x => purpose == CustomerAuthTokenPurpose.EmailVerification
                ? x.LastVerificationEmailSentAt : x.LastPasswordResetEmailSentAt)
            .SingleAsync(ct);
        if (WithinCooldown(lastSent)) return true;

        var now = DateTimeOffset.UtcNow;
        await db.CustomerAuthTokens
            .Where(x => x.CustomerUserId == customer.Id && x.Purpose == purpose
                && x.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now), ct);

        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var record = new CustomerAuthToken
        {
            CustomerUserId = customer.Id,
            Purpose = purpose,
            TokenHash = TokenHash(raw),
            CreatedAt = now,
            ExpiresAt = purpose == CustomerAuthTokenPurpose.EmailVerification
                ? now.AddHours(Math.Clamp(_options.VerificationLifetimeHours, 1, 168))
                : now.AddMinutes(Math.Clamp(_options.ResetLifetimeMinutes, 1, 60))
        };
        db.CustomerAuthTokens.Add(record);
        if (purpose == CustomerAuthTokenPurpose.EmailVerification)
            customer.LastVerificationEmailSentAt = now;
        else
            customer.LastPasswordResetEmailSentAt = now;
        customer.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await issuance.CommitAsync(ct);

        var isVerification = purpose == CustomerAuthTokenPurpose.EmailVerification;
        var action = isVerification ? "verify-email" : "reset-password";
        var url = $"{_options.FrontendBaseUrl.TrimEnd('/')}/auth/{action}?token={raw}";
        var label = isVerification ? "Verify email" : "Reset password";
        var expiry = isVerification ? "24 hours" : "15 minutes";
        var html = $"<p>Use the link below to {label.ToLowerInvariant()} for your Zetruv account.</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{label}</a></p>" +
            $"<p>The link expires in {expiry}. If you did not request this, ignore this email.</p>";
        try
        {
            await emails.SendAsync(customer.Email, $"Zetruv — {label}", html, ct);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Customer auth email delivery failed.");
            await db.CustomerAuthTokens.Where(x => x.Id == record.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt,
                    DateTimeOffset.UtcNow), ct);
            if (isVerification)
                customer.LastVerificationEmailSentAt = null;
            else
                customer.LastPasswordResetEmailSentAt = null;
            await db.SaveChangesAsync(ct);
            return false;
        }
    }
}
