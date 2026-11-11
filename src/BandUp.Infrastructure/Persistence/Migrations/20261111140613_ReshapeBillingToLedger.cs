using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeBillingToLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_transactions",
                schema: "billing");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "billing",
                table: "credit_accounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "balance",
                schema: "billing",
                table: "credit_accounts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.CreateTable(
                name: "credit_ledger",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_ledger", x => x.id);
                    table.CheckConstraint("delta_non_zero", "delta <> 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "balance_non_negative",
                schema: "billing",
                table: "credit_accounts",
                sql: "balance >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_credit_ledger_user_id",
                schema: "billing",
                table: "credit_ledger",
                column: "user_id");

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
            migrationBuilder.DropTable(
                name: "credit_ledger",
                schema: "billing");

            migrationBuilder.DropCheckConstraint(
                name: "balance_non_negative",
                schema: "billing",
                table: "credit_accounts");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "billing",
                table: "credit_accounts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "balance",
                schema: "billing",
                table: "credit_accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "credit_transactions",
                schema: "billing",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                });
        }
    }
}
