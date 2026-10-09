using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UploadedAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MusicAudioId",
                table: "RenderedVideos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MusicVolumePercent",
                table: "RenderedVideos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NarrationAudioId",
                table: "RenderedVideos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VariantAudio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    SampleRate = table.Column<int>(type: "integer", nullable: false),
                    Channels = table.Column<int>(type: "integer", nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    VolumePercent = table.Column<int>(type: "integer", nullable: false),
                    RightsConfirmedByMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightsConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantAudio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariantAudio_Members_RightsConfirmedByMemberId",
                        column: x => x.RightsConfirmedByMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VariantAudio_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VariantAudio_Variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "Variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VariantAudio_OrganizationId",
                table: "VariantAudio",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantAudio_RightsConfirmedByMemberId",
                table: "VariantAudio",
                column: "RightsConfirmedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantAudio_VariantId_Kind",
                table: "VariantAudio",
                columns: new[] { "VariantId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VariantAudio");

            migrationBuilder.DropColumn(
                name: "MusicAudioId",
                table: "RenderedVideos");

            migrationBuilder.DropColumn(
                name: "MusicVolumePercent",
                table: "RenderedVideos");

            migrationBuilder.DropColumn(
                name: "NarrationAudioId",
                table: "RenderedVideos");
        }
    }
}
