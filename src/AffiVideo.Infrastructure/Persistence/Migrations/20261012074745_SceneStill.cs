using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SceneStill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StillId",
                table: "StoryboardScenes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryboardScenes_StillId",
                table: "StoryboardScenes",
                column: "StillId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoryboardScenes_Stills_StillId",
                table: "StoryboardScenes",
                column: "StillId",
                principalTable: "Stills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoryboardScenes_Stills_StillId",
                table: "StoryboardScenes");

            migrationBuilder.DropIndex(
                name: "IX_StoryboardScenes_StillId",
                table: "StoryboardScenes");

            migrationBuilder.DropColumn(
                name: "StillId",
                table: "StoryboardScenes");
        }
    }
}
