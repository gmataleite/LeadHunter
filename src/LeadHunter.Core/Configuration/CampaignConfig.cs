namespace LeadHunter.Core.Configuration;

public sealed class CampaignConfig
{
    public string CampaignName { get; set; } = string.Empty;
    public string NicheDescription { get; set; } = string.Empty;
    public List<string> SearchQueries { get; set; } = [];
    public List<string> ExcludeKeywords { get; set; } = [];
    public int MaxFranchiseBranches { get; set; } = 3;
    public CampaignFilters Filters { get; set; } = new();
    public CampaignLimits Limits { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CampaignName))
            throw new InvalidOperationException("CampaignConfig.CampaignName is required.");

        if (SearchQueries is null || SearchQueries.Count == 0 || SearchQueries.All(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("CampaignConfig.SearchQueries must contain at least one query.");

        if (MaxFranchiseBranches < 1)
            throw new InvalidOperationException("CampaignConfig.MaxFranchiseBranches must be at least 1.");

        if (Filters.MinReviews < 0 || Filters.MaxReviews < Filters.MinReviews)
            throw new InvalidOperationException("CampaignConfig.Filters review bounds are invalid.");

        if (Limits.TargetValidLeads < 1)
            throw new InvalidOperationException("CampaignConfig.Limits.TargetValidLeads must be at least 1.");

        if (Limits.PreviewBatchSize < 1)
            throw new InvalidOperationException("CampaignConfig.Limits.PreviewBatchSize must be at least 1.");

        if (Limits.MaxBudgetUsd <= 0m)
            throw new InvalidOperationException("CampaignConfig.Limits.MaxBudgetUsd must be greater than zero.");
    }
}

public sealed class CampaignFilters
{
    public double MinRating { get; set; } = 4.0;
    public int MinReviews { get; set; } = 10;
    public int MaxReviews { get; set; } = 300;
    public bool AllowHasWebsite { get; set; } = false;
}

public sealed class CampaignLimits
{
    public int TargetValidLeads { get; set; } = 100;
    public int PreviewBatchSize { get; set; } = 5;
    public decimal MaxBudgetUsd { get; set; } = 0.80m;
}
