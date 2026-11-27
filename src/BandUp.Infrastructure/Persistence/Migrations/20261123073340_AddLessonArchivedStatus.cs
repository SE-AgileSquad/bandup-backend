using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonArchivedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "lessons_status_check",
                schema: "learning",
                table: "lessons");

            migrationBuilder.AddCheckConstraint(
                name: "lessons_status_check",
                schema: "learning",
                table: "lessons",
                sql: "status IN ('Draft', 'Published', 'Archived')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "lessons_status_check",
                schema: "learning",
                table: "lessons");

            migrationBuilder.AddCheckConstraint(
                name: "lessons_status_check",
                schema: "learning",
                table: "lessons",
                sql: "status IN ('Draft', 'Published')");
        }
    }
}
