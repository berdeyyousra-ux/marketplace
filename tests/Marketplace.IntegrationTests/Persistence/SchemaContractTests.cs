using System.Text;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Marketplace.IntegrationTests.Persistence;

// Section 7: the SQL in section 7.2 is the contract, and the migrations must produce it.
public sealed class SchemaContractTests : IAsyncLifetime
{
    private static readonly string[] _tables =
    [
        "__EFMigrationsHistory", "ai_suggestions", "audit_log", "categories", "download_grants", "idempotency_keys",
        "one_time_tokens", "order_items", "orders", "outbox_messages", "payment_events", "payments", "products",
        "refresh_tokens", "refund_requests", "report_refreshes", "role_claims", "roles", "seller_ledger_entries", "seller_profiles",
        "user_claims", "user_logins", "user_roles", "user_tokens", "users",
    ];

    // Every named constraint in section 7.2.
    private static readonly string[] _constraints =
    [
        "ux_refresh_tokens_token_hash", "ck_one_time_tokens_purpose", "ux_one_time_tokens_token_hash",
        "ux_seller_profiles_user_id", "ux_categories_slug", "ck_products_status", "ck_products_price",
        "ck_products_currency", "ck_products_published_has_file", "ck_orders_status", "ck_orders_total",
        "ck_orders_paid_at", "ux_order_items_order_id_product_id", "ck_order_items_status", "ck_order_items_unit_price",
        "ck_order_items_amounts", "ux_payments_provider_payment_id", "ck_payments_status", "ck_payments_amount",
        "ux_payment_events_provider_event", "ux_download_grants_order_item_id", "ck_download_grants_counts",
        "ux_refund_requests_order_item_id", "ck_refund_requests_status", "ck_refund_requests_verdict",
        "ck_refund_requests_decision", "ck_refund_requests_decided", "ck_ai_suggestions_status",
        "ck_idempotency_keys_status", "ck_ledger_entry_type", "ck_ledger_amount_sign", "ck_audit_log_actor_type",
    ];

