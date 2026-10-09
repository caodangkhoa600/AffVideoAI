using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoryboardEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManuallyEdited",
                table: "StoryboardScenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int[]>(
                name: "DrawnScenePositions",
                table: "RenderedVideos",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // Every Rendered Video made before clips were kept had all of its Scenes drawn.
            migrationBuilder.Sql(
                """
                UPDATE "RenderedVideos" AS video SET "DrawnScenePositions" = ARRAY(
                    SELECT scene."Position" FROM "StoryboardScenes" AS scene
                    WHERE scene."StoryboardId" = video."StoryboardId" ORDER BY scene."Position")
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManuallyEdited",
                table: "StoryboardScenes");

            migrationBuilder.DropColumn(
                name: "DrawnScenePositions",
                table: "RenderedVideos");
        }
    }
}
