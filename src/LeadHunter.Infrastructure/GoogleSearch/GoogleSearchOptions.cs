namespace LeadHunter.Infrastructure.GoogleSearch;

public sealed class GoogleSearchOptions
{
    public const string SectionName = "GoogleSearch";

    public string ApiKey { get; set; } = string.Empty;
    public string SearchEngineId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://www.googleapis.com/customsearch/v1";
    public int DailySafeLimit { get; set; } = 95;
    public int MaxResultsPerQuery { get; set; } = 5;
    public decimal EstimatedCostPerRequestUsd { get; set; } = 0.000m;
}
