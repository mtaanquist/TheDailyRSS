using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTickers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tickers",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Price = table.Column<double>(type: "double precision", nullable: false),
                    PreviousClose = table.Column<double>(type: "double precision", nullable: false),
                    MarketTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickers", x => x.Symbol);
                });

            migrationBuilder.CreateTable(
                name: "UserTickers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Promoted = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTickers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTickers_Tickers_Symbol",
                        column: x => x.Symbol,
                        principalTable: "Tickers",
                        principalColumn: "Symbol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTickers_Symbol",
                table: "UserTickers",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_UserTickers_UserId_Promoted_SortOrder",
                table: "UserTickers",
                columns: new[] { "UserId", "Promoted", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTickers_UserId_Symbol",
                table: "UserTickers",
                columns: new[] { "UserId", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTickers");

            migrationBuilder.DropTable(
                name: "Tickers");
        }
    }
}
