using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class ScopeFacebookPagesByUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacebookPages_PageId",
                table: "FacebookPages");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPages_UserId_PageId",
                table: "FacebookPages",
                columns: new[] { "UserId", "PageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacebookPages_UserId_PageId",
                table: "FacebookPages");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPages_PageId",
                table: "FacebookPages",
                column: "PageId",
                unique: true);
        }
    }
}
