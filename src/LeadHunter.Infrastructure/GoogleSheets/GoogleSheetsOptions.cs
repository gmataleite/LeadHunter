namespace LeadHunter.Infrastructure.GoogleSheets;

public sealed class GoogleSheetsOptions
{
    public const string SectionName = "GoogleSheets";

    public string ServiceAccountJsonPath { get; set; } = "service-account.json";
    public string TargetShareEmail { get; set; } = string.Empty;
    public string SpreadsheetId { get; set; } = string.Empty;
}
