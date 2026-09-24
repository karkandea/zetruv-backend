using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zetruv.Api.Features.Catalog;

public enum GameAccountAttributeType { Text, Number, Boolean, Select, MultiSelect }

public sealed class GameAccountAttributeDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public Game Game { get; set; } = null!;
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public GameAccountAttributeType Type { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnCard { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record UpsertGameAccountAttributeRequest(
    [Required, MaxLength(50)] string Key,
    [Required, MaxLength(100)] string Label,
    GameAccountAttributeType Type,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    bool IsActive,
    bool ShowOnCard,
    int SortOrder);

public sealed record GameAccountAttributeDefinitionResponse(
    Guid Id, string Key, string Label, GameAccountAttributeType Type,
    IReadOnlyList<string> Options, bool IsRequired, bool IsActive,
    bool ShowOnCard, int SortOrder);

public sealed record GameAccountAttributeResponse(
    string Key, string Label, GameAccountAttributeType Type,
    JsonElement Value, bool ShowOnCard, int SortOrder);

public sealed record GameAccountDetailsResponse(
    Guid? GameId, IReadOnlyList<GameAccountAttributeResponse> Attributes);

public sealed record GameAccountDetailsEditorResponse(
    Guid ProductId, Guid? GameId, IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyList<GameAccountAttributeDefinitionResponse> Schema, bool IsLegacyUnlinked);

public sealed record UpdateGameAccountDetailsRequest(
    [Required] Dictionary<string, JsonElement> Attributes);

public static partial class GameAccountAttributeRules
{
    public const int MaxAttributes = 30;
    public const int MaxOptions = 20;
    private static readonly Regex KeyPattern = AttributeKeyRegex();

    [GeneratedRegex("^[a-z][A-Za-z0-9_]{1,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeKeyRegex();

    public static IReadOnlyList<string> Options(GameAccountAttributeDefinition definition) =>
        JsonSerializer.Deserialize<string[]>(definition.OptionsJson) ?? [];

    public static GameAccountAttributeDefinitionResponse ToResponse(
        GameAccountAttributeDefinition definition) =>
        new(definition.Id, definition.Key, definition.Label, definition.Type,
            Options(definition), definition.IsRequired, definition.IsActive,
            definition.ShowOnCard, definition.SortOrder);

    public static string? ValidateDefinition(
        UpsertGameAccountAttributeRequest request, out IReadOnlyList<string> options)
    {
        options = [];
        if (string.IsNullOrWhiteSpace(request.Key) || !KeyPattern.IsMatch(request.Key))
            return "Attribute key must be 2-50 characters, starting with a lowercase letter and containing letters, numbers or underscores.";
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 100)
            return "Attribute label must contain 1-100 characters.";
        if (!Enum.IsDefined(request.Type))
            return "Unsupported attribute type.";
        var list = (request.Options ?? []).Select(x => x?.Trim() ?? "").ToList();
        if (list.Count > MaxOptions || list.Any(x => x.Length is < 1 or > 100) ||
            list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
            return "Options must contain at most 20 distinct nonempty values of 100 characters or fewer.";
        if (request.Type is GameAccountAttributeType.Select or GameAccountAttributeType.MultiSelect)
        {
            if (list.Count == 0) return "Select and MultiSelect require at least one option.";
        }
        else if (list.Count != 0)
            return "Options are supported only for Select and MultiSelect.";
        options = list;
        return null;
    }

    public static Dictionary<string, JsonElement> ParseValues(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    public static string? NormalizeValues(
        IReadOnlyList<GameAccountAttributeDefinition> definitions,
        IReadOnlyDictionary<string, JsonElement> submitted,
        out string normalizedJson)
    {
        normalizedJson = "{}";
        if (submitted.Count > MaxAttributes)
            return "Too many account attributes.";
        var active = definitions.Where(x => x.IsActive)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var key in submitted.Keys)
            if (!active.ContainsKey(key))
                return $"Attribute '{key}' is not configured and active for this game.";

        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var definition in active.Values)
        {
            if (!submitted.TryGetValue(definition.Key, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (definition.IsRequired) return $"Attribute '{definition.Key}' is required.";
                continue;
            }
            var options = Options(definition);
            object? typed;
            switch (definition.Type)
            {
                case GameAccountAttributeType.Text:
                    if (value.ValueKind != JsonValueKind.String) return $"'{definition.Key}' must be text.";
                    var text = value.GetString()?.Trim() ?? "";
                    if (text.Length > 250) return $"'{definition.Key}' text exceeds 250 characters.";
                    typed = text.Length == 0 ? null : text;
                    break;
                case GameAccountAttributeType.Number:
                    if (value.ValueKind != JsonValueKind.Number ||
                        !value.TryGetDecimal(out var number) || number < 0 || number > 999999999.99m ||
                        decimal.Round(number, 2) != number)
                        return $"'{definition.Key}' must be a nonnegative number with at most 2 decimals.";
                    typed = number;
                    break;
                case GameAccountAttributeType.Boolean:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return $"'{definition.Key}' must be true or false.";
                    typed = value.GetBoolean();
                    break;
                case GameAccountAttributeType.Select:
                    if (value.ValueKind != JsonValueKind.String ||
                        !options.Contains(value.GetString() ?? "", StringComparer.Ordinal))
                        return $"'{definition.Key}' must match a configured option.";
                    typed = value.GetString();
                    break;
                case GameAccountAttributeType.MultiSelect:
                    if (value.ValueKind != JsonValueKind.Array)
                        return $"'{definition.Key}' must be an array.";
                    var items = value.EnumerateArray().ToArray();
                    if (items.Length > MaxOptions || items.Any(x => x.ValueKind != JsonValueKind.String))
                        return $"'{definition.Key}' contains invalid multi-select values.";
                    var selected = items.Select(x => x.GetString() ?? "").ToList();
                    if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Count ||
                        selected.Any(x => !options.Contains(x, StringComparer.Ordinal)))
                        return $"'{definition.Key}' must contain distinct configured options.";
                    typed = selected.Count == 0 ? null : selected;
                    break;
                default:
                    return $"Unsupported type for '{definition.Key}'.";
            }
            if (typed is null)
            {
                if (definition.IsRequired) return $"Attribute '{definition.Key}' is required.";
                continue;
            }
            normalized.Add(definition.Key, typed);
        }
        normalizedJson = JsonSerializer.Serialize(normalized);
        return null;
    }

    public static GameAccountDetailsResponse ToPublic(
        Guid? gameId, string json,
        IReadOnlyList<GameAccountAttributeDefinition> definitions, bool cardOnly = false)
    {
        var values = ParseValues(json);
        var fields = definitions.Where(x => x.IsActive && (!cardOnly || x.ShowOnCard))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Key)
            .Where(x => values.ContainsKey(x.Key))
            .Select(x => new GameAccountAttributeResponse(
                x.Key, x.Label, x.Type, values[x.Key], x.ShowOnCard, x.SortOrder))
            .ToList();

        // Preserve visibility of historical records that predate per-game assignment.
        // Such records are read-only until an administrator creates a game-linked listing.
        if (gameId is null)
        {
            var legacy = new (string Key, string Label, GameAccountAttributeType Type, bool Card)[]
            {
                ("rank", "Rank", GameAccountAttributeType.Text, true),
                ("skinCount", "Skin count", GameAccountAttributeType.Number, true),
                ("region", "Region", GameAccountAttributeType.Text, true),
                ("level", "Level", GameAccountAttributeType.Number, false),
                ("additionalInfo", "Additional info", GameAccountAttributeType.Text, false)
            };
            fields = legacy.Where(x => values.ContainsKey(x.Key) && (!cardOnly || x.Card))
                .Select((x, i) => new GameAccountAttributeResponse(
                    x.Key, x.Label, x.Type, values[x.Key], x.Card, i))
                .ToList();
        }

        return new GameAccountDetailsResponse(gameId, fields);
    }
}
