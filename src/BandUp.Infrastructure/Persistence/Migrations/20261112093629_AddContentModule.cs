using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.CreateTable(
                name: "passages",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    source_note = table.Column<string>(type: "text", nullable: false),
                    usage_basis = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    previous_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passages", x => x.id);
                    table.CheckConstraint("passages_status_check", "status IN ('Draft', 'Published', 'Archived')");
                    table.ForeignKey(
                        name: "fk_passages_passages_previous_version_id",
                        column: x => x.previous_version_id,
                        principalSchema: "content",
                        principalTable: "passages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_passages_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    passage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    evidence_sentence = table.Column<string>(type: "text", nullable: true),
                    misleading_sentence = table.Column<string>(type: "text", nullable: true),
                    unspecified_part = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                    table.CheckConstraint("questions_status_check", "status IN ('Draft', 'Published')");
                    table.CheckConstraint("questions_type_check", "type IN ('TrueFalseNotGiven', 'SentenceCompletion')");
                    table.ForeignKey(
                        name: "fk_questions_passages_passage_id",
                        column: x => x.passage_id,
                        principalSchema: "content",
                        principalTable: "passages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "answer_keys",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correct_answer = table.Column<string>(type: "text", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_variants = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_answer_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_answer_keys_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "content",
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_answer_keys_users_verified_by",
                        column: x => x.verified_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_answer_keys_question_id",
                schema: "content",
                table: "answer_keys",
                column: "question_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_answer_keys_verified_by",
                schema: "content",
                table: "answer_keys",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "ix_passages_created_by",
                schema: "content",
                table: "passages",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_passages_previous_version_id",
                schema: "content",
                table: "passages",
                column: "previous_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_passage_id",
                schema: "content",
                table: "questions",
                column: "passage_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answer_keys",
                schema: "content");

            migrationBuilder.DropTable(
                name: "questions",
                schema: "content");

            migrationBuilder.DropTable(
                name: "passages",
                schema: "content");
        }
    }
}
