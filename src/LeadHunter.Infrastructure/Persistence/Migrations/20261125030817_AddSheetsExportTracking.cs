using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSheetsExportTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cnpj",
                table: "Leads",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExportedAtUtc",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetId",
                table: "Campaigns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpreadsheetUrl",
                table: "Campaigns",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_PrimaryPhone_ExportedAtUtc",
                table: "Leads",
                columns: new[] { "PrimaryPhone", "ExportedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_PrimaryPhone_ExportedAtUtc",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "Cnpj",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ExportedAtUtc",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SpreadsheetId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SpreadsheetUrl",
                table: "Campaigns");
        }
    }
}
