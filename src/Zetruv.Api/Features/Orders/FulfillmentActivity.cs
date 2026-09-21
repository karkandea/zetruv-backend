using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public enum FulfillmentActivityType
{
    ProviderAttemptStarted,
    ProviderAttemptSucceeded,
    ProviderAttemptFailed,
    ManualStatusChanged,
    CredentialRevealed,
    CredentialExpired,
    OrderCancelled
}

public enum FulfillmentActivitySource
{
    System,
    Payment,
    CmsAdmin,
    Background
}

public sealed class FulfillmentActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public FulfillmentActivityType Type { get; set; }
    public FulfillmentActivitySource Source { get; set; }
    public string? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public FulfillmentMethod FulfillmentMethod { get; set; }
    public FulfillmentStatus? FromStatus { get; set; }
    public FulfillmentStatus? ToStatus { get; set; }
    public int? AttemptNumber { get; set; }
    public string? Provider { get; set; }
    public string? ProviderReference { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record FulfillmentActor(string? Id, string? Email)
{
    public static FulfillmentActor System => new(null, null);

    public static FulfillmentActor FromClaims(ClaimsPrincipal user) =>
        new(
            user.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            user.FindFirstValue(JwtRegisteredClaimNames.Email) ??
            user.FindFirstValue(ClaimTypes.Email));
}

public sealed record FulfillmentExecutionContext(
    FulfillmentActivitySource Source,
    FulfillmentActor Actor)
{
    public static FulfillmentExecutionContext System =>
        new(FulfillmentActivitySource.System, FulfillmentActor.System);

    public static FulfillmentExecutionContext Payment =>
        new(FulfillmentActivitySource.Payment, FulfillmentActor.System);

    public static FulfillmentExecutionContext Admin(ClaimsPrincipal user) =>
        new(FulfillmentActivitySource.CmsAdmin, FulfillmentActor.FromClaims(user));
}

public sealed record FulfillmentActivityResponse(
    Guid Id,
    FulfillmentActivityType Type,
    FulfillmentActivitySource Source,
    string? ActorId,
    string? ActorEmail,
    FulfillmentStatus? FromStatus,
    FulfillmentStatus? ToStatus,
    int? AttemptNumber,
    string? Provider,
    string? ProviderReference,
    string? Message,
    DateTimeOffset CreatedAt);

public sealed class FulfillmentActivityService(ZetruvDbContext db)
{
    public FulfillmentActivity Create(
        OrderItem item,
        FulfillmentActivityType type,
        FulfillmentActivitySource source,
        FulfillmentActor actor,
        DateTimeOffset createdAt,
        FulfillmentStatus? fromStatus = null,
        FulfillmentStatus? toStatus = null,
        int? attemptNumber = null,
        string? provider = null,
        string? providerReference = null,
        string? message = null)
    {
        var activity = new FulfillmentActivity
        {
            OrderId = item.OrderId,
            OrderItemId = item.Id,
            Type = type,
            Source = source,
            ActorId = Clean(actor.Id),
            ActorEmail = Clean(actor.Email),
            FulfillmentMethod = item.FulfillmentMethod,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            AttemptNumber = attemptNumber,
            Provider = Clean(provider),
            ProviderReference = Clean(providerReference),
            Message = Clean(message),
            CreatedAt = createdAt
        };

        db.FulfillmentActivities.Add(activity);
        return activity;
    }

    public async Task<IReadOnlyList<FulfillmentActivityResponse>> GetAsync(
        Guid orderId,
        Guid orderItemId,
        CancellationToken cancellationToken = default) =>
        await db.FulfillmentActivities
            .AsNoTracking()
            .Where(x => x.OrderId == orderId && x.OrderItemId == orderItemId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new FulfillmentActivityResponse(
                x.Id,
                x.Type,
                x.Source,
                x.ActorId,
                x.ActorEmail,
                x.FromStatus,
                x.ToStatus,
                x.AttemptNumber,
                x.Provider,
                x.ProviderReference,
                x.Message,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
