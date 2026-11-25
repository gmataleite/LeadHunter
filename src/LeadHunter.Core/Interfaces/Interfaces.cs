using LeadHunter.Core.Configuration;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Models;

namespace LeadHunter.Core.Interfaces;

public interface IGooglePlacesClient
{
    /// <summary>
    /// Estimated cost per Text Search (Pro + Reviews / Enterprise) request in USD.
    /// </summary>
    decimal EstimatedCostPerRequestUsd { get; }

    Task<PlaceSearchResult> SearchTextAsync(
        string textQuery,
        string? pageToken = null,
        CancellationToken cancellationToken = default);
}

public interface ILeadHunterRepository
{
    Task<Campaign> GetOrCreateCampaignAsync(CampaignConfig config, string configPath, CancellationToken cancellationToken = default);
    Task<Campaign?> GetCampaignByNameAsync(string campaignName, CancellationToken cancellationToken = default);
    Task UpdateCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);

    Task<decimal> GetTotalSpentUsdAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<int> GetValidLeadsCountAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<bool> PlaceExistsAsync(string placeId, CancellationToken cancellationToken = default);
    Task<int> GetNormalizedNameCountAsync(int campaignId, string normalizedName, CancellationToken cancellationToken = default);

    Task AddLeadAsync(Lead lead, CancellationToken cancellationToken = default);
    Task AddDiscardedLeadAsync(DiscardedLead discardedLead, CancellationToken cancellationToken = default);
    Task AddApiCostLogAsync(ApiCostLog log, CancellationToken cancellationToken = default);

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