    // Every named index in section 7.2, plus the unique Identity indexes from section 7.2's users comment.
    private static readonly string[] _indexes =
    [
        "ix_refresh_tokens_user_id", "ix_refresh_tokens_family_id", "ix_one_time_tokens_user_id_purpose",
        "ix_products_seller_id_status", "ix_products_category_published", "ix_products_price_published",
        "ix_products_search_vector", "ix_orders_buyer_id_created_at", "ix_orders_pending_expires_at", "ix_orders_paid_at",
        "ix_order_items_product_id", "ix_order_items_seller_id_order_id", "ix_payments_order_id",
        "ux_payments_one_live_per_order", "ix_payment_events_unprocessed", "ux_download_grants_active_buyer_product",
        "ix_refund_requests_status_created_at", "ix_ai_suggestions_refund_request_id", "ix_idempotency_keys_expires_at",
        "ix_outbox_messages_pending", "ux_ledger_item_type", "ix_ledger_seller_id_created_at", "ix_audit_log_entity",
        "ix_audit_log_occurred_at", "ux_users_normalized_email", "ux_users_normalized_user_name",
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task SchemaContract_AllTablesAndConstraintsExist()
    {
        var tables = await QueryAsync("SELECT tablename FROM pg_tables WHERE schemaname = 'public'");
        var constraints = await QueryAsync("SELECT conname FROM pg_constraint WHERE connamespace = 'public'::regnamespace");
        var indexes = await QueryAsync("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");

        tables.Order().ShouldBe(_tables.Order());
        _constraints.Except(constraints).ShouldBeEmpty();
        _indexes.Except(indexes).ShouldBeEmpty();
    }

    // Columns, constraint definitions, indexes and triggers, reviewed once against section 7.2.
    [Fact]
    public async Task SchemaContract_FullSchemaMatchesReviewedSnapshot()
    {
        var snapshot = new StringBuilder();
        Append(snapshot, "Columns", await QueryAsync("""
            SELECT table_name || '.' || column_name || ' ' ||
                   CASE WHEN data_type IN ('USER-DEFINED', 'ARRAY') THEN udt_name ELSE data_type END ||
                   COALESCE('(' || character_maximum_length || ')', '') ||
                   COALESCE('(' || numeric_precision || ',' || numeric_scale || ')', '') ||
                   CASE WHEN is_nullable = 'NO' THEN ' NOT NULL' ELSE '' END ||
                   COALESCE(' DEFAULT ' || column_default, '') ||
                   CASE WHEN is_generated = 'ALWAYS' THEN ' GENERATED' ELSE '' END ||
                   CASE WHEN is_identity = 'YES' THEN ' IDENTITY ' || identity_generation ELSE '' END
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name, column_name
            """));
        Append(snapshot, "Constraints", await QueryAsync("""
            SELECT conrelid::regclass || ' ' || conname || ' ' || pg_get_constraintdef(oid)
            FROM pg_constraint WHERE connamespace = 'public'::regnamespace AND conrelid::regclass::text <> '"__EFMigrationsHistory"'
            ORDER BY 1
            """));
        Append(snapshot, "Indexes", await QueryAsync("""
            SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory' ORDER BY 1
            """));
        Append(snapshot, "Triggers", await QueryAsync("""
            SELECT tgrelid::regclass || ' ' || tgname FROM pg_trigger WHERE NOT tgisinternal ORDER BY 1
            """));

        await Verify(snapshot.ToString());
    }

    [Theory]
    [InlineData("UPDATE seller_ledger_entries SET created_at = now()", "seller_ledger_entries")]
    [InlineData("DELETE FROM seller_ledger_entries", "seller_ledger_entries")]
    [InlineData("UPDATE audit_log SET action = 'changed'", "audit_log")]
    [InlineData("DELETE FROM audit_log", "audit_log")]
    public async Task AppendOnlyTrigger_BlocksUpdateAndDelete(string statement, string table)
    {
        await ExecuteAsync("""
            INSERT INTO users (id, display_name, created_at, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ('0197a000-0000-7000-8000-000000000001', 'Seller', now(), true, false, false, false, 0) ON CONFLICT DO NOTHING;
            INSERT INTO seller_profiles (id, user_id, display_name, country, created_at)
            VALUES ('0197a000-0000-7000-8000-000000000002', '0197a000-0000-7000-8000-000000000001', 'Studio', 'ES', now()) ON CONFLICT DO NOTHING;
            INSERT INTO seller_ledger_entries (id, seller_id, entry_type, amount, currency, created_at)
            VALUES (gen_random_uuid(), '0197a000-0000-7000-8000-000000000002', 'Payout', -1.00, 'EUR', now());
            INSERT INTO audit_log (occurred_at, actor_type, action) VALUES (now(), 'System', 'test.row');
            """);

        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(statement));

        exception.MessageText.ShouldBe($"{table} is append-only");
    }

    [Fact]
    public async Task ReferenceData_CategoriesAndRoles_AreSeeded()
    {
        var categories = await QueryAsync("SELECT slug FROM categories ORDER BY sort_order");
        var roles = await QueryAsync("SELECT name FROM roles ORDER BY name");

        categories.ShouldBe(["ebooks", "templates", "graphics", "audio", "video", "software", "fonts", "other"]);
        roles.ShouldBe(["Admin", "Seller"]);
    }

    [Fact]
    public async Task Migration_DownToZeroAndUpAgain_RemovesAndRestoresSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("0", cancellationToken: ct);
        var tablesAfterDown = await QueryAsync("SELECT tablename FROM pg_tables WHERE schemaname = 'public'");
        var functionsAfterDown = await QueryAsync("SELECT proname FROM pg_proc WHERE proname = 'forbid_modification'");
        await migrator.MigrateAsync(cancellationToken: ct);

        tablesAfterDown.ShouldBe(["__EFMigrationsHistory"]);
        functionsAfterDown.ShouldBeEmpty();
        (await QueryAsync("SELECT slug FROM categories")).Count.ShouldBe(8);
    }

    private static void Append(StringBuilder snapshot, string heading, IEnumerable<string> lines)
    {
        snapshot.Append("## ").AppendLine(heading);
        foreach (var line in lines)
        {
            snapshot.AppendLine(line);
        }

        snapshot.AppendLine();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
