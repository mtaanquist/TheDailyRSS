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
    [Migration("20260529110000_KeywordFiltersPerSource")]
    public partial class KeywordFiltersPerSource : Migration
    {
        // Adds the optional feed-scope SourceId to KeywordFilter, mirroring FieldFilter.
        // BuildTargetModel is intentionally omitted; the runtime model snapshot covers
        // the post-migration state.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the old (UserId, Term) uniqueness with one that includes the new column,
            // so the same term can coexist as a global rule and one or more per-feed rules.
            migrationBuilder.DropIndex(
                name: "IX_KeywordFilters_UserId_Term",
                table: "KeywordFilters");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "KeywordFilters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeywordFilters_SourceId",
                table: "KeywordFilters",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_KeywordFilters_UserId_Term_SourceId",
                table: "KeywordFilters",
                columns: new[] { "UserId", "Term", "SourceId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_KeywordFilters_FeedSources_SourceId",
                table: "KeywordFilters",
                column: "SourceId",
                principalTable: "FeedSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KeywordFilters_FeedSources_SourceId",
                table: "KeywordFilters");

            migrationBuilder.DropIndex(
                name: "IX_KeywordFilters_UserId_Term_SourceId",
                table: "KeywordFilters");

            migrationBuilder.DropIndex(
                name: "IX_KeywordFilters_SourceId",
                table: "KeywordFilters");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "KeywordFilters");

            migrationBuilder.CreateIndex(
                name: "IX_KeywordFilters_UserId_Term",
                table: "KeywordFilters",
                columns: new[] { "UserId", "Term" },
                unique: true);
        }
    }
}
