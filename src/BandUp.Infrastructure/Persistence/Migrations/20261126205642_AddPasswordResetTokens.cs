using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.CheckConstraint("password_reset_token_consumption_check", "consumed_at IS NULL OR (consumed_at >= created_at AND consumed_at < expires_at)");
                    table.CheckConstraint("password_reset_token_hash_check", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("password_reset_token_invalidation_check", "invalidated_at IS NULL OR invalidated_at >= created_at");
                    table.CheckConstraint("password_reset_token_terminal_check", "consumed_at IS NULL OR invalidated_at IS NULL");
                    table.CheckConstraint("password_reset_token_times_check", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_password_reset_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalSchema: "identity",
                        principalTable: "password_reset_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_challenge_id",
                schema: "identity",
                table: "password_reset_tokens",
                column: "challenge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                schema: "identity",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_password_reset_token_open_user",
                schema: "identity",
                table: "password_reset_tokens",
                column: "user_id",
                unique: true,
                filter: "consumed_at IS NULL AND invalidated_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_tokens",
                schema: "identity");
        }
    }
}
