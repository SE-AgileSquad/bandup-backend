using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "learning");

            migrationBuilder.CreateTable(
                name: "lessons",
                schema: "learning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    question_type = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lessons", x => x.id);
                    table.CheckConstraint("lessons_question_type_check", "question_type IN ('TrueFalseNotGiven', 'SentenceCompletion')");
                    table.CheckConstraint("lessons_status_check", "status IN ('Draft', 'Published')");
                });

            migrationBuilder.CreateTable(
                name: "lesson_examples",
                schema: "learning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_sentence = table.Column<string>(type: "text", nullable: false),
                    paraphrase = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_examples", x => x.id);
                    table.ForeignKey(
                        name: "fk_lesson_examples_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalSchema: "learning",
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lesson_map",
                schema: "learning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_type = table.Column<string>(type: "text", nullable: false),
                    error_pattern = table.Column<string>(type: "text", nullable: true),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_map", x => x.id);
                    table.CheckConstraint("lesson_map_question_type_check", "question_type IN ('TrueFalseNotGiven', 'SentenceCompletion')");
                    table.ForeignKey(
                        name: "fk_lesson_map_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalSchema: "learning",
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lesson_progress",
                schema: "learning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_lesson_progress_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalSchema: "learning",
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lesson_progress_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_examples_lesson_id",
                schema: "learning",
                table: "lesson_examples",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_map_lesson_id",
                schema: "learning",
                table: "lesson_map",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_map_question_type_error_pattern",
                schema: "learning",
                table: "lesson_map",
                columns: new[] { "question_type", "error_pattern" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_lesson_progress_lesson_id",
                schema: "learning",
                table: "lesson_progress",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_progress_user_id_lesson_id",
                schema: "learning",
                table: "lesson_progress",
                columns: new[] { "user_id", "lesson_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lesson_examples",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "lesson_map",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "lesson_progress",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "lessons",
                schema: "learning");
        }
    }
}
