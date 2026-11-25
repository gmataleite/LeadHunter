using LeadHunter.Core.Enums;

namespace LeadHunter.Core.Entities;

public sealed class Campaign
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NicheDescription { get; set; } = string.Empty;
    public string ConfigPath { get; set; } = string.Empty;
    public decimal MaxBudgetUsd { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Created;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAtUtc { get; set; }

    public ICollection<Lead> Leads { get; set; } = [];
    public ICollection<DiscardedLead> DiscardedLeads { get; set; } = [];
    public ICollection<ApiCostLog> ApiCostLogs { get; set; } = [];
    public ICollection<QueryProgress> QueryProgresses { get; set; } = [];
}
