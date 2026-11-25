namespace LeadHunter.Core.Entities;

public sealed class DailyQuotaUsage
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = "GoogleCustomSearch";
    public DateOnly DateUtc { get; set; }
    public int QueriesUsed { get; set; }
    public int DailyLimit { get; set; } = 95;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
