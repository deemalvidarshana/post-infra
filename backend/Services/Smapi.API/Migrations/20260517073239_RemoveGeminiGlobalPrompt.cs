using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGeminiGlobalPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Prompt",
                table: "GeminiSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Prompt",
                table: "GeminiSettings",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");
        }
    }
}
