using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ludus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DownloadAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CleanupPending",
                table: "Downloads",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReplacementSearchPending",
                table: "Downloads",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionToken",
                table: "Downloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WebhookId = table.Column<long>(type: "INTEGER", nullable: false),
                    Event = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GameTitle = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.CheckConstraint("CK_WebhookDeliveries_Event", "\"Event\" IN ('Grabbed', 'Imported', 'Upgraded', 'DownloadFailed')");
                    table.CheckConstraint("CK_WebhookDelivery_Attempts", "\"Attempts\" >= 0");
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_Webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "Webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_SubmissionToken",
                table: "Downloads",
                column: "SubmissionToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_CompletedAtUtc_NextAttemptAtUtc",
                table: "WebhookDeliveries",
                columns: new[] { "CompletedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId",
                table: "WebhookDeliveries",
                column: "WebhookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Downloads_SubmissionToken",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "CleanupPending",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "ReplacementSearchPending",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "SubmissionToken",
                table: "Downloads");
        }
    }
}
