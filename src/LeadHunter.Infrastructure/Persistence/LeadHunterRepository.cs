using LeadHunter.Core.Configuration;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Enums;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadHunter.Infrastructure.Persistence;

public sealed class LeadHunterRepository : ILeadHunterRepository
{
    private readonly LeadHunterDbContext _dbContext;

    public LeadHunterRepository(LeadHunterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Campaign> GetOrCreateCampaignAsync(
        CampaignConfig config,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Campaigns
            .FirstOrDefaultAsync(c => c.Name == config.CampaignName, cancellationToken);

        if (existing is not null)
        {
            existing.NicheDescription = config.NicheDescription;
            existing.ConfigPath = configPath;
            existing.MaxBudgetUsd = config.Limits.MaxBudgetUsd;
            return existing;
        }

        var campaign = new Campaign
        {
            Name = config.CampaignName,
            NicheDescription = config.NicheDescription,
            ConfigPath = configPath,
            MaxBudgetUsd = config.Limits.MaxBudgetUsd,
            Status = CampaignStatus.Created,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.Campaigns.AddAsync(campaign, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return campaign;
    }

    public Task<Campaign?> GetCampaignByNameAsync(string campaignName, CancellationToken cancellationToken = default) =>
        _dbContext.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == campaignName, cancellationToken);

    public Task UpdateCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        _dbContext.Campaigns.Update(campaign);
        return Task.CompletedTask;
    }

    public async Task<decimal> GetTotalSpentUsdAsync(int campaignId, CancellationToken cancellationToken = default)
    {
        var costs = await _dbContext.ApiCostLogs
            .AsNoTracking()
            .Where(a => a.CampaignId == campaignId)
            .Select(a => a.EstimatedCostUsd)
            .ToListAsync(cancellationToken);

        return costs.Sum();
    }

    public Task<int> GetValidLeadsCountAsync(int campaignId, CancellationToken cancellationToken = default) =>
        _dbContext.Leads
            .AsNoTracking()
            .CountAsync(
                l => l.CampaignId == campaignId && l.EnrichmentStatus != EnrichmentStatus.DiscardedByNiche,
                cancellationToken);

    public Task<int> GetConfirmedMobileLeadsCountAsync(int campaignId, CancellationToken cancellationToken = default) =>
        _dbContext.Leads
            .AsNoTracking()
            .CountAsync(
                l => l.CampaignId == campaignId
                     && l.EnrichmentStatus != EnrichmentStatus.DiscardedByNiche
                     && ((l.PrimaryPhone != null && l.PrimaryPhone != string.Empty)
                         || (l.MobilePhone != null && l.MobilePhone != string.Empty)),
                cancellationToken);

    public async Task<bool> PlaceExistsAsync(string placeId, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Leads.Local.Any(l => l.PlaceId == placeId) ||
            _dbContext.DiscardedLeads.Local.Any(d => d.PlaceId == placeId))
        {
            return true;
        }

        if (await _dbContext.Leads.AsNoTracking().AnyAsync(l => l.PlaceId == placeId, cancellationToken))
        {
            return true;
        }

        return await _dbContext.DiscardedLeads.AsNoTracking().AnyAsync(d => d.PlaceId == placeId, cancellationToken);
    }

    public async Task<int> GetNormalizedNameCountAsync(
        int campaignId,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        var leadsCount = await _dbContext.Leads
            .AsNoTracking()
            .CountAsync(l => l.CampaignId == campaignId && l.NormalizedName == normalizedName, cancellationToken);

        var discardedCount = await _dbContext.DiscardedLeads
            .AsNoTracking()
            .CountAsync(d => d.CampaignId == campaignId && d.NormalizedName == normalizedName, cancellationToken);

        return leadsCount + discardedCount;
    }

