using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace Marketplace.Tools.Seeding;

// Writes a dataset with binary COPY in one transaction, so a failed run leaves nothing behind. Columns with a
// database default (currency) and the generated search_vector are left out.
public sealed class SeedWriter(NpgsqlDataSource dataSource, TextWriter log)
{
    public async Task WriteAsync(SeedDataset data, string passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureEmptyAsync(connection, cancellationToken);
        var roles = await RoleIdsAsync(connection, cancellationToken);
        var random = new Random(data.Options.Seed + 1);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TimedAsync("users", () => WriteUsersAsync(connection, data, passwordHash, cancellationToken));
        await TimedAsync("user_roles", () => WriteUserRolesAsync(connection, data, roles, cancellationToken));
        await TimedAsync("seller_profiles", () => WriteSellersAsync(connection, data, cancellationToken));
        await TimedAsync("products", () => WriteProductsAsync(connection, data, cancellationToken));
        await TimedAsync("orders", () => WriteOrdersAsync(connection, data, cancellationToken));
        await TimedAsync("order_items", () => WriteItemsAsync(connection, data, cancellationToken));
        await TimedAsync("payments", () => WritePaymentsAsync(connection, data, cancellationToken));
        await TimedAsync("download_grants", () => WriteGrantsAsync(connection, data, cancellationToken));
        await TimedAsync("seller_ledger_entries", () => WriteLedgerAsync(connection, data, random, cancellationToken));
        await TimedAsync("refund_requests", () => WriteRefundRequestsAsync(connection, data, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        await TimedAsync("analyze", async () =>
        {
            await using var analyze = new NpgsqlCommand("ANALYZE", connection);
            await analyze.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        });
    }

    private static async Task EnsureEmptyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM orders) + (SELECT count(*) FROM users WHERE normalized_email LIKE '%@SEED.TEST')", connection);
        if ((long)(await command.ExecuteScalarAsync(cancellationToken))! > 0)
        {
            throw new InvalidOperationException("The database already has orders or seeded users. Seed an empty database.");
        }
    }

