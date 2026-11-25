namespace LeadHunter.Core.Entities;

public sealed class ApiCostLog
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string Provider { get; set; } = "GooglePlaces";
    public string Endpoint { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int ResultsCount { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int HttpStatusCode { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
}
