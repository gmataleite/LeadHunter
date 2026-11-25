using LeadHunter.Core.Enums;

namespace LeadHunter.Core.Entities;

public sealed class Lead
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string PlaceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? FormattedAddress { get; set; }
    public string? City { get; set; }
    public double? Rating { get; set; }
    public int? UserRatingCount { get; set; }
    public string? BusinessStatus { get; set; }
    public string? RawPhoneNumber { get; set; }
    public string? MobilePhone { get; set; }
    public string? LandlinePhone { get; set; }

    // Etapa 2: Dados consolidados de enriquecimento web
    public string? PrimaryPhone { get; set; }
    public string? SecondaryPhone { get; set; }
    public string? InstagramHandle { get; set; }
    public string? InstagramUrl { get; set; }
    public string? Cnpj { get; set; }
    public string? WebSnippetsJson { get; set; }
    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;

    // Etapa 2: Análise estruturada via Gemini
    public bool? IsTargetNiche { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerConfidence { get; set; }
    public string? OwnerSource { get; set; }
    public string? ReviewerFirstName { get; set; }
    public string? PraisedService { get; set; }
    public DateTime? AnalyzedAtUtc { get; set; }

    // Etapa 3: Exportação e deduplicação
    public bool IsExported { get; set; }
    public DateTime? ExportedAtUtc { get; set; }

    public string? WebsiteUri { get; set; }
    public string? GoogleMapsUri { get; set; }
    public string? ReviewsJson { get; set; }
    public LeadTier Tier { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
}
