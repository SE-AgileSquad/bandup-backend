using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                schema: "writing",
                table: "writing_prompts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                schema: "content",
                table: "passages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "images",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_images", x => x.id);
                    table.CheckConstraint("images_size_positive", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_images_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_writing_prompts_photo_id",
                schema: "writing",
                table: "writing_prompts",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_passages_photo_id",
                schema: "content",
                table: "passages",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_images_created_at",
                schema: "media",
                table: "images",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_images_storage_key",
                schema: "media",
                table: "images",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_images_uploaded_by",
                schema: "media",
                table: "images",
                column: "uploaded_by");

            migrationBuilder.AddForeignKey(
                name: "fk_passages_images_photo_id",
                schema: "content",
                table: "passages",
                column: "photo_id",
                principalSchema: "media",
                principalTable: "images",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_writing_prompts_images_photo_id",
                schema: "writing",
                table: "writing_prompts",
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
                name: "fk_passages_images_photo_id",
                schema: "content",
                table: "passages");

            migrationBuilder.DropForeignKey(
                name: "fk_writing_prompts_images_photo_id",
                schema: "writing",
                table: "writing_prompts");

            migrationBuilder.DropTable(
                name: "images",
                schema: "media");

            migrationBuilder.DropIndex(
                name: "ix_writing_prompts_photo_id",
                schema: "writing",
                table: "writing_prompts");

            migrationBuilder.DropIndex(
                name: "ix_passages_photo_id",
                schema: "content",
                table: "passages");

            migrationBuilder.DropColumn(
                name: "photo_id",
                schema: "writing",
                table: "writing_prompts");

            migrationBuilder.DropColumn(
                name: "photo_id",
                schema: "content",
                table: "passages");
        }
    }
}
