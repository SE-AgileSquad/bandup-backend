using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVocabularyModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vocabulary");

            migrationBuilder.CreateTable(
                name: "paraphrase_pairs",
                schema: "vocabulary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_sentence = table.Column<string>(type: "text", nullable: false),
                    equivalent_sentence = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paraphrase_pairs", x => x.id);
                    table.CheckConstraint("paraphrase_pairs_status_check", "status IN ('Draft', 'Approved')");
                    table.ForeignKey(
                        name: "fk_paraphrase_pairs_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "content",
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_paraphrase_pairs_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vocab_cards",
                schema: "vocabulary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    word = table.Column<string>(type: "text", nullable: false),
                    context_sentence = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vocab_cards", x => x.id);
                    table.UniqueConstraint("ak_vocab_cards_user_id_id", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_vocab_cards_questions_source_question_id",
                        column: x => x.source_question_id,
                        principalSchema: "content",
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vocab_cards_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_items",
                schema: "vocabulary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "text", nullable: false),
                    vocab_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paraphrase_pair_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leitner_box = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    streak_correct = table.Column<int>(type: "integer", nullable: false),
                    last_reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_items", x => x.id);
                    table.CheckConstraint("review_items_leitner_box_check", "leitner_box BETWEEN 1 AND 5");
                    table.CheckConstraint("review_items_streak_correct_check", "streak_correct >= 0");
                    table.CheckConstraint("review_items_target_check", "(item_type = 'Vocab'\n    AND vocab_card_id IS NOT NULL AND paraphrase_pair_id IS NULL)\nOR\n(item_type = 'Paraphrase'\n    AND paraphrase_pair_id IS NOT NULL AND vocab_card_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_review_items_paraphrase_pairs_paraphrase_pair_id",
                        column: x => x.paraphrase_pair_id,
                        principalSchema: "vocabulary",
                        principalTable: "paraphrase_pairs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_items_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_items_vocab_cards_user_id_vocab_card_id",
                        columns: x => new { x.user_id, x.vocab_card_id },
                        principalSchema: "vocabulary",
                        principalTable: "vocab_cards",
                        principalColumns: new[] { "user_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paraphrase_pairs_created_by",
                schema: "vocabulary",
                table: "paraphrase_pairs",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_paraphrase_pairs_question_id",
                schema: "vocabulary",
                table: "paraphrase_pairs",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_items_paraphrase_pair_id",
                schema: "vocabulary",
                table: "review_items",
                column: "paraphrase_pair_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_items_user_id_due_date",
                schema: "vocabulary",
                table: "review_items",
                columns: new[] { "user_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_review_items_user_id_paraphrase_pair_id",
                schema: "vocabulary",
                table: "review_items",
                columns: new[] { "user_id", "paraphrase_pair_id" },
                unique: true,
                filter: "paraphrase_pair_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_review_items_user_id_vocab_card_id",
                schema: "vocabulary",
                table: "review_items",
                columns: new[] { "user_id", "vocab_card_id" },
                unique: true,
                filter: "vocab_card_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vocab_cards_source_question_id",
                schema: "vocabulary",
                table: "vocab_cards",
                column: "source_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_vocab_cards_user_id_word",
                schema: "vocabulary",
                table: "vocab_cards",
                columns: new[] { "user_id", "word" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_items",
                schema: "vocabulary");

            migrationBuilder.DropTable(
                name: "paraphrase_pairs",
                schema: "vocabulary");

            migrationBuilder.DropTable(
                name: "vocab_cards",
                schema: "vocabulary");
        }
    }
}
