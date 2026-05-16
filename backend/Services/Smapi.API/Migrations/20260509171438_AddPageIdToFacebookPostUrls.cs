using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPageIdToFacebookPostUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PermalinkUrl",
                table: "FacebookPostUrls");

            migrationBuilder.AddColumn<string>(
                name: "PageId",
                table: "FacebookPostUrls",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_UserId_PageId",
                table: "FacebookPostUrls",
                columns: new[] { "UserId", "PageId" });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_PermalinkUrl",
                table: "FacebookPostUrls",
                columns: new[] { "UserId", "PageId", "PermalinkUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PageId",
                table: "FacebookPostUrls");

            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_PermalinkUrl",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "PageId",
                table: "FacebookPostUrls");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_UserId_PermalinkUrl",
                table: "FacebookPostUrls",
                columns: new[] { "UserId", "PermalinkUrl" },
                unique: true);
        }
    }
}
