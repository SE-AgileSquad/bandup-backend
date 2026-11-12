using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExamsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "exams");

            migrationBuilder.CreateTable(
                name: "attempts",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    raw_score = table.Column<int>(type: "integer", nullable: true),
                    total_questions = table.Column<int>(type: "integer", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                    table.CheckConstraint("attempts_status_check", "status IN ('InProgress', 'Submitted')");
                    table.ForeignKey(
                        name: "fk_attempts_passages_passage_id",
                        column: x => x.passage_id,
                        principalSchema: "content",
                        principalTable: "passages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attempts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempt_answers",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_answer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_attempt_answers_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "exams",
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_answers_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "content",
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempt_answers_question_id",
                schema: "exams",
                table: "attempt_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ux_attempt_answers_attempt_question",
                schema: "exams",
                table: "attempt_answers",
                columns: new[] { "attempt_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attempts_passage_id",
                schema: "exams",
                table: "attempts",
                column: "passage_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_user_started_at",
                schema: "exams",
                table: "attempts",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_attempts_user_idempotency_key",
                schema: "exams",
                table: "attempts",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_attempts_user_passage_in_progress",
                schema: "exams",
                table: "attempts",
                columns: new[] { "user_id", "passage_id" },
                unique: true,
                filter: "status = 'InProgress'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempt_answers",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "attempts",
                schema: "exams");
        }
    }
}