    public async Task AddLeadAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        await _dbContext.Leads.AddAsync(lead, cancellationToken);
    }

    public Task UpdateLeadAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        _dbContext.Leads.Update(lead);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Lead>> GetLeadsPendingEnrichmentAsync(
        int campaignId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Leads
            .Where(l => l.CampaignId == campaignId &&
                        (l.EnrichmentStatus == EnrichmentStatus.Pending ||
                         l.EnrichmentStatus == EnrichmentStatus.PendingQuota))
            .OrderBy(l => l.Tier)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lead>> GetLeadsPendingAnalysisAsync(
        int campaignId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Leads
            .Where(l => l.CampaignId == campaignId &&
                        l.AnalyzedAtUtc == null &&
                        l.EnrichmentStatus == EnrichmentStatus.Enriched)
            .OrderBy(l => l.Tier)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lead>> GetLeadsReadyForExportAsync(
        int campaignId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Leads
            .Where(l => l.CampaignId == campaignId &&
                        l.ExportedAtUtc == null &&
                        !l.IsExported &&
                        l.EnrichmentStatus != EnrichmentStatus.DiscardedByNiche &&
                        ((l.PrimaryPhone != null && l.PrimaryPhone != string.Empty) ||
                         (l.MobilePhone != null && l.MobilePhone != string.Empty)))
            .OrderBy(l => l.Tier)
            .ThenByDescending(l => l.Rating)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lead>> GetLeadsPendingQuotaAsync(
        int campaignId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Leads
            .AsNoTracking()
            .Where(l => l.CampaignId == campaignId && l.EnrichmentStatus == EnrichmentStatus.PendingQuota)
            .OrderBy(l => l.Tier)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetExportedPrimaryPhonesAsync(CancellationToken cancellationToken = default)
    {
        var phones = await _dbContext.Leads
            .AsNoTracking()
            .Where(l => (l.ExportedAtUtc != null || l.IsExported) &&
                        ((l.PrimaryPhone != null && l.PrimaryPhone != string.Empty) ||
                         (l.MobilePhone != null && l.MobilePhone != string.Empty)))
            .Select(l => l.PrimaryPhone ?? l.MobilePhone!)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(phones, StringComparer.Ordinal);
    }

    public Task MarkLeadsAsExportedAsync(
        IEnumerable<Lead> leads,
        DateTime exportedAtUtc,
        CancellationToken cancellationToken = default)
    {
        foreach (var lead in leads)
        {
            lead.IsExported = true;
            lead.ExportedAtUtc = exportedAtUtc;
            lead.UpdatedAtUtc = exportedAtUtc;
            _dbContext.Leads.Update(lead);
        }

        return Task.CompletedTask;
    }

    public async Task AddDiscardedLeadAsync(DiscardedLead discardedLead, CancellationToken cancellationToken = default)
    {
        // Guard against duplicate PlaceId in DiscardedLeads (e.g., if already discarded)
        if (_dbContext.DiscardedLeads.Local.Any(d => d.PlaceId == discardedLead.PlaceId) ||
            await _dbContext.DiscardedLeads.AsNoTracking().AnyAsync(d => d.PlaceId == discardedLead.PlaceId, cancellationToken))
        {
            return;
        }

        await _dbContext.DiscardedLeads.AddAsync(discardedLead, cancellationToken);
    }

    public async Task AddApiCostLogAsync(ApiCostLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.ApiCostLogs.AddAsync(log, cancellationToken);
    }

    public async Task<DailyQuotaUsage> GetOrCreateDailyQuotaAsync(
        string serviceName,
        DateOnly dateUtc,
        int dailyLimit,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.DailyQuotaUsages
            .FirstOrDefaultAsync(q => q.ServiceName == serviceName && q.DateUtc == dateUtc, cancellationToken);

        if (existing is not null)
        {
            existing.DailyLimit = dailyLimit;
            return existing;
        }

        var quota = new DailyQuotaUsage
        {
            ServiceName = serviceName,
            DateUtc = dateUtc,
            QueriesUsed = 0,
            DailyLimit = dailyLimit,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.DailyQuotaUsages.AddAsync(quota, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return quota;
    }

    public Task UpdateDailyQuotaAsync(DailyQuotaUsage quota, CancellationToken cancellationToken = default)
    {
        _dbContext.DailyQuotaUsages.Update(quota);
        return Task.CompletedTask;
    }

    public async Task<QueryProgress> GetOrCreateQueryProgressAsync(
        int campaignId,
        string queryText,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.QueryProgresses
            .FirstOrDefaultAsync(q => q.CampaignId == campaignId && q.QueryText == queryText, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var progress = new QueryProgress
        {
            CampaignId = campaignId,
            QueryText = queryText,
            PagesFetched = 0,
            IsCompleted = false,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.QueryProgresses.AddAsync(progress, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return progress;
    }

    public Task UpdateQueryProgressAsync(QueryProgress progress, CancellationToken cancellationToken = default)
    {
        _dbContext.QueryProgresses.Update(progress);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CampaignStatusReport>> GetStatusReportsAsync(
        string? campaignName = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Campaigns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(campaignName))
        {
            query = query.Where(c => c.Name == campaignName);
        }

        var campaigns = await query
            .OrderByDescending(c => c.LastRunAtUtc ?? c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayQuota = await _dbContext.DailyQuotaUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.ServiceName == "GoogleCustomSearch" && q.DateUtc == todayUtc, cancellationToken);

        var reports = new List<CampaignStatusReport>(campaigns.Count);

        foreach (var campaign in campaigns)
        {
            var leads = await _dbContext.Leads
                .AsNoTracking()
                .Where(l => l.CampaignId == campaign.Id)
                .Select(l => new
                {
                    l.Tier,
                    l.IsExported,
                    l.ExportedAtUtc,
                    l.EnrichmentStatus,
                    l.PrimaryPhone,
                    l.MobilePhone,
                    l.OwnerName,
                    l.OwnerConfidence,
                    l.AnalyzedAtUtc
                })
                .ToListAsync(cancellationToken);

            var validLeads = leads.Where(l => l.EnrichmentStatus != EnrichmentStatus.DiscardedByNiche).ToList();

            var discardedReasons = await _dbContext.DiscardedLeads
                .AsNoTracking()
                .Where(d => d.CampaignId == campaign.Id)
                .Select(d => d.DiscardReason)
                .ToListAsync(cancellationToken);

            var apiLogs = await _dbContext.ApiCostLogs
                .AsNoTracking()
                .Where(a => a.CampaignId == campaign.Id)
                .Select(a => new { a.EstimatedCostUsd, a.TotalTokens })
                .ToListAsync(cancellationToken);

            var queryProgresses = await _dbContext.QueryProgresses
                .AsNoTracking()
                .Where(q => q.CampaignId == campaign.Id)
                .Select(q => q.IsCompleted)
                .ToListAsync(cancellationToken);

            var reasonBuckets = discardedReasons
                .GroupBy(reason =>
                {
                    var colonIdx = reason.IndexOf(':');
                    if (colonIdx > 0) return reason[..colonIdx].Trim();
                    var parenIdx = reason.IndexOf('(');
                    return parenIdx > 0 ? reason[..parenIdx].Trim() : reason;
                }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var confirmedMobileCount = validLeads.Count(l => !string.IsNullOrWhiteSpace(l.PrimaryPhone ?? l.MobilePhone));
            var ownersIdentifiedCount = validLeads.Count(l =>
                !string.IsNullOrWhiteSpace(l.OwnerName) &&
                (string.Equals(l.OwnerConfidence, "Alta", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(l.OwnerConfidence, "Média", StringComparison.OrdinalIgnoreCase)));

            reports.Add(new CampaignStatusReport
            {
                CampaignId = campaign.Id,
                CampaignName = campaign.Name,
                NicheDescription = campaign.NicheDescription,
                ConfigPath = campaign.ConfigPath,
                Status = campaign.Status,
                SpreadsheetUrl = campaign.SpreadsheetUrl,
                MaxBudgetUsd = campaign.MaxBudgetUsd,
                TotalSpentUsd = apiLogs.Sum(a => a.EstimatedCostUsd),
                TotalApiCalls = apiLogs.Count,
                TotalGeminiTokens = apiLogs.Sum(a => a.TotalTokens ?? 0),
                TotalValidLeads = validLeads.Count,
                ConfirmedMobileCount = confirmedMobileCount,
                WithoutMobileCount = validLeads.Count - confirmedMobileCount,
                OwnersIdentifiedCount = ownersIdentifiedCount,
                EnrichedCount = validLeads.Count(l => l.EnrichmentStatus is EnrichmentStatus.Enriched or EnrichmentStatus.Completed),
                PendingQuotaCount = validLeads.Count(l => l.EnrichmentStatus == EnrichmentStatus.PendingQuota),
                AnalyzedCount = leads.Count(l => l.AnalyzedAtUtc != null),
                DiscardedByNicheCount = leads.Count(l => l.EnrichmentStatus == EnrichmentStatus.DiscardedByNiche),
                Tier1Count = validLeads.Count(l => l.Tier == LeadTier.Tier1HighPriority),
                Tier2Count = validLeads.Count(l => l.Tier == LeadTier.Tier2MediumPriority),
                Tier3Count = validLeads.Count(l => l.Tier == LeadTier.Tier3LowPriority),
                ExportedLeadsCount = validLeads.Count(l => l.IsExported || l.ExportedAtUtc != null),
                TotalDiscardedLeads = discardedReasons.Count,
                CompletedQueries = queryProgresses.Count(done => done),
                TotalQueries = queryProgresses.Count,
                QuotaUsedToday = todayQuota?.QueriesUsed ?? 0,
                QuotaDailyLimit = todayQuota?.DailyLimit ?? 95,
                CreatedAtUtc = campaign.CreatedAtUtc,
                LastRunAtUtc = campaign.LastRunAtUtc,
                DiscardReasons = reasonBuckets
            });
        }

        return reports;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
