using LeadHunter.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadHunter.Infrastructure.Persistence;

public sealed class LeadHunterDbContext : DbContext
{
    public LeadHunterDbContext(DbContextOptions<LeadHunterDbContext> options)
        : base(options)
    {
    }

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<DiscardedLead> DiscardedLeads => Set<DiscardedLead>();
    public DbSet<ApiCostLog> ApiCostLogs => Set<ApiCostLog>();
    public DbSet<QueryProgress> QueryProgresses => Set<QueryProgress>();
    public DbSet<DailyQuotaUsage> DailyQuotaUsages => Set<DailyQuotaUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.ToTable("Campaigns");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.Property(c => c.NicheDescription).HasMaxLength(500);
            entity.Property(c => c.ConfigPath).HasMaxLength(500);
            entity.Property(c => c.SpreadsheetId).HasMaxLength(200);
            entity.Property(c => c.SpreadsheetUrl).HasMaxLength(500);
            entity.Property(c => c.MaxBudgetUsd).HasPrecision(18, 4);
            entity.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("Leads");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.PlaceId).IsRequired().HasMaxLength(255);
            entity.Property(l => l.DisplayName).IsRequired().HasMaxLength(300);
            entity.Property(l => l.NormalizedName).IsRequired().HasMaxLength(300);
            entity.Property(l => l.FormattedAddress).HasMaxLength(500);
            entity.Property(l => l.City).HasMaxLength(120);
            entity.Property(l => l.BusinessStatus).HasMaxLength(64);
            entity.Property(l => l.RawPhoneNumber).HasMaxLength(64);
            entity.Property(l => l.MobilePhone).HasMaxLength(32);
            entity.Property(l => l.LandlinePhone).HasMaxLength(32);
            entity.Property(l => l.PrimaryPhone).HasMaxLength(32);
            entity.Property(l => l.SecondaryPhone).HasMaxLength(32);
            entity.Property(l => l.InstagramHandle).HasMaxLength(100);
            entity.Property(l => l.InstagramUrl).HasMaxLength(500);
            entity.Property(l => l.Cnpj).HasMaxLength(32);
            entity.Property(l => l.OwnerName).HasMaxLength(200);
            entity.Property(l => l.OwnerConfidence).HasMaxLength(32);
            entity.Property(l => l.OwnerSource).HasMaxLength(200);
            entity.Property(l => l.ReviewerFirstName).HasMaxLength(100);
            entity.Property(l => l.PraisedService).HasMaxLength(120);
            entity.Property(l => l.WebsiteUri).HasMaxLength(1000);
            entity.Property(l => l.GoogleMapsUri).HasMaxLength(1000);

            // Idempotency: never store or export the same PlaceId twice
            entity.HasIndex(l => l.PlaceId).IsUnique();
            entity.HasIndex(l => new { l.CampaignId, l.Tier });
            entity.HasIndex(l => new { l.CampaignId, l.EnrichmentStatus });
            entity.HasIndex(l => new { l.CampaignId, l.NormalizedName });
            entity.HasIndex(l => new { l.PrimaryPhone, l.ExportedAtUtc });

            entity.HasOne(l => l.Campaign)
                .WithMany(c => c.Leads)
                .HasForeignKey(l => l.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiscardedLead>(entity =>
        {
            entity.ToTable("DiscardedLeads");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.PlaceId).IsRequired().HasMaxLength(255);
            entity.Property(d => d.DisplayName).IsRequired().HasMaxLength(300);
            entity.Property(d => d.NormalizedName).IsRequired().HasMaxLength(300);
            entity.Property(d => d.DiscardReason).IsRequired().HasMaxLength(500);
            entity.Property(d => d.BusinessStatus).HasMaxLength(64);
            entity.Property(d => d.WebsiteUri).HasMaxLength(1000);

            entity.HasIndex(d => d.PlaceId).IsUnique();
            entity.HasIndex(d => new { d.CampaignId, d.NormalizedName });

            entity.HasOne(d => d.Campaign)
                .WithMany(c => c.DiscardedLeads)
                .HasForeignKey(d => d.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiCostLog>(entity =>
        {
            entity.ToTable("ApiCostLog");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Provider).IsRequired().HasMaxLength(64);
            entity.Property(a => a.Endpoint).IsRequired().HasMaxLength(128);
            entity.Property(a => a.QueryText).IsRequired().HasMaxLength(300);
            entity.Property(a => a.EstimatedCostUsd).HasPrecision(18, 6);

            entity.HasIndex(a => new { a.CampaignId, a.RequestedAtUtc });

            entity.HasOne(a => a.Campaign)
                .WithMany(c => c.ApiCostLogs)
                .HasForeignKey(a => a.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QueryProgress>(entity =>
        {
            entity.ToTable("QueryProgresses");
            entity.HasKey(q => q.Id);
            entity.Property(q => q.QueryText).IsRequired().HasMaxLength(300);
            entity.Property(q => q.NextPageToken).HasMaxLength(2000);

            entity.HasIndex(q => new { q.CampaignId, q.QueryText }).IsUnique();

            entity.HasOne(q => q.Campaign)
                .WithMany(c => c.QueryProgresses)
                .HasForeignKey(q => q.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DailyQuotaUsage>(entity =>
        {
            entity.ToTable("DailyQuotaUsage");
            entity.HasKey(q => q.Id);
            entity.Property(q => q.ServiceName).IsRequired().HasMaxLength(64);
            entity.HasIndex(q => new { q.ServiceName, q.DateUtc }).IsUnique();
        });
    }
}
