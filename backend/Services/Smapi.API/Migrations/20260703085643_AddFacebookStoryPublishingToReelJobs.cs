using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookStoryPublishingToReelJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacebookStoryId",
                table: "FacebookReelUploadJobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PublishAsStory",
                table: "FacebookReelUploadJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StoryErrorMessage",
                table: "FacebookReelUploadJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StoryPublishedAt",
                table: "FacebookReelUploadJobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacebookStoryId",
                table: "FacebookReelUploadJobs");

            migrationBuilder.DropColumn(
                name: "PublishAsStory",
                table: "FacebookReelUploadJobs");

            migrationBuilder.DropColumn(
                name: "StoryErrorMessage",
                table: "FacebookReelUploadJobs");

            migrationBuilder.DropColumn(
                name: "StoryPublishedAt",
                table: "FacebookReelUploadJobs");
        }
    }
}
