using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiErrorLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiErrorLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    User = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiErrorLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiErrorLogs_OccurredAt",
                table: "AiErrorLogs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiErrorLogs");
        }
    }
}
