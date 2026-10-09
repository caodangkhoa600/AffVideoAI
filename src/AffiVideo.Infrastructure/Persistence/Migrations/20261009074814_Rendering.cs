using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rendering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenderJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryboardId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RenderedVideoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenderJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenderJobs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenderJobs_Storyboards_StoryboardId",
                        column: x => x.StoryboardId,
                        principalTable: "Storyboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RenderedVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryboardId = table.Column<Guid>(type: "uuid", nullable: false),
                    RenderJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    UncutAssetIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenderedVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenderedVideos_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenderedVideos_RenderJobs_RenderJobId",
                        column: x => x.RenderJobId,
                        principalTable: "RenderJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenderedVideos_Storyboards_StoryboardId",
                        column: x => x.StoryboardId,
                        principalTable: "Storyboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenderedVideos_OrganizationId",
                table: "RenderedVideos",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RenderedVideos_RenderJobId",
                table: "RenderedVideos",
                column: "RenderJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenderedVideos_StoryboardId_CreatedAt",
                table: "RenderedVideos",
                columns: new[] { "StoryboardId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RenderJobs_OrganizationId",
                table: "RenderJobs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RenderJobs_State_CreatedAt",
                table: "RenderJobs",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RenderJobs_StoryboardId_CreatedAt",
                table: "RenderJobs",
                columns: new[] { "StoryboardId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RenderedVideos");

            migrationBuilder.DropTable(
                name: "RenderJobs");
        }
    }
}
