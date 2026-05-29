using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheDailyRSS.Server.Data;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260529100000_AddFieldFilters")]
    public partial class AddFieldFilters : Migration
    {
        // BuildTargetModel is intentionally omitted; the runtime model snapshot covers the
        // post-migration state. EF tooling (dotnet ef) will regenerate a Designer file the
        // next time `migrations add` is run.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New JSONB column on Articles holding the structured field map captured from feed XML.
            migrationBuilder.AddColumn<string>(
                name: "Fields",
                table: "Articles",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            // Per-user mute-by-field rules.
            migrationBuilder.CreateTable(
                name: "FieldFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldFilters_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FieldFilters_FeedSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "FeedSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldFilters_SourceId",
                table: "FieldFilters",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldFilters_UserId_FieldKey_Operator_Value_SourceId",
                table: "FieldFilters",
                columns: new[] { "UserId", "FieldKey", "Operator", "Value", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FieldFilters");
            migrationBuilder.DropColumn(name: "Fields", table: "Articles");
        }
    }
}
