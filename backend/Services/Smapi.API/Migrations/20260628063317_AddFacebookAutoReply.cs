using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Smapi.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookAutoReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FacebookAutoReplySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Tone = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Language = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxRepliesPerPostPerDay = table.Column<int>(type: "integer", nullable: false),
                    IgnoreKeywords = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    EscalationKeywords = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    GraphApiVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookAutoReplySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacebookCommentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CommentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentCommentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CommentText = table.Column<string>(type: "text", nullable: true),
                    CommentAuthorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CommentAuthorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Verb = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GeneratedReply = table.Column<string>(type: "text", nullable: true),
                    ReplyCommentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SkipReason = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookCommentEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookAutoReplySettings_UserId_PageId",
                table: "FacebookAutoReplySettings",
                columns: new[] { "UserId", "PageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookCommentEvents_PageId_CommentId",
                table: "FacebookCommentEvents",
                columns: new[] { "PageId", "CommentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookCommentEvents_Status",
                table: "FacebookCommentEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookCommentEvents_UserId_PageId_ReceivedAt",
                table: "FacebookCommentEvents",
                columns: new[] { "UserId", "PageId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacebookAutoReplySettings");

            migrationBuilder.DropTable(
                name: "FacebookCommentEvents");
        }
    }
}
