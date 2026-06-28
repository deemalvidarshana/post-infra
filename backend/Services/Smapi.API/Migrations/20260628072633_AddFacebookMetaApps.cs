using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookMetaApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FacebookMetaAppId",
                table: "FacebookPages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FacebookMetaApps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AppId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AppSecret = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    VerifyToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WebhookKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GraphApiVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookMetaApps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPages_FacebookMetaAppId",
                table: "FacebookPages",
                column: "FacebookMetaAppId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookMetaApps_UserId_Name",
                table: "FacebookMetaApps",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookMetaApps_WebhookKey",
                table: "FacebookMetaApps",
                column: "WebhookKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FacebookPages_FacebookMetaApps_FacebookMetaAppId",
                table: "FacebookPages",
                column: "FacebookMetaAppId",
                principalTable: "FacebookMetaApps",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FacebookPages_FacebookMetaApps_FacebookMetaAppId",
                table: "FacebookPages");

            migrationBuilder.DropTable(
                name: "FacebookMetaApps");

            migrationBuilder.DropIndex(
                name: "IX_FacebookPages_FacebookMetaAppId",
                table: "FacebookPages");

            migrationBuilder.DropColumn(
                name: "FacebookMetaAppId",
                table: "FacebookPages");
        }
    }
}
