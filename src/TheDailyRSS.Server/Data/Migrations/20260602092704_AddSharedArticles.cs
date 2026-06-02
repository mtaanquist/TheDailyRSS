using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedArticles_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedArticles_ArticleId",
                table: "SharedArticles",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedArticles_CreatedByUserId_ArticleId",
                table: "SharedArticles",
                columns: new[] { "CreatedByUserId", "ArticleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedArticles");
        }
    }
}
