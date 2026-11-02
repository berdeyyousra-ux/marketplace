using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// R-10 (section 13.2): monthly net sales and fees per seller, refreshed every 15 minutes with
    /// REFRESH MATERIALIZED VIEW CONCURRENTLY, which needs the unique index. report_refreshes records when each
    /// refresh started, so EP-71 can read later months live from the ledger. Both are outside the EF model.
    /// </summary>
    public partial class AddSellerMonthlyRevenueView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW IF NOT EXISTS mv_seller_monthly_revenue AS
                SELECT seller_id,
                       date_trunc('month', created_at AT TIME ZONE 'UTC')                                  AS month,
                       COALESCE(SUM(amount) FILTER (WHERE entry_type IN ('Sale', 'Refund')), 0)           AS net_sales,
                       COALESCE(SUM(amount) FILTER (WHERE entry_type IN ('PlatformFee', 'FeeRefund')), 0) AS fees
                FROM seller_ledger_entries
                WHERE entry_type <> 'Payout'
                GROUP BY seller_id, 2
                WITH DATA;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_mv_seller_monthly_revenue_seller_id_month
                  ON mv_seller_monthly_revenue (seller_id, month);

                CREATE TABLE IF NOT EXISTS report_refreshes (
                  view_name text NOT NULL,
                  refreshed_at timestamptz NOT NULL,
                  CONSTRAINT pk_report_refreshes PRIMARY KEY (view_name)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS report_refreshes;
                DROP MATERIALIZED VIEW IF EXISTS mv_seller_monthly_revenue;
                """);
        }
    }
}
