using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpokenNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Voice",
                table: "Projects",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "sarah");

            migrationBuilder.AddColumn<string>(
                name: "DefaultVoice",
                table: "Organizations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpeechId",
                table: "GenerationCostRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Speech",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Voice = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Speech", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Speech_Members_RequestedByMemberId",
                        column: x => x.RequestedByMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Speech_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Speech_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Speech_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCostRecords_SpeechId",
                table: "GenerationCostRecords",
                column: "SpeechId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Speech_OrganizationId",
                table: "Speech",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Speech_ProductId",
                table: "Speech",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Speech_ProjectId_CreatedAt",
                table: "Speech",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Speech_ProjectId_IdempotencyKey_Ordinal",
                table: "Speech",
                columns: new[] { "ProjectId", "IdempotencyKey", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Speech_RequestedByMemberId",
                table: "Speech",
                column: "RequestedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Speech_State_CreatedAt",
                table: "Speech",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_GenerationCostRecords_Speech_SpeechId",
                table: "GenerationCostRecords",
                column: "SpeechId",
                principalTable: "Speech",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GenerationCostRecords_Speech_SpeechId",
                table: "GenerationCostRecords");

            migrationBuilder.DropTable(
                name: "Speech");

            migrationBuilder.DropIndex(
                name: "IX_GenerationCostRecords_SpeechId",
                table: "GenerationCostRecords");

            migrationBuilder.DropColumn(
                name: "Voice",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DefaultVoice",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SpeechId",
                table: "GenerationCostRecords");
        }
    }
}
