using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingPersistenceFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "reserved_refund_capacity",
                schema: "billing",
                table: "credit_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "writing_submissions",
                schema: "writing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_title = table.Column<string>(type: "text", nullable: false),
                    prompt_text = table.Column<string>(type: "text", nullable: false),
                    essay_text = table.Column<string>(type: "text", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    queue_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publish_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_publish_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publish_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    publish_lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_writing_submissions", x => x.id);
                    table.CheckConstraint("writing_submission_deadline", "queue_deadline > submitted_at");
                    table.CheckConstraint("writing_submission_lease", "(publish_lease_id IS NULL) = (publish_lease_expires_at IS NULL)");
                    table.CheckConstraint("writing_submission_lifecycle", "(started_at IS NULL OR (started_at >= submitted_at AND started_at < queue_deadline))\nAND (completed_at IS NULL OR completed_at >= COALESCE(started_at, submitted_at))\nAND ((status = 'Queued' AND started_at IS NULL AND completed_at IS NULL)\n  OR (status = 'Processing' AND started_at IS NOT NULL AND completed_at IS NULL)\n  OR (status = 'Succeeded' AND started_at IS NOT NULL AND completed_at IS NOT NULL)\n  OR (status = 'Failed' AND completed_at IS NOT NULL AND (started_at IS NOT NULL OR completed_at >= queue_deadline)))");
                    table.CheckConstraint("writing_submission_publish_attempts", "publish_attempts >= 0");
                    table.ForeignKey(
                        name: "fk_writing_submissions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_writing_submissions_writing_prompts_prompt_id",
                        column: x => x.prompt_id,
                        principalSchema: "writing",
                        principalTable: "writing_prompts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "writing_feedbacks",
                schema: "writing",
                columns: table => new
                {
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<string>(type: "text", nullable: false),
                    estimated_overall_band = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_writing_feedbacks", x => x.submission_id);
                    table.CheckConstraint("writing_feedback_band", "estimated_overall_band BETWEEN 0 AND 9 AND mod(estimated_overall_band, 0.5) = 0");
                    table.CheckConstraint("writing_feedback_metadata", "(schema_version = payload->>'schemaVersion' AND payload->>'scoreLabel' = 'estimated'\n AND estimated_overall_band = (payload->>'estimatedOverallBand')::numeric) IS TRUE");
                    table.ForeignKey(
                        name: "fk_writing_feedbacks_writing_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "writing",
                        principalTable: "writing_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "credit_account_refund_headroom",
                schema: "billing",
                table: "credit_accounts",
                sql: "balance::bigint + reserved_refund_capacity <= 2147483647");

            migrationBuilder.AddCheckConstraint(
                name: "reserved_refund_capacity_non_negative",
                schema: "billing",
                table: "credit_accounts",
                sql: "reserved_refund_capacity >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_writing_submissions_prompt_id",
                schema: "writing",
                table: "writing_submissions",
                column: "prompt_id");

            migrationBuilder.CreateIndex(
                name: "ix_writing_submissions_user_id_idempotency_key",
                schema: "writing",
                table: "writing_submissions",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_writing_submissions_user_id_submitted_at_id",
                schema: "writing",
                table: "writing_submissions",
                columns: new[] { "user_id", "submitted_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "writing_feedbacks",
                schema: "writing");

            migrationBuilder.DropTable(
                name: "writing_submissions",
                schema: "writing");

            migrationBuilder.DropCheckConstraint(
                name: "credit_account_refund_headroom",
                schema: "billing",
                table: "credit_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "reserved_refund_capacity_non_negative",
                schema: "billing",
                table: "credit_accounts");

            migrationBuilder.DropColumn(
                name: "reserved_refund_capacity",
                schema: "billing",
                table: "credit_accounts");
        }
    }
}
