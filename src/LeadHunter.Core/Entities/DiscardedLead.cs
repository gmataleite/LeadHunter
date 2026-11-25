namespace LeadHunter.Core.Entities;

public sealed class DiscardedLead
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string PlaceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DiscardReason { get; set; } = string.Empty;
    public double? Rating { get; set; }
    public int? UserRatingCount { get; set; }
    public string? BusinessStatus { get; set; }
    public string? WebsiteUri { get; set; }
    public DateTime DiscardedAtUtc { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
}
