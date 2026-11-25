namespace LeadHunter.Core.Enums;

public enum LeadTier
{
    Tier1HighPriority = 1,
    Tier2MediumPriority = 2,
    Tier3LowPriority = 3
}

public enum CampaignStatus
{
    Created = 0,
    Running = 1,
    PausedByBudget = 2,
    Completed = 3,
    Failed = 4
}
