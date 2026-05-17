using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTikTokScrapeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PageId",
                table: "FacebookPostUrls");

            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_PermalinkUrl",
                table: "FacebookPostUrls");

            migrationBuilder.AddColumn<string>(
                name: "AuthorName",
                table: "FacebookPostUrls",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CommentCount",
                table: "FacebookPostUrls",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "FacebookPostUrls",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LikeCount",
                table: "FacebookPostUrls",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicAuthor",
                table: "FacebookPostUrls",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicName",
                table: "FacebookPostUrls",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "FacebookPostUrls",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Facebook");

            migrationBuilder.AddColumn<long>(
                name: "PlayCount",
                table: "FacebookPostUrls",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ShareCount",
                table: "FacebookPostUrls",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_Platform",
                table: "FacebookPostUrls",
                columns: new[] { "UserId", "PageId", "Platform" });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_Platform_PermalinkUrl",
                table: "FacebookPostUrls",
                columns: new[] { "UserId", "PageId", "Platform", "PermalinkUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_Platform",
                table: "FacebookPostUrls");

            migrationBuilder.DropIndex(
                name: "IX_FacebookPostUrls_UserId_PageId_Platform_PermalinkUrl",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "AuthorName",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "LikeCount",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "MusicAuthor",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "MusicName",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "ShareCount",
                table: "FacebookPostUrls");

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
    }
}
