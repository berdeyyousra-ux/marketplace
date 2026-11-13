using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowCancelledRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "ux_refund_requests_order_item_id",
                table: "refund_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_requests_status",
                table: "refund_requests");

            migrationBuilder.CreateIndex(
                name: "ux_refund_requests_live_order_item",
                table: "refund_requests",
                column: "order_item_id",
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_requests_status",
                table: "refund_requests",
                sql: "status IN ('Submitted','Approved','Rejected','Executing','Completed','Failed','Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_refund_requests_live_order_item",
                table: "refund_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_requests_status",
                table: "refund_requests");

            migrationBuilder.AddUniqueConstraint(
                name: "ux_refund_requests_order_item_id",
                table: "refund_requests",
                column: "order_item_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_requests_status",
                table: "refund_requests",
                sql: "status IN ('Submitted','Approved','Rejected','Executing','Completed','Failed')");
        }
    }
}
