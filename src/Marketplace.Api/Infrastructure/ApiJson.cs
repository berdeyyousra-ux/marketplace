using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Marketplace.Api.Infrastructure;

// Section 8.1 and section 11.2 JSON rules: camelCase, enums as strings, unknown members rejected, and request
// strings trimmed and normalized to NFC. Passwords and tokens are normalized but never trimmed (changelog 2026-11-02).
public static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new JsonStringEnumConverter());
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NormalizeStrings } };
    }

    public static bool IsSecret(string propertyName) =>
        propertyName.Contains("password", StringComparison.OrdinalIgnoreCase)
        || propertyName.Contains("token", StringComparison.OrdinalIgnoreCase);

    private static void NormalizeStrings(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties.Where(property => property.PropertyType == typeof(string)))
        {
            property.CustomConverter = IsSecret(property.Name) ? NormalizedString.SecretInstance : NormalizedString.Instance;
        }
    }

    private sealed class NormalizedString(bool trim) : JsonConverter<string>
    {
        public static readonly NormalizedString Instance = new(trim: true);
        public static readonly NormalizedString SecretInstance = new(trim: false);

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value is null)
            {
                return null;
            }

            var normalized = value.Normalize(NormalizationForm.FormC);
            return trim ? normalized.Trim() : normalized;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value);
        }
    }
}
