using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookPostUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FacebookPostUrls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PermalinkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourcePageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PostCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    ScrapedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookPostUrls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_PostId",
                table: "FacebookPostUrls",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPostUrls_UserId_PermalinkUrl",
                table: "FacebookPostUrls",
                columns: new[] { "UserId", "PermalinkUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacebookPostUrls");
        }
    }
}
