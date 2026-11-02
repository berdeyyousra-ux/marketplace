using System.Text;
using System.Text.Json;
using FluentValidation;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Common;

// D-29 and section 8.1: keyset pagination. Lists return { items, nextCursor }, limit defaults to 20 (at most 100),
// and a cursor is the base64url JSON of the last row's sort keys. A malformed cursor is 400 invalid_cursor.
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

public static class Paging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static readonly Error InvalidCursor = new("invalid_cursor", "The pagination cursor is not valid.", ErrorType.Validation);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static string Encode<TKey>(TKey key) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(key, _json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode<TKey>(string? cursor, out TKey? key)
        where TKey : class
    {
        key = null;
        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            key = JsonSerializer.Deserialize<TKey>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)), _json);
            return key is not null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    // Fetches limit + 1 rows to know whether another page exists.
    public static Page<T> ToPage<T, TKey>(List<T> rows, int limit, Func<T, TKey> keyOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(keyOf);

        var hasMore = rows.Count > limit;
        var items = hasMore ? rows.GetRange(0, limit) : rows;
        return new Page<T>(items, hasMore ? Encode(keyOf(items[^1])) : null);
    }
}

public static class PagingRules
{
    public static IRuleBuilderOptions<T, int> ValidLimit<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, Paging.MaxLimit);
}
