using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDailyRSS.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHiddenArticleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "UserArticleStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_UserArticleStates_UserId_IsHidden",
                table: "UserArticleStates",
                columns: new[] { "UserId", "IsHidden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserArticleStates_UserId_IsHidden",
                table: "UserArticleStates");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "UserArticleStates");
        }
    }
}
