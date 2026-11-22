using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuperAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "users_role_check",
                schema: "identity",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "users_role_check",
                schema: "identity",
                table: "users",
                sql: "role IN ('User', 'Admin', 'SuperAdmin')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "users_role_check",
                schema: "identity",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "users_role_check",
                schema: "identity",
                table: "users",
                sql: "role IN ('User', 'Admin')");
        }
    }
}
