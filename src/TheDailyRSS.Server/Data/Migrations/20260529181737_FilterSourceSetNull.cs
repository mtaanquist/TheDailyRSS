using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FilterSourceSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldFilters_FeedSources_SourceId",
                table: "FieldFilters");

            migrationBuilder.DropForeignKey(
                name: "FK_KeywordFilters_FeedSources_SourceId",
                table: "KeywordFilters");

            migrationBuilder.AddForeignKey(
                name: "FK_FieldFilters_FeedSources_SourceId",
                table: "FieldFilters",
                column: "SourceId",
                principalTable: "FeedSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_KeywordFilters_FeedSources_SourceId",
                table: "KeywordFilters",
                column: "SourceId",
                principalTable: "FeedSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldFilters_FeedSources_SourceId",
                table: "FieldFilters");

            migrationBuilder.DropForeignKey(
                name: "FK_KeywordFilters_FeedSources_SourceId",
                table: "KeywordFilters");

            migrationBuilder.AddForeignKey(
                name: "FK_FieldFilters_FeedSources_SourceId",
                table: "FieldFilters",
                column: "SourceId",
                principalTable: "FeedSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_KeywordFilters_FeedSources_SourceId",
                table: "KeywordFilters",
                column: "SourceId",
                principalTable: "FeedSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
