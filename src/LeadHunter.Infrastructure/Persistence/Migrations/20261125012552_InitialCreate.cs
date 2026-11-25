using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NicheDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ConfigPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MaxBudgetUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiCostLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    QueryText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCostLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiCostLog_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscardedLeads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DiscardReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: true),
                    UserRatingCount = table.Column<int>(type: "INTEGER", nullable: true),
                    BusinessStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    WebsiteUri = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DiscardedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscardedLeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscardedLeads_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    FormattedAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Rating = table.Column<double>(type: "REAL", nullable: true),
                    UserRatingCount = table.Column<int>(type: "INTEGER", nullable: true),
                    BusinessStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RawPhoneNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MobilePhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LandlinePhone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    WebsiteUri = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    GoogleMapsUri = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReviewsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    IsExported = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    QueryText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NextPageToken = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PagesFetched = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryProgresses_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCostLog_CampaignId_RequestedAtUtc",
                table: "ApiCostLog",
                columns: new[] { "CampaignId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Name",
                table: "Campaigns",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscardedLeads_CampaignId_NormalizedName",
                table: "DiscardedLeads",
                columns: new[] { "CampaignId", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscardedLeads_PlaceId",
                table: "DiscardedLeads",
                column: "PlaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CampaignId_NormalizedName",
                table: "Leads",
                columns: new[] { "CampaignId", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CampaignId_Tier",
                table: "Leads",
                columns: new[] { "CampaignId", "Tier" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_PlaceId",
                table: "Leads",
                column: "PlaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueryProgresses_CampaignId_QueryText",
                table: "QueryProgresses",
                columns: new[] { "CampaignId", "QueryText" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiCostLog");

            migrationBuilder.DropTable(
                name: "DiscardedLeads");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "QueryProgresses");

            migrationBuilder.DropTable(
                name: "Campaigns");
        }
    }
}
