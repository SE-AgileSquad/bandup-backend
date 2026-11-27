using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpLoginChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_login_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_mac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    max_failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_login_challenges", x => x.id);
                    table.CheckConstraint("otp_login_attempts_check", "max_failed_attempts BETWEEN 1 AND 100 AND failed_attempts BETWEEN 0 AND max_failed_attempts");
                    table.CheckConstraint("otp_login_consumption_check", "consumed_at IS NULL OR (consumed_at >= created_at AND consumed_at < expires_at AND failed_attempts < max_failed_attempts)");
                    table.CheckConstraint("otp_login_invalidation_check", "invalidated_at IS NULL OR invalidated_at >= created_at");
                    table.CheckConstraint("otp_login_mac_check", "code_mac ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("otp_login_terminal_state_check", "consumed_at IS NULL OR invalidated_at IS NULL");
                    table.CheckConstraint("otp_login_times_check", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_otp_login_challenges_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_otp_login_user_created",
                schema: "identity",
                table: "otp_login_challenges",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_otp_login_open_user",
                schema: "identity",
                table: "otp_login_challenges",
                column: "user_id",
                unique: true,
                filter: "consumed_at IS NULL AND invalidated_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_login_challenges",
                schema: "identity");
        }
    }
}
