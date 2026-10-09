using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionCostRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttemptStartedAt",
                table: "RenderJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductionCostRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RenderJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TechniqueCounts = table.Column<string>(type: "jsonb", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RatesVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionCostRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionCostRecords_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionCostRecords_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionCostRecords_RenderJobs_RenderJobId",
                        column: x => x.RenderJobId,
                        principalTable: "RenderJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionCostRecords_OrganizationId",
                table: "ProductionCostRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionCostRecords_ProductId",
                table: "ProductionCostRecords",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionCostRecords_RenderJobId_Attempt",
                table: "ProductionCostRecords",
                columns: new[] { "RenderJobId", "Attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionCostRecords");

            migrationBuilder.DropColumn(
                name: "AttemptStartedAt",
                table: "RenderJobs");
        }
    }
}