    private static async Task<Dictionary<string, Guid>> RoleIdsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var roles = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT name, id FROM roles", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles[reader.GetString(0)] = reader.GetGuid(1);
        }

        return roles;
    }

    private async Task TimedAsync(string table, Func<Task<ulong>> write) => await TimedAsync(table, async () => (int)await write());

    private async Task TimedAsync(string table, Func<Task<int>> write)
    {
        var watch = Stopwatch.StartNew();
        var rows = await write();
        await log.WriteLineAsync($"{table,-22} {rows,10:N0} rows  {watch.Elapsed.TotalSeconds,6:0.0} s");
    }

    private static async Task<ulong> WriteUsersAsync(NpgsqlConnection connection, SeedDataset data, string passwordHash, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY users (id, display_name, created_at, user_name, normalized_user_name, email, normalized_email, email_confirmed, password_hash, " +
            "security_stamp, concurrency_stamp, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var user in data.SellerUsers.Concat(data.Buyers).Prepend(data.Admin))
        {
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(user.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(user.DisplayName, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(user.CreatedAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync(user.Email, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(user.Email.ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(user.Email, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(user.Email.ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(true, NpgsqlDbType.Boolean, cancellationToken);
            await copy.WriteAsync(passwordHash, NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(user.Id.ToString("N").ToUpperInvariant(), NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(user.Id.ToString(), NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
            await copy.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
            await copy.WriteAsync(true, NpgsqlDbType.Boolean, cancellationToken);
            await copy.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task<ulong> WriteUserRolesAsync(NpgsqlConnection connection, SeedDataset data, Dictionary<string, Guid> roles, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync("COPY user_roles (user_id, role_id) FROM STDIN (FORMAT BINARY)", cancellationToken);
        await copy.WriteRowAsync(cancellationToken, data.Admin.Id, roles["Admin"]);
        foreach (var user in data.SellerUsers)
        {
            await copy.WriteRowAsync(cancellationToken, user.Id, roles["Seller"]);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task<ulong> WriteSellersAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY seller_profiles (id, user_id, display_name, bio, country, created_at) FROM STDIN (FORMAT BINARY)", cancellationToken);
        foreach (var seller in data.Sellers)
        {
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(seller.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.SellerUsers[seller.UserIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(seller.DisplayName, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync("Digital goods made with care.", NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(seller.Country, NpgsqlDbType.Char, cancellationToken);
            await copy.WriteAsync(seller.CreatedAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task<ulong> WriteProductsAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY products (id, seller_id, category_id, title, description, price, status, file_key, file_name, file_size_bytes, " +
            "file_content_type, file_sha256, created_at, updated_at, published_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var product in data.Products)
        {
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Sellers[product.SellerIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(product.CategoryId, NpgsqlDbType.Integer, cancellationToken);
            await copy.WriteAsync(product.Title, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(product.Description, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(product.Price, NpgsqlDbType.Numeric, cancellationToken);
            await copy.WriteAsync(product.Status, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(copy, product.FileKey, NpgsqlDbType.Varchar, cancellationToken);
            await WriteNullableAsync(copy, product.FileName, NpgsqlDbType.Varchar, cancellationToken);
            await WriteNullableAsync<long>(copy, product.FileKey is null ? null : product.FileSize, NpgsqlDbType.Bigint, cancellationToken);
            await WriteNullableAsync(copy, product.FileContentType, NpgsqlDbType.Varchar, cancellationToken);
            await WriteNullableAsync(copy, product.FileSha256, NpgsqlDbType.Char, cancellationToken);
            await copy.WriteAsync(product.CreatedAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync((product.PublishedAt ?? product.CreatedAt).UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(copy, product.PublishedAt?.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task<ulong> WriteOrdersAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY orders (id, buyer_id, status, total, created_at, updated_at, expires_at, paid_at, cancelled_at, expired_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var order in data.Orders)
        {
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(order.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Buyers[order.BuyerIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(order.Status.ToString(), NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(order.Total, NpgsqlDbType.Numeric, cancellationToken);
            await copy.WriteAsync(order.CreatedAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync(UpdatedAt(data, order).UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync((order.CreatedAt + TimeSpan.FromMinutes(30)).UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(copy, order.PaidAt?.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(copy, order.Status == SeedOrderStatus.Cancelled ? order.ClosedAt?.UtcDateTime : null, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(copy, order.Status == SeedOrderStatus.Expired ? order.ClosedAt?.UtcDateTime : null, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task<ulong> WriteItemsAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY order_items (id, order_id, product_id, seller_id, product_title, unit_price, platform_fee, seller_amount, status, refunded_at) " +
            "FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var item in data.Items)
        {
            var product = data.Products[item.ProductIndex];
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(item.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Orders[item.OrderIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Sellers[product.SellerIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(product.Title, NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(item.UnitPrice, NpgsqlDbType.Numeric, cancellationToken);
            await copy.WriteAsync(item.PlatformFee, NpgsqlDbType.Numeric, cancellationToken);
            await copy.WriteAsync(item.UnitPrice - item.PlatformFee, NpgsqlDbType.Numeric, cancellationToken);
            await copy.WriteAsync(item.RefundedAt is null ? "Active" : "Refunded", NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(copy, item.RefundedAt?.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task<ulong> WritePaymentsAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY payments (id, order_id, provider, provider_payment_id, amount, status, failure_code, created_at, updated_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var order in data.Orders)
        {
            if (order.HadFailedAttempt)
            {
                var at = order.CreatedAt + TimeSpan.FromSeconds(5);
                await WritePaymentAsync(copy, order.FailedPaymentId, order, "Failed", "card_declined", at, at + TimeSpan.FromSeconds(3), cancellationToken);
            }

            if (order.PaidAt is not null)
            {
                var status = order.Status == SeedOrderStatus.Refunded ? "Refunded" : "Succeeded";
                await WritePaymentAsync(copy, order.PaymentId, order, status, null, order.CreatedAt + TimeSpan.FromSeconds(10), UpdatedAt(data, order), cancellationToken);
            }
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task WritePaymentAsync(
        NpgsqlBinaryImporter copy, Guid id, SeedOrder order, string status, string? failureCode, DateTimeOffset created, DateTimeOffset updated,
        CancellationToken cancellationToken)
    {
        await copy.StartRowAsync(cancellationToken);
        await copy.WriteAsync(id, NpgsqlDbType.Uuid, cancellationToken);
        await copy.WriteAsync(order.Id, NpgsqlDbType.Uuid, cancellationToken);
        await copy.WriteAsync("Fake", NpgsqlDbType.Varchar, cancellationToken);
        await copy.WriteAsync("pi_seed_" + id.ToString("N"), NpgsqlDbType.Varchar, cancellationToken);
        await copy.WriteAsync(order.Total, NpgsqlDbType.Numeric, cancellationToken);
        await copy.WriteAsync(status, NpgsqlDbType.Text, cancellationToken);
        await WriteNullableAsync(copy, failureCode, NpgsqlDbType.Varchar, cancellationToken);
        await copy.WriteAsync(created.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
        await copy.WriteAsync(updated.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
    }

    private static async Task<ulong> WriteGrantsAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY download_grants (id, order_item_id, buyer_id, product_id, created_at, expires_at, max_downloads, download_count, revoked_at) " +
            "FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var item in data.Items)
        {
            var order = data.Orders[item.OrderIndex];
            if (order.PaidAt is not { } paidAt)
            {
                continue;
            }

            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(item.GrantId, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(item.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Buyers[order.BuyerIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Products[item.ProductIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(paidAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync((paidAt + TimeSpan.FromDays(365)).UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync(10, NpgsqlDbType.Integer, cancellationToken);
            await copy.WriteAsync(item.DownloadCount, NpgsqlDbType.Integer, cancellationToken);
            await WriteNullableAsync(copy, item.RefundedAt?.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    // Section 9.6: Sale and PlatformFee when paid; Refund and FeeRefund when refunded.
    private static async Task<ulong> WriteLedgerAsync(NpgsqlConnection connection, SeedDataset data, Random random, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY seller_ledger_entries (id, seller_id, order_item_id, entry_type, amount, created_at) FROM STDIN (FORMAT BINARY)", cancellationToken);
        foreach (var item in data.Items)
        {
            var order = data.Orders[item.OrderIndex];
            if (order.PaidAt is not { } paidAt)
            {
                continue;
            }

            var sellerId = data.Sellers[data.Products[item.ProductIndex].SellerIndex].Id;
            await WriteEntryAsync(copy, Ids.Version7(paidAt, random), sellerId, item.Id, "Sale", item.UnitPrice, paidAt, cancellationToken);
            await WriteEntryAsync(copy, Ids.Version7(paidAt, random), sellerId, item.Id, "PlatformFee", -item.PlatformFee, paidAt, cancellationToken);
            if (item.RefundedAt is { } refundedAt)
            {
                await WriteEntryAsync(copy, Ids.Version7(refundedAt, random), sellerId, item.Id, "Refund", -item.UnitPrice, refundedAt, cancellationToken);
                await WriteEntryAsync(copy, Ids.Version7(refundedAt, random), sellerId, item.Id, "FeeRefund", item.PlatformFee, refundedAt, cancellationToken);
            }
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static async Task WriteEntryAsync(
        NpgsqlBinaryImporter copy, Guid id, Guid sellerId, Guid itemId, string type, decimal amount, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await copy.StartRowAsync(cancellationToken);
        await copy.WriteAsync(id, NpgsqlDbType.Uuid, cancellationToken);
        await copy.WriteAsync(sellerId, NpgsqlDbType.Uuid, cancellationToken);
        await copy.WriteAsync(itemId, NpgsqlDbType.Uuid, cancellationToken);
        await copy.WriteAsync(type, NpgsqlDbType.Text, cancellationToken);
        await copy.WriteAsync(amount, NpgsqlDbType.Numeric, cancellationToken);
        await copy.WriteAsync(at.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
    }

    // Refunded items have a completed request; a few active items have a rejected one.
    private static async Task<ulong> WriteRefundRequestsAsync(NpgsqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY refund_requests (id, order_item_id, requested_by, reason_text, status, policy_verdict, download_count_at_request, created_at, " +
            "updated_at, decided_by, decision, decision_note, decided_at, executed_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var item in data.Items)
        {
            if (item.RefundedAt is null && !item.RefundRejected)
            {
                continue;
            }

            var order = data.Orders[item.OrderIndex];
            var completed = item.RefundedAt is not null;
            var requested = Ids.CreatedAt(item.RefundRequestId);
            var decided = requested + TimeSpan.FromMinutes(30);
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(item.RefundRequestId, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(item.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(data.Buyers[order.BuyerIndex].Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(completed ? "The file does not match the description." : "I changed my mind about this purchase.", NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(completed ? "Completed" : "Rejected", NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(item.DownloadCount > 0 ? "EligibleDownloaded" : "Eligible", NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(item.DownloadCount, NpgsqlDbType.Integer, cancellationToken);
            await copy.WriteAsync(requested.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync((item.RefundedAt ?? decided).UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await copy.WriteAsync(data.Admin.Id, NpgsqlDbType.Uuid, cancellationToken);
            await copy.WriteAsync(completed ? "Approved" : "Rejected", NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(completed ? "Approved under the refund policy." : "Downloaded and outside the stated reasons.", NpgsqlDbType.Varchar, cancellationToken);
            await copy.WriteAsync(decided.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(copy, item.RefundedAt?.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        return await copy.CompleteAsync(cancellationToken);
    }

    private static DateTimeOffset UpdatedAt(SeedDataset data, SeedOrder order)
    {
        var updated = order.PaidAt ?? order.ClosedAt ?? order.CreatedAt;
        for (var i = order.FirstItem; i < order.FirstItem + order.ItemCount; i++)
        {
            if (data.Items[i].RefundedAt is { } refundedAt && refundedAt > updated)
            {
                updated = refundedAt;
            }
        }

        return updated;
    }

    private static Task WriteNullableAsync<T>(NpgsqlBinaryImporter copy, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : class =>
        value is null ? copy.WriteNullAsync(cancellationToken) : copy.WriteAsync(value, type, cancellationToken);

    private static Task WriteNullableAsync<T>(NpgsqlBinaryImporter copy, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct =>
        value is null ? copy.WriteNullAsync(cancellationToken) : copy.WriteAsync(value.Value, type, cancellationToken);
}
