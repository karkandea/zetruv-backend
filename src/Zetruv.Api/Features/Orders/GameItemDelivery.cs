using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record GameItemDeliveryField(string Label, string Value);

public static partial class GameItemDelivery
{
    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex InternationalPhone();

    public static bool IsWhatsAppNumber(string value) => InternationalPhone().IsMatch(value);

    public static Task<string?> GetSupportPhoneAsync(ZetruvDbContext db, CancellationToken ct) =>
        db.SiteSettings.AsNoTracking().OrderBy(x => x.CreatedAt)
            .Select(x => x.GameItemSupportPhone).FirstOrDefaultAsync(ct);

    public static IReadOnlyList<GameItemDeliveryField>? Read(
        ManualLoginCredentialProtector protector, Guid orderItemId, string payload)
    {
        if (!protector.IsConfigured) return null;

        try
        {
            return protector.Unprotect(orderItemId, payload).Values
                .Select(value => JsonSerializer.Deserialize<GameItemDeliveryField>(value))
                .OfType<GameItemDeliveryField>().ToArray();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
