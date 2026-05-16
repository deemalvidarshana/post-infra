using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddS3SettingsAndPostUploadStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "S3Bucket",
                table: "FacebookPostUrls",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3Key",
                table: "FacebookPostUrls",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3Region",
                table: "FacebookPostUrls",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3UploadError",
                table: "FacebookPostUrls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3UploadStatus",
                table: "FacebookPostUrls",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotUploaded");

            migrationBuilder.AddColumn<DateTime>(
                name: "S3UploadedAt",
                table: "FacebookPostUrls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "S3StorageSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndpointUrl = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    AccessKeyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SecretAccessKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    SessionToken = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_S3StorageSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_S3StorageSettings_UserId",
                table: "S3StorageSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "S3StorageSettings");

            migrationBuilder.DropColumn(
                name: "S3Bucket",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "S3Key",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "S3Region",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "S3UploadError",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "S3UploadStatus",
                table: "FacebookPostUrls");

            migrationBuilder.DropColumn(
                name: "S3UploadedAt",
                table: "FacebookPostUrls");
        }
    }
}
