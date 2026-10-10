using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommissionReportsAndSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // What was kept as the source is the name of the report: it is kept, under its own name.
            migrationBuilder.RenameColumn(
                name: "Source",
                table: "CommissionRecords",
                newName: "Report");

            migrationBuilder.RenameIndex(
                name: "IX_CommissionRecords_AffiliateLinkId_Source_Currency_PeriodSta~",
                table: "CommissionRecords",
                newName: "IX_CommissionRecords_AffiliateLinkId_Report_Currency_PeriodSta~");

            migrationBuilder.RenameIndex(
                name: "IX_CommissionRecords_ProductId_Source_Currency_PeriodStart_Per~",
                table: "CommissionRecords",
                newName: "IX_CommissionRecords_ProductId_Report_Currency_PeriodStart_Per~");

            // Every record so far was typed in.
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "CommissionRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "CommissionRecords");

            migrationBuilder.RenameIndex(
                name: "IX_CommissionRecords_AffiliateLinkId_Report_Currency_PeriodSta~",
                table: "CommissionRecords",
                newName: "IX_CommissionRecords_AffiliateLinkId_Source_Currency_PeriodSta~");

            migrationBuilder.RenameIndex(
                name: "IX_CommissionRecords_ProductId_Report_Currency_PeriodStart_Per~",
                table: "CommissionRecords",
                newName: "IX_CommissionRecords_ProductId_Source_Currency_PeriodStart_Per~");

            migrationBuilder.RenameColumn(
                name: "Report",
                table: "CommissionRecords",
                newName: "Source");
        }
    }
}
