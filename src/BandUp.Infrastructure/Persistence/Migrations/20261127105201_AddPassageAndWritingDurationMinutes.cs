using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPassageAndWritingDurationMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                schema: "writing",
                table: "writing_prompts",
                type: "integer",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                schema: "content",
                table: "passages",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddCheckConstraint(
                name: "writing_prompts_duration_positive",
                schema: "writing",
                table: "writing_prompts",
                sql: "duration_minutes > 0");

            migrationBuilder.AddCheckConstraint(
                name: "passages_duration_positive",
                schema: "content",
                table: "passages",
                sql: "duration_minutes > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "writing_prompts_duration_positive",
                schema: "writing",
                table: "writing_prompts");

            migrationBuilder.DropCheckConstraint(
                name: "passages_duration_positive",
                schema: "content",
                table: "passages");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                schema: "writing",
                table: "writing_prompts");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                schema: "content",
                table: "passages");
        }
    }
}
