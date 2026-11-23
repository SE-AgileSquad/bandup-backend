using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptApproxBandAndExpired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "attempts_status_check",
                schema: "exams",
                table: "attempts");

            migrationBuilder.AddColumn<decimal>(
                name: "approx_band",
                schema: "exams",
                table: "attempts",
                type: "numeric(2,1)",
                precision: 2,
                scale: 1,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "attempts_approx_band_check",
                schema: "exams",
                table: "attempts",
                sql: "approx_band IS NULL OR (approx_band >= 0 AND approx_band <= 9)");

            migrationBuilder.AddCheckConstraint(
                name: "attempts_status_check",
                schema: "exams",
                table: "attempts",
                sql: "status IN ('InProgress', 'Submitted', 'Expired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "attempts_approx_band_check",
                schema: "exams",
                table: "attempts");

            migrationBuilder.DropCheckConstraint(
                name: "attempts_status_check",
                schema: "exams",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "approx_band",
                schema: "exams",
                table: "attempts");

            migrationBuilder.AddCheckConstraint(
                name: "attempts_status_check",
                schema: "exams",
                table: "attempts",
                sql: "status IN ('InProgress', 'Submitted')");
        }
    }
}
