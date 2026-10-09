using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApproveAndLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenderedVideos_OrganizationId",
                table: "RenderedVideos");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "RenderedVideos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByMemberId",
                table: "RenderedVideos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenderedVideos_ApprovedByMemberId",
                table: "RenderedVideos",
                column: "ApprovedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RenderedVideos_OrganizationId_CreatedAt",
                table: "RenderedVideos",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_RenderedVideos_Members_ApprovedByMemberId",
                table: "RenderedVideos",
                column: "ApprovedByMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RenderedVideos_Members_ApprovedByMemberId",
                table: "RenderedVideos");

            migrationBuilder.DropIndex(
                name: "IX_RenderedVideos_ApprovedByMemberId",
                table: "RenderedVideos");

            migrationBuilder.DropIndex(
                name: "IX_RenderedVideos_OrganizationId_CreatedAt",
                table: "RenderedVideos");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "RenderedVideos");

            migrationBuilder.DropColumn(
                name: "ApprovedByMemberId",
                table: "RenderedVideos");

            migrationBuilder.CreateIndex(
                name: "IX_RenderedVideos_OrganizationId",
                table: "RenderedVideos",
                column: "OrganizationId");
        }
    }
}
