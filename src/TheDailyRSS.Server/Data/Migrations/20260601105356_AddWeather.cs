using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeather : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowWeather",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "WeatherLatitude",
                table: "AspNetUsers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeatherLocationName",
                table: "AspNetUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeatherLongitude",
                table: "AspNetUsers",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WeatherSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EditionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentTempC = table.Column<double>(type: "double precision", nullable: false),
                    CurrentCode = table.Column<int>(type: "integer", nullable: false),
                    HighTempC = table.Column<double>(type: "double precision", nullable: false),
                    LowTempC = table.Column<double>(type: "double precision", nullable: false),
                    HourlyJson = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherSnapshots_LocationKey_EditionDate",
                table: "WeatherSnapshots",
                columns: new[] { "LocationKey", "EditionDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "ShowWeather",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WeatherLatitude",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WeatherLocationName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WeatherLongitude",
                table: "AspNetUsers");
        }
    }
}
