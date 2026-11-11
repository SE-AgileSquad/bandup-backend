using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignBillingWithSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledger_reason_reference",
                schema: "billing",
                table: "credit_ledger");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_reason_reference",
                schema: "billing",
                table: "credit_ledger",
                columns: new[] { "reason", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledger_reason_reference",
                schema: "billing",
                table: "credit_ledger");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_reason_reference",
                schema: "billing",
                table: "credit_ledger",
                columns: new[] { "reason", "reference_id" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL");
        }
    }
}
