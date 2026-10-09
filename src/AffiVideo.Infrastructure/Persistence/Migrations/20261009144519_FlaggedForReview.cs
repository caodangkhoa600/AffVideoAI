using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlaggedForReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearedFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryboardId = table.Column<Guid>(type: "uuid", nullable: true),
                    RenderedVideoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClearedByMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClearedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearedFlags", x => x.Id);
                    table.CheckConstraint("CK_ClearedFlags_OneSubject", "(\"StoryboardId\" IS NULL) <> (\"RenderedVideoId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ClearedFlags_Facts_FactId",
                        column: x => x.FactId,
                        principalTable: "Facts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearedFlags_Members_ClearedByMemberId",
                        column: x => x.ClearedByMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearedFlags_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearedFlags_RenderedVideos_RenderedVideoId",
                        column: x => x.RenderedVideoId,
                        principalTable: "RenderedVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClearedFlags_Storyboards_StoryboardId",
                        column: x => x.StoryboardId,
                        principalTable: "Storyboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClearedFlags_ClearedByMemberId",
                table: "ClearedFlags",
                column: "ClearedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearedFlags_FactId",
                table: "ClearedFlags",
                column: "FactId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearedFlags_OrganizationId",
                table: "ClearedFlags",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearedFlags_RenderedVideoId_FactId",
                table: "ClearedFlags",
                columns: new[] { "RenderedVideoId", "FactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearedFlags_StoryboardId_FactId",
                table: "ClearedFlags",
                columns: new[] { "StoryboardId", "FactId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearedFlags");
        }
    }
}
