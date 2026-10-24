using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandUp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS citext;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS vector;");
        }
    }
}