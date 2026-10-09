using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiVideo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FailureReason",
                table: "RenderJobs",
                newName: "FailureMessage");

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "RenderJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvailableAt",
                table: "RenderJobs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "FailureCategory",
                table: "RenderJobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureDetail",
                table: "RenderJobs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureStage",
                table: "RenderJobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "RenderJobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "RenderJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "RenderJobs",
                type: "uuid",
                nullable: true);

            // The jobs there already are. Each was one click, so each gets a key of its own; and
            // each could be taken from the moment it was made.
            migrationBuilder.Sql("""
                UPDATE "RenderJobs" SET "IdempotencyKey" = "Id"::text, "AvailableAt" = "CreatedAt",
                    "Attempt" = CASE WHEN "State" = 'Queued' THEN 0 ELSE 1 END
                """);
            // A job a worker was rendering when it stopped has stayed in that state since. With
            // a lease that has run out, the first worker to look puts it back in the queue.
            migrationBuilder.Sql("""
                UPDATE "RenderJobs" SET "LeaseId" = gen_random_uuid(), "LeaseExpiresAt" = now()
                WHERE "State" IN ('Validating', 'Planning', 'GeneratingAssets', 'GeneratingVideo', 'Rendering', 'QualityReview')
                """);
            // A failure used to be its message alone. The stage it happened in was not kept: a
            // Storyboard that was refused failed while validating, and rendering stands for the rest.
            migrationBuilder.Sql("""
                UPDATE "RenderJobs" SET
                    "FailureStage" = CASE WHEN "FailureMessage" LIKE 'The video could not be rendered:%' THEN 'Rendering' ELSE 'Validating' END,
                    "FailureCategory" = CASE WHEN "FailureMessage" LIKE 'The video could not be rendered:%' THEN 'Internal' ELSE 'InvalidInput' END
                WHERE "State" = 'Failed' AND "FailureMessage" IS NOT NULL
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RenderJobs_StoryboardId_IdempotencyKey",
                table: "RenderJobs",
                columns: new[] { "StoryboardId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenderJobs_StoryboardId_IdempotencyKey",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "AvailableAt",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "FailureCategory",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "FailureDetail",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "FailureStage",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "RenderJobs");

            migrationBuilder.RenameColumn(
                name: "FailureMessage",
                table: "RenderJobs",
                newName: "FailureReason");
        }
    }
}
