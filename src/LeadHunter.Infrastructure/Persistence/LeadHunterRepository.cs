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
            .CountAsync(l => l.CampaignId == campaignId, cancellationToken);

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

    public async Task AddDiscardedLeadAsync(DiscardedLead discardedLead, CancellationToken cancellationToken = default)
    {
        await _dbContext.DiscardedLeads.AddAsync(discardedLead, cancellationToken);
    }

    public async Task AddApiCostLogAsync(ApiCostLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.ApiCostLogs.AddAsync(log, cancellationToken);
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

        var reports = new List<CampaignStatusReport>(campaigns.Count);

        foreach (var campaign in campaigns)
        {
            var leads = await _dbContext.Leads
                .AsNoTracking()
                .Where(l => l.CampaignId == campaign.Id)
                .Select(l => new { l.Tier, l.IsExported })
                .ToListAsync(cancellationToken);

            var discardedReasons = await _dbContext.DiscardedLeads
                .AsNoTracking()
                .Where(d => d.CampaignId == campaign.Id)
                .Select(d => d.DiscardReason)
                .ToListAsync(cancellationToken);

            var apiLogs = await _dbContext.ApiCostLogs
                .AsNoTracking()
                .Where(a => a.CampaignId == campaign.Id)
                .Select(a => a.EstimatedCostUsd)
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

            reports.Add(new CampaignStatusReport
            {
                CampaignId = campaign.Id,
                CampaignName = campaign.Name,
                NicheDescription = campaign.NicheDescription,
                ConfigPath = campaign.ConfigPath,
                Status = campaign.Status,
                MaxBudgetUsd = campaign.MaxBudgetUsd,
                TotalSpentUsd = apiLogs.Sum(),
                TotalApiCalls = apiLogs.Count,
                TotalValidLeads = leads.Count,
                Tier1Count = leads.Count(l => l.Tier == LeadTier.Tier1HighPriority),
                Tier2Count = leads.Count(l => l.Tier == LeadTier.Tier2MediumPriority),
                Tier3Count = leads.Count(l => l.Tier == LeadTier.Tier3LowPriority),
                ExportedLeadsCount = leads.Count(l => l.IsExported),
                TotalDiscardedLeads = discardedReasons.Count,
                CompletedQueries = queryProgresses.Count(done => done),
                TotalQueries = queryProgresses.Count,
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
