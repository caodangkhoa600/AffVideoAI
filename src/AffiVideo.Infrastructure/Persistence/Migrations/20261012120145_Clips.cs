using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Clips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClipId",
                table: "StoryboardScenes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "FactIds",
                table: "Stills",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ClipId",
                table: "GenerationCostRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    StillId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FactIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RatesVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    RequestedByMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    AttemptStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FailureDetail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Billed = table.Column<bool>(type: "boolean", nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedByMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clips_Members_ApprovedByMemberId",
                        column: x => x.ApprovedByMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clips_Members_RequestedByMemberId",
                        column: x => x.RequestedByMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clips_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clips_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clips_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Clips_Stills_StillId",
                        column: x => x.StillId,
                        principalTable: "Stills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryboardScenes_ClipId",
                table: "StoryboardScenes",
                column: "ClipId");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCostRecords_ClipId",
                table: "GenerationCostRecords",
                column: "ClipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clips_ApprovedByMemberId",
                table: "Clips",
                column: "ApprovedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Clips_OrganizationId",
                table: "Clips",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Clips_ProductId",
                table: "Clips",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Clips_ProjectId_CreatedAt",
                table: "Clips",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Clips_ProjectId_IdempotencyKey_Ordinal",
                table: "Clips",
                columns: new[] { "ProjectId", "IdempotencyKey", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clips_RequestedByMemberId",
                table: "Clips",
                column: "RequestedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Clips_State_CreatedAt",
                table: "Clips",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Clips_StillId",
                table: "Clips",
                column: "StillId");

            migrationBuilder.AddForeignKey(
                name: "FK_GenerationCostRecords_Clips_ClipId",
                table: "GenerationCostRecords",
                column: "ClipId",
                principalTable: "Clips",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StoryboardScenes_Clips_ClipId",
                table: "StoryboardScenes",
                column: "ClipId",
                principalTable: "Clips",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GenerationCostRecords_Clips_ClipId",
                table: "GenerationCostRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_StoryboardScenes_Clips_ClipId",
                table: "StoryboardScenes");

            migrationBuilder.DropTable(
                name: "Clips");

            migrationBuilder.DropIndex(
                name: "IX_StoryboardScenes_ClipId",
                table: "StoryboardScenes");

            migrationBuilder.DropIndex(
                name: "IX_GenerationCostRecords_ClipId",
                table: "GenerationCostRecords");

            migrationBuilder.DropColumn(
                name: "ClipId",
                table: "StoryboardScenes");

            migrationBuilder.DropColumn(
                name: "FactIds",
                table: "Stills");

            migrationBuilder.DropColumn(
                name: "ClipId",
                table: "GenerationCostRecords");
        }
    }
}
