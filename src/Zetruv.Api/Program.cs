using Scalar.AspNetCore;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zetruv.Api.Features.Articles;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Features.Home;
using Zetruv.Api.Features.Media;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Features.Payments;
using Zetruv.Api.Features.Shipping;
using Zetruv.Api.Features.Site;
using Zetruv.Api.Persistence;
using Zetruv.Api.OpenApi;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    var mockProviderSettings = new[]
    {
        "Payments:Provider",
        "Shipping:Provider",
        "GameAccountValidation:Provider"
    };

    var enabledMockProviders = mockProviderSettings
        .Where(key => string.Equals(
            builder.Configuration[key]?.Trim(),
            "mock",
            StringComparison.OrdinalIgnoreCase))
        .ToArray();

    if (enabledMockProviders.Length > 0)
    {
        throw new InvalidOperationException(
            $"Mock providers cannot be enabled in Production: {string.Join(", ", enabledMockProviders)}.");
    }

    if (!ManualLoginCredentialProtector.IsValidConfiguredKey(
            builder.Configuration["ManualLogin:EncryptionKey"]))
    {
        throw new InvalidOperationException(
            "ManualLogin:EncryptionKey must be configured as a base64-encoded 32-byte key in Production.");
    }
}

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("customer-auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown")
                + ":" + httpContext.Request.Path,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("order-lookup", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("shipping-quote", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("voucher-preview", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("payment-initiation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("game-account-validation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services.AddDbContext<ZetruvDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CustomerAuthOptions>(
    builder.Configuration.GetSection(CustomerAuthOptions.SectionName));
builder.Services.Configure<CustomerEmailOptions>(
    builder.Configuration.GetSection(CustomerEmailOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.Configure<MediaOptions>(
    builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.Configure<ShippingOptions>(
    builder.Configuration.GetSection(ShippingOptions.SectionName));

var configuredMediaOptions = builder.Configuration
    .GetSection(MediaOptions.SectionName)
    .Get<MediaOptions>() ?? new MediaOptions();
var configuredMediaMaxBytes = Math.Clamp(
    configuredMediaOptions.MaxFileSizeBytes,
    64 * 1024,
    10 * 1024 * 1024);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit =
        configuredMediaMaxBytes + (1024 * 1024));

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");

if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must contain at least 32 characters.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    .AddJwtBearer(CustomerAuthConstants.Scheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = CustomerJwtTokenService.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirst(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                var versionClaim = context.Principal?.FindFirst(
                    CustomerJwtTokenService.VersionClaim)?.Value;
                if (!Guid.TryParse(sub, out var userId) ||
                    !int.TryParse(versionClaim, out var version))
                {
                    context.Fail("Invalid customer session.");
                    return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<ZetruvDbContext>();
                var valid = await db.CustomerUsers.AsNoTracking().AnyAsync(
                    x => x.Id == userId && x.IsActive && x.EmailVerifiedAt != null
                         && x.TokenVersion == version,
                    context.HttpContext.RequestAborted);
                if (!valid) context.Fail("Customer session is no longer valid.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.CmsAdmin, policy =>
        policy.RequireRole(AdminRoles.Admin));
});

var allowedOrigins = (builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [])
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            return;
        }

        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<CustomerJwtTokenService>();
builder.Services.AddScoped<CustomerEmailSender>();
builder.Services.AddScoped<AdminSeeder>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CatalogSeeder>();
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderFulfillmentService>();
builder.Services.AddScoped<FulfillmentExecutionService>();
builder.Services.AddScoped<FulfillmentActivityService>();
builder.Services.AddScoped<FulfillmentQueueService>();
builder.Services.AddSingleton<ManualLoginCredentialProtector>();
builder.Services.AddScoped<ManualLoginCredentialService>();
builder.Services.AddHostedService<ManualLoginCredentialCleanupService>();
builder.Services.AddScoped<OrderAccessTokenService>();
builder.Services.AddScoped<OrderTrackingService>();
builder.Services.AddScoped<DiscountVoucherService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<InventoryReservationService>();
builder.Services.AddHostedService<InventoryReservationCleanupService>();
builder.Services.AddScoped<IMediaStorage, LocalMediaStorage>();
builder.Services.AddScoped<MediaStorageResolver>();
builder.Services.AddScoped<MediaService>();

if (!builder.Environment.IsProduction())
{
    builder.Services.AddScoped<IGameAccountValidator, MockGameAccountValidator>();
    builder.Services.AddScoped<IShippingProvider, MockShippingProvider>();
    builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
    builder.Services.AddScoped<IAutoIdFulfillmentProvider, MockAutoIdFulfillmentProvider>();
}

builder.Services.AddScoped<GameAccountValidatorResolver>();
builder.Services.AddScoped<GameAccountValidationService>();
builder.Services.AddScoped<ShippingProviderResolver>();
builder.Services.AddScoped<ShippingService>();
builder.Services.AddHostedService<ShippingQuotePiiCleanupService>();
builder.Services.AddScoped<ShipmentFulfillmentService>();
builder.Services.Configure<PaymentReconciliationOptions>(
    builder.Configuration.GetSection(PaymentReconciliationOptions.SectionName));
builder.Services.AddScoped<PaymentGatewayResolver>();
builder.Services.AddScoped<PaymentWebhookEventLedger>();
builder.Services.AddHostedService<PaymentReconciliationBackgroundService>();
builder.Services.AddScoped<AutoIdRuntimeProviderMappingService>();
builder.Services.AddScoped<AutoIdFulfillmentProviderResolver>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<SiteService>();
builder.Services.AddScoped<SiteSeeder>();
builder.Services.AddScoped<HomepageService>();
builder.Services.AddScoped<HomepageSeeder>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                       ForwardedHeaders.XForwardedProto
});

var mediaOptions = app.Services
    .GetRequiredService<IOptions<MediaOptions>>()
    .Value;

if (string.Equals(
        mediaOptions.Provider,
        "local",
        StringComparison.OrdinalIgnoreCase))
{
    var mediaRoot = MediaPaths.ResolveLocalRoot(mediaOptions, app.Environment);
    Directory.CreateDirectory(mediaRoot);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(mediaRoot),
        RequestPath = MediaPaths.NormalizePublicPath(mediaOptions.PublicPath),
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers.CacheControl =
                "public,max-age=31536000,immutable";
            context.Context.Response.Headers.XContentTypeOptions = "nosniff";
        }
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options
        .WithTitle("Zetruv API")
        .DisableAgent());
}

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ZetruvDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsProduction())
    {
        await ProductionProviderSafety.EnsureAutoIdMappingsAreSafeAsync(db);
    }

    await scope.ServiceProvider
        .GetRequiredService<AdminSeeder>()
        .SeedAsync();

    await scope.ServiceProvider
        .GetRequiredService<CatalogSeeder>()
        .SeedAsync();

    await scope.ServiceProvider
        .GetRequiredService<HomepageSeeder>()
        .SeedAsync();

    await scope.ServiceProvider
        .GetRequiredService<SiteSeeder>()
        .SeedAsync();
}

app.Run();
