using LeadHunter.Core.Configuration;
using LeadHunter.Core.Enums;
using LeadHunter.Core.Models;

namespace LeadHunter.Core.Services;

/// <summary>
/// Evaluates place candidates against campaign quality filters and assigns enrichment Tiers.
/// </summary>
public sealed class LeadClassifier
{
    private static readonly string[] SocialOrLinkInBioHosts =
    [
        "instagram.com",
        "wa.me",
        "whatsapp.com",
        "linktr.ee"
    ];

    public LeadClassificationResult Classify(
        PlaceCandidate candidate,
        CampaignConfig config,
        int existingBranchesWithSameNormalizedName)
    {
        var phone = PhoneNormalizer.Normalize(candidate.NationalPhoneNumber);
        var normalizedName = NameNormalizer.Normalize(candidate.DisplayName);

        // 1. Operational status
        if (!string.Equals(candidate.BusinessStatus, "OPERATIONAL", StringComparison.OrdinalIgnoreCase))
        {
            return Discard(
                $"BusinessStatus inválido: '{candidate.BusinessStatus ?? "N/A"}' (esperado OPERATIONAL)",
                phone,
                normalizedName);
        }

        // 2. ExcludeKeywords check (accent-insensitive against display name)
        var searchableName = NameNormalizer.RemoveDiacritics(candidate.DisplayName ?? string.Empty);
        foreach (var keyword in config.ExcludeKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            var normalizedKeyword = NameNormalizer.RemoveDiacritics(keyword.Trim());
            if (searchableName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return Discard($"Palavra-chave excluída encontrada: '{keyword}'", phone, normalizedName);
            }
        }

        // 3. Rating filter
        if (candidate.Rating is null || candidate.Rating.Value < config.Filters.MinRating)
        {
            return Discard(
                $"Rating abaixo do mínimo ({candidate.Rating?.ToString("0.0") ?? "N/A"} < {config.Filters.MinRating:0.0})",
                phone,
                normalizedName);
        }

        // 4. Reviews count bounds
        if (candidate.UserRatingCount is null
            || candidate.UserRatingCount.Value < config.Filters.MinReviews
            || candidate.UserRatingCount.Value > config.Filters.MaxReviews)
        {
            return Discard(
                $"Avaliações fora do intervalo ({candidate.UserRatingCount?.ToString() ?? "0"} não está entre {config.Filters.MinReviews} e {config.Filters.MaxReviews})",
                phone,
                normalizedName);
        }

        // 5. Franchise branch cap
        if (!string.IsNullOrEmpty(normalizedName) && existingBranchesWithSameNormalizedName >= config.MaxFranchiseBranches)
        {
            return Discard(
                $"Limite de franquia excedido (>{config.MaxFranchiseBranches} unidades para '{normalizedName}')",
                phone,
                normalizedName);
        }

        // 6. Tier Prioritization (Enrichment Queue)
        var hasWebsite = !string.IsNullOrWhiteSpace(candidate.WebsiteUri);
        var isSocialOnlyWebsite = hasWebsite && IsSocialOrLinkInBioUrl(candidate.WebsiteUri!);

        LeadTier tier;
        if (!hasWebsite && !phone.HasMobile)
        {
            // Tier 1 (Alta Prioridade): WebsiteUri vazio E sem celular identificado no Maps
            tier = LeadTier.Tier1HighPriority;
        }
        else if (!hasWebsite && phone.HasMobile)
        {
            // Tier 2 (Média Prioridade): WebsiteUri vazio COM celular no Maps
            tier = LeadTier.Tier2MediumPriority;
        }
        else if (isSocialOnlyWebsite)
        {
            // Tier 2 (Média Prioridade): WebsiteUri apontando apenas para instagram.com, wa.me, whatsapp.com ou linktr.ee
            tier = LeadTier.Tier2MediumPriority;
        }
        else
        {
            // Tier 3 (Baixa Prioridade / Descarte): Possui site próprio estruturado
            tier = LeadTier.Tier3LowPriority;
            if (!config.Filters.AllowHasWebsite)
            {
                return Discard(
                    $"Possui site próprio estruturado ({candidate.WebsiteUri})",
                    phone,
                    normalizedName);
            }
        }

        return new LeadClassificationResult(
            IsQualified: true,
            Tier: tier,
            DiscardReason: null,
            Phone: phone,
            NormalizedName: normalizedName);
    }

    public static bool IsSocialOrLinkInBioUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var url = rawUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        foreach (var domain in SocialOrLinkInBioHosts)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static LeadClassificationResult Discard(
        string reason,
        PhoneNormalizationResult phone,
        string normalizedName) =>
        new(false, null, reason, phone, normalizedName);
}
