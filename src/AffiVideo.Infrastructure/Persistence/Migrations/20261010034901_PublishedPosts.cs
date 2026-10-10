using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublishedPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffiliateLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateLinks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocialAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublishedPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RenderedVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedPosts_AffiliateLinks_AffiliateLinkId",
                        column: x => x.AffiliateLinkId,
                        principalTable: "AffiliateLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedPosts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedPosts_RenderedVideos_RenderedVideoId",
                        column: x => x.RenderedVideoId,
                        principalTable: "RenderedVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedPosts_SocialAccounts_SocialAccountId",
                        column: x => x.SocialAccountId,
                        principalTable: "SocialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateLinks_OrganizationId_Url",
                table: "AffiliateLinks",
                columns: new[] { "OrganizationId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublishedPosts_AffiliateLinkId",
                table: "PublishedPosts",
                column: "AffiliateLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedPosts_OrganizationId_PublishedOn",
                table: "PublishedPosts",
                columns: new[] { "OrganizationId", "PublishedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedPosts_OrganizationId_Url",
                table: "PublishedPosts",
                columns: new[] { "OrganizationId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublishedPosts_RenderedVideoId",
                table: "PublishedPosts",
                column: "RenderedVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedPosts_SocialAccountId",
                table: "PublishedPosts",
                column: "SocialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_OrganizationId_Platform_Handle",
                table: "SocialAccounts",
                columns: new[] { "OrganizationId", "Platform", "Handle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublishedPosts");

            migrationBuilder.DropTable(
                name: "AffiliateLinks");

            migrationBuilder.DropTable(
                name: "SocialAccounts");
        }
    }
}
