using LeadHunter.Core.Enums;

namespace LeadHunter.Core.Entities;

public sealed class Lead
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string PlaceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? FormattedAddress { get; set; }
    public double? Rating { get; set; }
    public int? UserRatingCount { get; set; }
    public string? BusinessStatus { get; set; }
    public string? RawPhoneNumber { get; set; }
    public string? MobilePhone { get; set; }
    public string? LandlinePhone { get; set; }
    public string? WebsiteUri { get; set; }
    public string? GoogleMapsUri { get; set; }
    public string? ReviewsJson { get; set; }
    public LeadTier Tier { get; set; }
    public bool IsExported { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
}
