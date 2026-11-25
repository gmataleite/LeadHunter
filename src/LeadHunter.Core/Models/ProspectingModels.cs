using LeadHunter.Core.Entities;
using LeadHunter.Core.Enums;

namespace LeadHunter.Core.Models;

public sealed class PlaceSearchResult
{
    public IReadOnlyList<PlaceCandidate> Places { get; init; } = [];
    public string? NextPageToken { get; init; }
    public int HttpStatusCode { get; init; }
    public decimal EstimatedCostUsd { get; init; }
}

public sealed class PlaceCandidate
{
    public string PlaceId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? FormattedAddress { get; init; }
    public double? Rating { get; init; }
    public int? UserRatingCount { get; init; }
    public string? BusinessStatus { get; init; }
    public string? NationalPhoneNumber { get; init; }
    public string? WebsiteUri { get; init; }
    public string? GoogleMapsUri { get; init; }
    public string? ReviewsJson { get; init; }
}

public sealed record PhoneNormalizationResult(
    string? RawInput,
    string? MobilePhone,
    string? LandlinePhone)
{
    public bool HasMobile => !string.IsNullOrWhiteSpace(MobilePhone);
    public bool HasLandline => !string.IsNullOrWhiteSpace(LandlinePhone);
}

public sealed record LeadClassificationResult(
    bool IsQualified,
    LeadTier? Tier,
    string? DiscardReason,
    PhoneNormalizationResult Phone,
    string NormalizedName);

public sealed class ProspectingExecutionSummary
{
    public string CampaignName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int ValidLeadsAdded { get; set; }
    public int DiscardedCount { get; set; }
    public int SkippedDuplicates { get; set; }
    public int ApiCallsMade { get; set; }
    public decimal SessionSpentUsd { get; set; }
    public decimal TotalCampaignSpentUsd { get; set; }
    public decimal MaxBudgetUsd { get; set; }
    public bool StoppedByBudgetCircuitBreaker { get; set; }
    public bool StoppedByTargetLimit { get; set; }
    public List<Lead> NewLeads { get; } = [];
    public Dictionary<string, int> DiscardBreakdown { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CampaignStatusReport
{
    public int CampaignId { get; init; }
    public string CampaignName { get; init; } = string.Empty;
    public string NicheDescription { get; init; } = string.Empty;
    public string ConfigPath { get; init; } = string.Empty;
    public CampaignStatus Status { get; init; }
    public decimal MaxBudgetUsd { get; init; }
    public decimal TotalSpentUsd { get; init; }
    public int TotalApiCalls { get; init; }
    public int TotalValidLeads { get; init; }
    public int Tier1Count { get; init; }
    public int Tier2Count { get; init; }
    public int Tier3Count { get; init; }
    public int ExportedLeadsCount { get; init; }
    public int TotalDiscardedLeads { get; init; }
    public int CompletedQueries { get; init; }
    public int TotalQueries { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastRunAtUtc { get; init; }
    public IReadOnlyDictionary<string, int> DiscardReasons { get; init; } = new Dictionary<string, int>();
}
