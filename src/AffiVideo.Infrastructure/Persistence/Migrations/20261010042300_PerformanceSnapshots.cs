using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerformanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    TakenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: true),
                    Likes = table.Column<long>(type: "bigint", nullable: true),
                    Comments = table.Column<long>(type: "bigint", nullable: true),
                    Shares = table.Column<long>(type: "bigint", nullable: true),
                    Clicks = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceSnapshots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PerformanceSnapshots_PublishedPosts_PublishedPostId",
                        column: x => x.PublishedPostId,
                        principalTable: "PublishedPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSnapshots_OrganizationId",
                table: "PerformanceSnapshots",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSnapshots_PublishedPostId_TakenAt_RecordedAt",
                table: "PerformanceSnapshots",
                columns: new[] { "PublishedPostId", "TakenAt", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformanceSnapshots");
        }
    }
}
