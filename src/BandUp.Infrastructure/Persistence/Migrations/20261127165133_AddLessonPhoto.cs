using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                schema: "learning",
                table: "lessons",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lessons_photo_id",
                schema: "learning",
                table: "lessons",
                column: "photo_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lessons_images_photo_id",
                schema: "learning",
                table: "lessons",
                column: "photo_id",
                principalSchema: "media",
                principalTable: "images",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lessons_images_photo_id",
                schema: "learning",
                table: "lessons");

            migrationBuilder.DropIndex(
                name: "ix_lessons_photo_id",
                schema: "learning",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "photo_id",
                schema: "learning",
                table: "lessons");
        }
    }
}
