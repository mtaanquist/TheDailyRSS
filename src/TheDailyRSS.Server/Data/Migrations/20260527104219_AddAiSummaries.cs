using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiApiKeyEncrypted",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiAutoDaily",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiAutoWeekly",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiBaseUrl",
                table: "AspNetUsers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "AspNetUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiSystemPrompt",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArticleCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSummaries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSummaries_UserId_Kind_PeriodStart_PeriodEnd",
                table: "AiSummaries",
                columns: new[] { "UserId", "Kind", "PeriodStart", "PeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSummaries");

            migrationBuilder.DropColumn(
                name: "AiApiKeyEncrypted",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AiAutoDaily",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AiAutoWeekly",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AiBaseUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AiEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AiModel",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AiSystemPrompt",
                table: "AspNetUsers");
        }
    }
}
