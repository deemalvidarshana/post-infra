using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookReelUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VideoUrl",
                table: "FacebookPostUrls",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FacebookReelUploadJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FacebookPostUrlId = table.Column<int>(type: "integer", nullable: true),
                    VideoSourceUrl = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    S3Bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    S3Region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    S3EndpointUrl = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    S3Key = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    GraphApiVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FacebookVideoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FacebookPostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetainUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookReelUploadJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacebookReelUploadJobs_FacebookPostUrls_FacebookPostUrlId",
                        column: x => x.FacebookPostUrlId,
                        principalTable: "FacebookPostUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookReelUploadJobs_FacebookPostUrlId",
                table: "FacebookReelUploadJobs",
                column: "FacebookPostUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookReelUploadJobs_Status",
                table: "FacebookReelUploadJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookReelUploadJobs_UserId_CreatedAt",
                table: "FacebookReelUploadJobs",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacebookReelUploadJobs");

            migrationBuilder.DropColumn(
                name: "VideoUrl",
                table: "FacebookPostUrls");
        }
    }
}
