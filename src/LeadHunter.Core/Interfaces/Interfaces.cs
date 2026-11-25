using LeadHunter.Core.Configuration;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Models;

namespace LeadHunter.Core.Interfaces;

public interface IGooglePlacesClient
{
    decimal EstimatedCostPerRequestUsd { get; }

    Task<PlaceSearchResult> SearchTextAsync(
        string textQuery,
        string? pageToken = null,
        CancellationToken cancellationToken = default);
}

public interface IGoogleSearchEnricher
{
    int DailySafeLimit { get; }
    bool IsConfigured { get; }

    Task<WebEnrichmentResult> EnrichAsync(
        Lead lead,
        bool isTier2InstagramOnly,
        CancellationToken cancellationToken = default);
}

public interface IGeminiLeadAnalyzer
{
    bool IsConfigured { get; }

    Task<GeminiAnalysisResult> AnalyzeAsync(
        Lead lead,
        CampaignConfig config,
        CancellationToken cancellationToken = default);
}

public interface IGoogleSheetsExporter
{
    bool IsConfigured { get; }

    Task<SheetsExportResult> ExportAsync(
        Campaign campaign,
        IReadOnlyList<Lead> readyLeads,
        IReadOnlyList<Lead> pendingQuotaLeads,
        CampaignStatusReport auditReport,
        CancellationToken cancellationToken = default);
}

public interface ILeadHunterRepository
{
    Task<Campaign> GetOrCreateCampaignAsync(CampaignConfig config, string configPath, CancellationToken cancellationToken = default);
    Task<Campaign?> GetCampaignByNameAsync(string campaignName, CancellationToken cancellationToken = default);
    Task UpdateCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);

    Task<decimal> GetTotalSpentUsdAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<int> GetValidLeadsCountAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<int> GetConfirmedMobileLeadsCountAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<bool> PlaceExistsAsync(string placeId, CancellationToken cancellationToken = default);
    Task<int> GetNormalizedNameCountAsync(int campaignId, string normalizedName, CancellationToken cancellationToken = default);

    Task AddLeadAsync(Lead lead, CancellationToken cancellationToken = default);
    Task UpdateLeadAsync(Lead lead, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lead>> GetLeadsPendingEnrichmentAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lead>> GetLeadsPendingAnalysisAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lead>> GetLeadsReadyForExportAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lead>> GetLeadsPendingQuotaAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetExportedPrimaryPhonesAsync(CancellationToken cancellationToken = default);
    Task MarkLeadsAsExportedAsync(IEnumerable<Lead> leads, DateTime exportedAtUtc, CancellationToken cancellationToken = default);

    Task AddDiscardedLeadAsync(DiscardedLead discardedLead, CancellationToken cancellationToken = default);
    Task AddApiCostLogAsync(ApiCostLog log, CancellationToken cancellationToken = default);

    Task<DailyQuotaUsage> GetOrCreateDailyQuotaAsync(string serviceName, DateOnly dateUtc, int dailyLimit, CancellationToken cancellationToken = default);
    Task UpdateDailyQuotaAsync(DailyQuotaUsage quota, CancellationToken cancellationToken = default);

    Task<QueryProgress> GetOrCreateQueryProgressAsync(int campaignId, string queryText, CancellationToken cancellationToken = default);
    Task UpdateQueryProgressAsync(QueryProgress progress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CampaignStatusReport>> GetStatusReportsAsync(string? campaignName = null, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICampaignConfigLoader
{
    Task<CampaignConfig> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IProspectingService
{
    Task<ProspectingExecutionSummary> ExecuteAsync(
        CampaignConfig config,
        string configPath,
        bool isPreview,
        bool isResume,
        Action<string>? onStatusMessage = null,
        CancellationToken cancellationToken = default);
}
