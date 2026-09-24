using System.ComponentModel.DataAnnotations;

namespace Zetruv.Api.Features.Auth;

public static class CustomerAuthConstants
{
    public const string Scheme = "CustomerBearer";
}

public sealed class CustomerUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public int TokenVersion { get; set; }
    public DateTimeOffset? LastVerificationEmailSentAt { get; set; }
    public DateTimeOffset? LastPasswordResetEmailSentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CustomerPasswordHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerUserId { get; set; }
    public CustomerUser CustomerUser { get; set; } = null!;
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum CustomerAuthTokenPurpose { EmailVerification, PasswordReset, RegistrationEdit }

public sealed class CustomerAuthToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerUserId { get; set; }
    public CustomerUser CustomerUser { get; set; } = null!;
    public CustomerAuthTokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class CustomerAuthOptions
{
    public const string SectionName = "CustomerAuth";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public int VerificationLifetimeHours { get; set; } = 24;
    public int ResetLifetimeMinutes { get; set; } = 15;
    public int ResendCooldownSeconds { get; set; } = 45;
    public int JwtExpiryMinutes { get; set; } = 480;
}

public sealed class CustomerEmailOptions
{
    public const string SectionName = "CustomerEmail";
    public string Provider { get; set; } = "disabled";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Zetruv";
    public string ResendApiKey { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string CaptureDirectory { get; set; } = "";
}

public sealed record CustomerRegisterRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required] string Password);
public sealed record CustomerLoginRequest(
    [Required, EmailAddress] string Email, [Required] string Password);
public sealed record CustomerEmailRequest([Required, EmailAddress] string Email);
public sealed record CustomerChangePendingEmailRequest(
    [Required] string RegistrationToken,
    [Required, EmailAddress, StringLength(320)] string NewEmail);
public sealed record CustomerVerifyRequest([Required] string Token);
public sealed record CustomerResetRequest(
    [Required] string Token, [Required] string NewPassword,
    [Required] string ConfirmPassword);
public sealed record CustomerIdentity(Guid Id, string Name, string Email, bool EmailVerified);
public sealed record CustomerLoginResponse(
    string AccessToken, DateTimeOffset ExpiresAt, CustomerIdentity Customer);
