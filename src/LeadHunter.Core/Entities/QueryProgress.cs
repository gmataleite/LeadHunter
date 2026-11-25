namespace LeadHunter.Core.Entities;

public sealed class QueryProgress
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string? NextPageToken { get; set; }
    public int PagesFetched { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
}
