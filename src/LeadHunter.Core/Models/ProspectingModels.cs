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

public sealed class WebEnrichmentResult
{
    public string QueryExecuted { get; init; } = string.Empty;
    public string? InstagramHandle { get; init; }
    public string? InstagramUrl { get; init; }
    public IReadOnlyList<string> DiscoveredMobilePhones { get; init; } = [];
    public IReadOnlyList<string> DiscoveredLandlines { get; init; } = [];
    public string? SnippetsJson { get; init; }
    public int ResultsCount { get; init; }
    public int HttpStatusCode { get; init; }
    public decimal EstimatedCostUsd { get; init; }
}

public sealed class GeminiAnalysisResult
{
    public bool IsTargetNiche { get; init; } = true;
    public string? OwnerName { get; init; }
    public string? OwnerConfidence { get; init; }
    public string? OwnerSource { get; init; }
    public string? ReviewerFirstName { get; init; }
    public string? PraisedService { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens { get; init; }
    public decimal EstimatedCostUsd { get; init; }
    public int HttpStatusCode { get; init; }
    public string ModelName { get; init; } = "gemini-2.5-flash";
}

public sealed class SheetsExportResult
{
    public string SpreadsheetId { get; init; } = string.Empty;
    public string SpreadsheetUrl { get; init; } = string.Empty;
    public int ExportedLeadsCount { get; init; }
    public int PendingQuotaRowsCount { get; init; }
    public IReadOnlyList<Lead> ExportedLeads { get; init; } = [];
}

public sealed class ProspectingExecutionSummary
{
    public string CampaignName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int TargetValidLeads { get; init; }
    public int ValidLeadsAdded { get; set; }
    public int ConfirmedMobileLeadsCount { get; set; }
    public int OwnersIdentifiedCount { get; set; }
    public int DiscardedCount { get; set; }
    public int DiscardedByNicheCount { get; set; }
    public int SkippedDuplicates { get; set; }
    public int ApiCallsMade { get; set; }
    public int SearchEnrichCallsMade { get; set; }
    public int EnrichedTier1Count { get; set; }
    public int EnrichedTier2Count { get; set; }
    public int PendingQuotaCount { get; set; }
    public int QuotaUsedToday { get; set; }
    public int QuotaDailyLimit { get; set; } = 95;
    public int GeminiAnalyzedCount { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public decimal SessionSpentUsd { get; set; }
    public decimal TotalCampaignSpentUsd { get; set; }
    public decimal MaxBudgetUsd { get; set; }
    public decimal? ProjectedCostForTargetUsd { get; set; }
    public bool StoppedByBudgetCircuitBreaker { get; set; }
    public bool StoppedByTargetLimit { get; set; }
    public bool EnrichmentPausedByDailyQuota { get; set; }

    // Etapa 3: Exportação Google Sheets
    public int ExportedLeadsCount { get; set; }
    public int SkippedExportDuplicatePhoneCount { get; set; }
    public string? SpreadsheetId { get; set; }
    public string? SpreadsheetUrl { get; set; }
    public string? SheetsExportSkippedReason { get; set; }
    public List<Lead> ExportedLeads { get; } = [];

    public List<Lead> NewLeads { get; } = [];
    public List<Lead> ProcessedLeads { get; } = [];
    public Dictionary<string, int> DiscardBreakdown { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CampaignStatusReport
{
    public int CampaignId { get; init; }
    public string CampaignName { get; init; } = string.Empty;
    public string NicheDescription { get; init; } = string.Empty;
    public string ConfigPath { get; init; } = string.Empty;
    public CampaignStatus Status { get; init; }
    public string? SpreadsheetUrl { get; init; }
    public decimal MaxBudgetUsd { get; init; }
    public decimal TotalSpentUsd { get; init; }
    public int TotalApiCalls { get; init; }
    public int TotalGeminiTokens { get; init; }
    public int TotalValidLeads { get; init; }
    public int ConfirmedMobileCount { get; init; }
    public int WithoutMobileCount { get; init; }
    public int OwnersIdentifiedCount { get; init; }
    public int EnrichedCount { get; init; }
    public int PendingQuotaCount { get; init; }
    public int AnalyzedCount { get; init; }
    public int DiscardedByNicheCount { get; init; }
    public int Tier1Count { get; init; }
    public int Tier2Count { get; init; }
    public int Tier3Count { get; init; }
    public int ExportedLeadsCount { get; init; }
    public int TotalDiscardedLeads { get; init; }
    public int CompletedQueries { get; init; }
    public int TotalQueries { get; init; }
    public int QuotaUsedToday { get; init; }
    public int QuotaDailyLimit { get; init; } = 95;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastRunAtUtc { get; init; }
    public IReadOnlyDictionary<string, int> DiscardReasons { get; init; } = new Dictionary<string, int>();
}
