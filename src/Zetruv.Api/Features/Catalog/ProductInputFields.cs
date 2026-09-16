using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zetruv.Api.Features.Catalog;

public enum ProductInputFieldScope
{
    AccountValidation,
    LoginCredential
}

public enum ProductInputFieldType
{
    Text,
    Password,
    Email,
    Number,
    Select
}

public sealed class ProductInputField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ProductInputFieldScope Scope { get; set; }
    public ProductInputFieldType Type { get; set; } = ProductInputFieldType.Text;
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsSensitive { get; set; }
    public int MaxLength { get; set; } = 200;
    public string? OptionsJson { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ProductInputFieldResponse(
    Guid Id,
    string Key,
    string Label,
    ProductInputFieldScope Scope,
    ProductInputFieldType Type,
    string? Placeholder,
    string? HelpText,
    bool IsRequired,
    bool IsSensitive,
    int MaxLength,
    IReadOnlyList<string> Options,
    int SortOrder);

public sealed record UpsertProductInputFieldRequest(
    [Required, MaxLength(60)] string Key,
    [Required, MaxLength(120)] string Label,
    ProductInputFieldScope Scope,
    ProductInputFieldType Type,
    [MaxLength(160)] string? Placeholder,
    [MaxLength(500)] string? HelpText,
    bool IsRequired,
    bool IsSensitive,
    [Range(1, 1000)] int MaxLength,
    IReadOnlyList<string>? Options,
    int SortOrder);

public sealed record ProductInputPayloadResult(
    IReadOnlyDictionary<string, string>? Fields,
    string? Error)
{
    public static ProductInputPayloadResult Success(IReadOnlyDictionary<string, string> fields) => new(fields, null);
    public static ProductInputPayloadResult Failure(string error) => new(null, error);
}

public static partial class ProductInputFieldRules
{
    private static readonly HashSet<string> BlockedTransientKeys = new(
        ["otp", "2fa", "totp", "verificationcode", "verification_code", "cookie", "session", "sessionid", "session_id", "token"],
        StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,60}$")]
    private static partial Regex KeyRegex();

    public static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();

    public static string? ValidateDefinition(
        FulfillmentMethod fulfillmentMethod,
        UpsertProductInputFieldRequest request)
    {
        var key = request.Key?.Trim() ?? string.Empty;
        if (!KeyRegex().IsMatch(key))
        {
            return "Field key must use letters, numbers, dot, dash, or underscore and be at most 60 characters.";
        }

        if (BlockedTransientKeys.Contains(key))
        {
            return $"One-time/session field '{key}' is not supported.";
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return "Field label is required.";
        }

        var expectedScope = fulfillmentMethod switch
        {
            FulfillmentMethod.AUTO_ID => ProductInputFieldScope.AccountValidation,
            FulfillmentMethod.MANUAL_LOGIN => ProductInputFieldScope.LoginCredential,
            _ => (ProductInputFieldScope?)null
        };

        if (!expectedScope.HasValue)
        {
            return "This product fulfillment method does not accept an input schema.";
        }

        if (request.Scope != expectedScope.Value)
        {
            return $"{fulfillmentMethod} products require {expectedScope.Value} input fields.";
        }

        if (request.Scope == ProductInputFieldScope.AccountValidation &&
            (request.IsSensitive || request.Type == ProductInputFieldType.Password))
        {
            return "Account validation fields cannot be sensitive or password fields.";
        }

        if (request.Type == ProductInputFieldType.Password && !request.IsSensitive)
        {
            return "Password fields must be marked sensitive.";
        }

        var options = CleanOptions(request.Options);
        if (request.Type == ProductInputFieldType.Select)
        {
            if (options.Count == 0)
            {
                return "Select fields require at least one option.";
            }

            if (options.Count > 50 || options.Any(x => x.Length > 100))
            {
                return "Select fields support up to 50 options of at most 100 characters each.";
            }
        }
        else if (options.Count > 0)
        {
            return "Options are only accepted for Select fields.";
        }

        return null;
    }

    public static ProductInputPayloadResult NormalizePayload(
        IEnumerable<ProductInputField> schema,
        ProductInputFieldScope scope,
        IReadOnlyDictionary<string, string>? fields)
    {
        var definitions = schema
            .Where(x => x.Scope == scope)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Label)
            .ToList();

        if (definitions.Count == 0)
        {
            return ProductInputPayloadResult.Failure("Product input schema is not configured.");
        }

        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fields is not null)
        {
            foreach (var pair in fields)
            {
                var key = pair.Key?.Trim() ?? string.Empty;
                if (!definitions.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    return ProductInputPayloadResult.Failure($"Unexpected input field '{key}'.");
                }

                if (!supplied.TryAdd(key, pair.Value ?? string.Empty))
                {
                    return ProductInputPayloadResult.Failure($"Duplicate input field '{key}'.");
                }
            }
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            supplied.TryGetValue(definition.Key, out var rawValue);
            var value = rawValue ?? string.Empty;
            var normalizedValue = definition.IsSensitive ? value : value.Trim();

            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                if (definition.IsRequired)
                {
                    return ProductInputPayloadResult.Failure($"Field '{definition.Label}' is required.");
                }

                continue;
            }

            if (normalizedValue.Length > definition.MaxLength)
            {
                return ProductInputPayloadResult.Failure(
                    $"Field '{definition.Label}' must be at most {definition.MaxLength} characters.");
            }

            if (definition.Type == ProductInputFieldType.Email &&
                !new EmailAddressAttribute().IsValid(normalizedValue))
            {
                return ProductInputPayloadResult.Failure($"Field '{definition.Label}' must be a valid email address.");
            }

            if (definition.Type == ProductInputFieldType.Number &&
                !decimal.TryParse(normalizedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                return ProductInputPayloadResult.Failure($"Field '{definition.Label}' must be numeric.");
            }

            if (definition.Type == ProductInputFieldType.Select)
            {
                var options = ParseOptions(definition.OptionsJson);
                if (!options.Contains(normalizedValue, StringComparer.OrdinalIgnoreCase))
                {
                    return ProductInputPayloadResult.Failure($"Field '{definition.Label}' contains an invalid option.");
                }
            }

            normalized[definition.Key] = normalizedValue;
        }

        return ProductInputPayloadResult.Success(normalized);
    }

    public static ProductInputFieldResponse ToResponse(ProductInputField field) =>
        new(
            field.Id,
            field.Key,
            field.Label,
            field.Scope,
            field.Type,
            field.Placeholder,
            field.HelpText,
            field.IsRequired,
            field.IsSensitive,
            field.MaxLength,
            ParseOptions(field.OptionsJson),
            field.SortOrder);

    public static IReadOnlyList<string> CleanOptions(IReadOnlyList<string>? options) =>
        options is null
            ? []
            : options
                .Select(x => x?.Trim() ?? string.Empty)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static IReadOnlyList<string> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
