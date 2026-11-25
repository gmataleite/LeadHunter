using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentAndGeminiAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAtUtc",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Leads",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnrichmentStatus",
                table: "Leads",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InstagramHandle",
                table: "Leads",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "Leads",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTargetNiche",
                table: "Leads",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerConfidence",
                table: "Leads",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "Leads",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerSource",
                table: "Leads",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PraisedService",
                table: "Leads",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryPhone",
                table: "Leads",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerFirstName",
                table: "Leads",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPhone",
                table: "Leads",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebSnippetsJson",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "ApiCostLog",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "ApiCostLog",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "ApiCostLog",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "ApiCostLog",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailyQuotaUsage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DateUtc = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    QueriesUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuotaUsage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CampaignId_EnrichmentStatus",
                table: "Leads",
                columns: new[] { "CampaignId", "EnrichmentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuotaUsage_ServiceName_DateUtc",
                table: "DailyQuotaUsage",
                columns: new[] { "ServiceName", "DateUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyQuotaUsage");

            migrationBuilder.DropIndex(
                name: "IX_Leads_CampaignId_EnrichmentStatus",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "AnalyzedAtUtc",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "EnrichmentStatus",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "InstagramHandle",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "IsTargetNiche",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "OwnerConfidence",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "OwnerSource",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "PraisedService",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "PrimaryPhone",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ReviewerFirstName",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SecondaryPhone",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "WebSnippetsJson",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "ApiCostLog");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "ApiCostLog");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "ApiCostLog");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "ApiCostLog");
        }
    }
}
