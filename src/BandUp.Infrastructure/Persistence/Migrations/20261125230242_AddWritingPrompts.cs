using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "writing");

            migrationBuilder.CreateTable(
                name: "writing_prompts",
                schema: "writing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    task_text = table.Column<string>(type: "text", nullable: false),
                    essay_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    min_words = table.Column<int>(type: "integer", nullable: false),
                    max_words = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_writing_prompts", x => x.id);
                    table.CheckConstraint("writing_prompts_status_check", "status IN ('Draft', 'Published', 'Archived')");
                    table.CheckConstraint("writing_prompts_word_range_check", "min_words > 0 AND max_words >= min_words");
                    table.ForeignKey(
                        name: "fk_writing_prompts_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_writing_prompts_created_by",
                schema: "writing",
                table: "writing_prompts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_writing_prompts_status_updated_at_id",
                schema: "writing",
                table: "writing_prompts",
                columns: new[] { "status", "updated_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "writing_prompts",
                schema: "writing");
        }
    }
}
