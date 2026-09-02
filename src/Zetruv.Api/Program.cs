using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Zetruv.Api.Features.Articles;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Features.Home;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Features.Payments;
using Zetruv.Api.Features.Shipping;
using Zetruv.Api.Features.Site;
using Zetruv.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
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
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services.AddDbContext<ZetruvDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

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
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.CmsAdmin, policy =>
        policy.RequireRole(AdminRoles.Admin));
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

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
builder.Services.AddScoped<AdminSeeder>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CatalogSeeder>();
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderTrackingService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<InventoryReservationService>();
builder.Services.AddHostedService<InventoryReservationCleanupService>();
builder.Services.AddScoped<IGameAccountValidator, MockGameAccountValidator>();
builder.Services.AddScoped<GameAccountValidatorResolver>();
builder.Services.AddScoped<GameAccountValidationService>();
builder.Services.AddScoped<IShippingProvider, MockShippingProvider>();
builder.Services.AddScoped<ShippingProviderResolver>();
builder.Services.AddScoped<ShippingService>();
builder.Services.AddScoped<ShipmentFulfillmentService>();
builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
builder.Services.AddScoped<PaymentGatewayResolver>();
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
