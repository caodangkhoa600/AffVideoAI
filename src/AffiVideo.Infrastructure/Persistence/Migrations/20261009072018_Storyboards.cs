using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Storyboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Storyboards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreativeTemplate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    Planner = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RenderMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Storyboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Storyboards_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Storyboards_Variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "Variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryboardScenes",
                columns: table => new
                {
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StoryboardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Layout = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Technique = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    OnScreenText = table.Column<string[]>(type: "text[]", nullable: false),
                    NarrationText = table.Column<string>(type: "text", nullable: false),
                    AssetIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryboardScenes", x => new { x.StoryboardId, x.Position });
                    table.ForeignKey(
                        name: "FK_StoryboardScenes_Storyboards_StoryboardId",
                        column: x => x.StoryboardId,
                        principalTable: "Storyboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryboardSceneFacts",
                columns: table => new
                {
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StoryboardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenePosition = table.Column<int>(type: "integer", nullable: false),
                    FactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryboardSceneFacts", x => new { x.StoryboardId, x.ScenePosition, x.Position });
                    table.ForeignKey(
                        name: "FK_StoryboardSceneFacts_Facts_FactId",
                        column: x => x.FactId,
                        principalTable: "Facts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoryboardSceneFacts_StoryboardScenes_StoryboardId_ScenePos~",
                        columns: x => new { x.StoryboardId, x.ScenePosition },
                        principalTable: "StoryboardScenes",
                        principalColumns: new[] { "StoryboardId", "Position" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Storyboards_OrganizationId",
                table: "Storyboards",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Storyboards_VariantId_Version",
                table: "Storyboards",
                columns: new[] { "VariantId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryboardSceneFacts_FactId",
                table: "StoryboardSceneFacts",
                column: "FactId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryboardSceneFacts");

            migrationBuilder.DropTable(
                name: "StoryboardScenes");

            migrationBuilder.DropTable(
                name: "Storyboards");
        }
    }
}
