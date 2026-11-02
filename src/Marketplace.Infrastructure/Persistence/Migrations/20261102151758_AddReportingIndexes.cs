using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0003: covering indexes for the report queries, measured on the 1M-order dataset (docs/sql/README.md).
    /// The two ledger indexes INCLUDE the amount column of a complex property, which EF cannot express, so they are
    /// created here in SQL and kept out of the model.
    /// </summary>
    public partial class AddReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_seller_id_created_at",
                table: "seller_ledger_entries");

            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at",
                table: "orders",
                column: "created_at")
                .Annotation("Npgsql:IndexInclude", new[] { "id", "status", "paid_at" });

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_ledger_seller_id_created_at
                  ON seller_ledger_entries (seller_id, created_at) INCLUDE (entry_type, amount, order_item_id);
                CREATE INDEX IF NOT EXISTS ix_ledger_created_at
                  ON seller_ledger_entries (created_at) INCLUDE (seller_id, entry_type, amount);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_ledger_created_at;
                DROP INDEX IF EXISTS ix_ledger_seller_id_created_at;
                """);

            migrationBuilder.DropIndex(
                name: "ix_orders_created_at",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_seller_id_created_at",
                table: "seller_ledger_entries",
                columns: new[] { "seller_id", "created_at" });
        }
    }
}
