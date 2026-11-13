using Marketplace.Domain.Common;

namespace Marketplace.Application.Common;

// Maps PostgreSQL failures to client errors without exposing constraint names (section 4.6 and section 7.4).
public static class DatabaseErrors
{
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string CheckViolation = "23514";

    public static readonly Error ConcurrencyConflict =
        new("concurrency_conflict", "The resource was changed by another request. Reload it and try again.", ErrorType.Conflict);

    // Unique constraints whose violation has a specific meaning in the section 8.2 error catalogue.
    private static readonly Dictionary<string, Error> _uniqueConstraintErrors = new(StringComparer.Ordinal)
    {
        ["ux_seller_profiles_user_id"] = new("already_seller", "You already have a seller profile.", ErrorType.Conflict),
        ["ux_download_grants_active_buyer_product"] = new("already_owned", "You already own this product.", ErrorType.Conflict),
        ["ux_refund_requests_live_order_item"] = new("refund_not_allowed", "A refund has already been requested for this item.", ErrorType.Conflict),
    };

    // Returns an error for a unique violation, or null for anything else: foreign key and check
    // violations are programming errors (section 7.4) and must surface as a 500.
    public static Error? FromPostgres(string? sqlState, string? constraintName)
    {
        if (sqlState != UniqueViolation)
        {
            return null;
        }

        return constraintName is not null && _uniqueConstraintErrors.TryGetValue(constraintName, out var error)
            ? error
            : ConcurrencyConflict;
    }

    public static bool IsBug(string? sqlState) => sqlState is ForeignKeyViolation or CheckViolation;
}
