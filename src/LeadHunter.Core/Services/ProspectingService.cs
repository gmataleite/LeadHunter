using LeadHunter.Core.Configuration;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Enums;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;

namespace LeadHunter.Core.Services;

/// <summary>
/// Orchestrates the complete LeadHunter pipeline:
/// 1. Google Places collection (idempotent + budget circuit breaker)
/// 2. Google Custom Search enrichment (Tier 1 first, then Tier 2 Instagram-only, capped at 95/day)
/// 3. Gemini 2.5 Flash structured extraction and niche validation (with token cost logging)
/// </summary>
public sealed class ProspectingService : IProspectingService
{
    private const string GoogleSearchQuotaService = "GoogleCustomSearch";

    private readonly IGooglePlacesClient _placesClient;
    private readonly IGoogleSearchEnricher _searchEnricher;
    private readonly IGeminiLeadAnalyzer _geminiAnalyzer;
    private readonly IGoogleSheetsExporter _sheetsExporter;
    private readonly ILeadHunterRepository _repository;
    private readonly LeadClassifier _classifier;

    public ProspectingService(
        IGooglePlacesClient placesClient,
        IGoogleSearchEnricher searchEnricher,
        IGeminiLeadAnalyzer geminiAnalyzer,
        IGoogleSheetsExporter sheetsExporter,
        ILeadHunterRepository repository,
        LeadClassifier classifier)
    {
        _placesClient = placesClient;
        _searchEnricher = searchEnricher;
        _geminiAnalyzer = geminiAnalyzer;
        _sheetsExporter = sheetsExporter;
        _repository = repository;
        _classifier = classifier;
    }

    public async Task<ProspectingExecutionSummary> ExecuteAsync(
        CampaignConfig config,
        string configPath,
        bool isPreview,
        bool isResume,
        Action<string>? onStatusMessage = null,
        CancellationToken cancellationToken = default)
    {
        config.Validate();

        var campaign = await _repository.GetOrCreateCampaignAsync(config, configPath, cancellationToken);
        campaign.Status = CampaignStatus.Running;
        campaign.LastRunAtUtc = DateTime.UtcNow;
        campaign.MaxBudgetUsd = config.Limits.MaxBudgetUsd;
        await _repository.UpdateCampaignAsync(campaign, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var dailyQuota = await _repository.GetOrCreateDailyQuotaAsync(
            GoogleSearchQuotaService,
            todayUtc,
            _searchEnricher.DailySafeLimit,
            cancellationToken);

        var summary = new ProspectingExecutionSummary
        {
            CampaignName = config.CampaignName,
            Mode = isPreview ? "PREVIEW" : (isResume ? "RESUME" : "RUN"),
            TargetValidLeads = config.Limits.TargetValidLeads,
            MaxBudgetUsd = config.Limits.MaxBudgetUsd,
            QuotaUsedToday = dailyQuota.QueriesUsed,
            QuotaDailyLimit = dailyQuota.DailyLimit
        };

        var totalSpentUsd = await _repository.GetTotalSpentUsdAsync(campaign.Id, cancellationToken);
        var existingValidLeads = await _repository.GetValidLeadsCountAsync(campaign.Id, cancellationToken);
        var targetForThisRun = isPreview ? config.Limits.PreviewBatchSize : config.Limits.TargetValidLeads;

        var sessionBranchCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (isPreview)
            {
                await RunPreviewPipelineAsync(
                    campaign,
                    config,
                    dailyQuota,
                    summary,
                    sessionBranchCounts,
                    totalSpentUsd,
                    targetForThisRun,
                    onStatusMessage,
                    cancellationToken);
            }
            else
            {
                totalSpentUsd = await RunPlacesCollectionPhaseAsync(
                    campaign,
                    config,
                    summary,
                    sessionBranchCounts,
                    totalSpentUsd,
                    existingValidLeads,
                    targetForThisRun,
                    onStatusMessage,
                    cancellationToken);

                totalSpentUsd = await RunEnrichmentPhaseAsync(
                    campaign,
                    dailyQuota,
                    summary,
                    totalSpentUsd,
                    onStatusMessage,
                    cancellationToken);

                totalSpentUsd = await RunGeminiAnalysisPhaseAsync(
                    campaign,
                    config,
                    summary,
                    totalSpentUsd,
                    onStatusMessage,
                    cancellationToken);
            }

            summary.QuotaUsedToday = dailyQuota.QueriesUsed;
            summary.TotalCampaignSpentUsd = await _repository.GetTotalSpentUsdAsync(campaign.Id, cancellationToken);

            // Compute cost projection for TargetValidLeads
            var effectiveConfirmedLeads = summary.ConfirmedMobileLeadsCount > 0
                ? summary.ConfirmedMobileLeadsCount
                : await _repository.GetConfirmedMobileLeadsCountAsync(campaign.Id, cancellationToken);

            if (effectiveConfirmedLeads > 0)
            {
                var costBasis = summary.SessionSpentUsd > 0 && summary.ConfirmedMobileLeadsCount > 0
                    ? summary.SessionSpentUsd / summary.ConfirmedMobileLeadsCount
                    : summary.TotalCampaignSpentUsd / effectiveConfirmedLeads;

                summary.ProjectedCostForTargetUsd = Math.Round(costBasis * config.Limits.TargetValidLeads, 4);
            }

            campaign.Status = summary.StoppedByBudgetCircuitBreaker
                ? CampaignStatus.PausedByBudget
                : (summary.EnrichmentPausedByDailyQuota ? CampaignStatus.PausedByQuota : CampaignStatus.Completed);
            campaign.LastRunAtUtc = DateTime.UtcNow;
            await _repository.UpdateCampaignAsync(campaign, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Etapa 3: Exportação para Google Sheets com deduplicação por PlaceId e PrimaryPhone
            await RunGoogleSheetsExportPhaseAsync(campaign, summary, onStatusMessage, cancellationToken);
        }
        catch
        {
            campaign.Status = CampaignStatus.Failed;
            campaign.LastRunAtUtc = DateTime.UtcNow;
            await _repository.UpdateCampaignAsync(campaign, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }

        return summary;
    }

    private async Task RunPreviewPipelineAsync(
        Campaign campaign,
        CampaignConfig config,
        DailyQuotaUsage dailyQuota,
        ProspectingExecutionSummary summary,
        Dictionary<string, int> sessionBranchCounts,
        decimal totalSpentUsd,
        int previewTargetWithMobile,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        foreach (var rawQuery in config.SearchQueries)
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
            {
                continue;
            }

            var query = rawQuery.Trim();
            var progress = await _repository.GetOrCreateQueryProgressAsync(campaign.Id, query, cancellationToken);

            if (progress.IsCompleted)
            {
                onStatusMessage?.Invoke($"Pulando query já concluída: '{query}'");
                continue;
            }

            var pageToken = progress.NextPageToken;
            var pageNumber = progress.PagesFetched;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (summary.ConfirmedMobileLeadsCount >= previewTargetWithMobile)
                {
                    summary.StoppedByTargetLimit = true;
                    break;
                }

                // Circuit Breaker de Orçamento
                var nextEstimatedCost = _placesClient.EstimatedCostPerRequestUsd;
                if (totalSpentUsd + nextEstimatedCost > config.Limits.MaxBudgetUsd)
                {
                    summary.StoppedByBudgetCircuitBreaker = true;
                    onStatusMessage?.Invoke(
                        $"CIRCUIT BREAKER ATIVADO: gasto acumulado (${totalSpentUsd:0.000}) + próxima chamada (${nextEstimatedCost:0.000}) excede o teto de ${config.Limits.MaxBudgetUsd:0.000}.");
                    break;
                }

                pageNumber++;
                onStatusMessage?.Invoke($"[Places] Consultando '{query}' (página {pageNumber})...");

                var searchResult = await _placesClient.SearchTextAsync(query, pageToken, cancellationToken);

                await LogApiCostAsync(
                    campaign.Id,
                    "GooglePlaces",
                    "places:searchText",
                    query,
                    pageNumber,
                    searchResult.Places.Count,
                    searchResult.EstimatedCostUsd,
                    searchResult.HttpStatusCode,
                    cancellationToken: cancellationToken);

                totalSpentUsd += searchResult.EstimatedCostUsd;
                summary.SessionSpentUsd += searchResult.EstimatedCostUsd;
                summary.ApiCallsMade++;

                var pageLeads = await ProcessPlacesPageAsync(
                    campaign,
                    config,
                    query,
                    searchResult.Places,
                    sessionBranchCounts,
                    summary,
                    cancellationToken);

                progress.PagesFetched = pageNumber;
                progress.NextPageToken = searchResult.NextPageToken;
                progress.IsCompleted = string.IsNullOrWhiteSpace(searchResult.NextPageToken);
                progress.UpdatedAtUtc = DateTime.UtcNow;

                await _repository.UpdateQueryProgressAsync(progress, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                // Enrich + Analyze leads from this page immediately so Preview stops as soon as PreviewBatchSize confirmed-mobile leads are reached
                var orderedForPreview = pageLeads
                    .OrderBy(l => l.Tier)
                    .ToList();

                foreach (var lead in orderedForPreview)
                {
                    if (summary.ConfirmedMobileLeadsCount >= previewTargetWithMobile)
                    {
                        summary.StoppedByTargetLimit = true;
                        break;
                    }

                    totalSpentUsd = await EnrichSingleLeadAsync(
                        campaign,
                        lead,
                        dailyQuota,
                        summary,
                        totalSpentUsd,
                        onStatusMessage,
                        cancellationToken);

                    if (lead.EnrichmentStatus == EnrichmentStatus.PendingQuota)
                    {
                        continue;
                    }

                    totalSpentUsd = await AnalyzeSingleLeadAsync(
                        campaign,
                        lead,
                        config,
                        summary,
                        totalSpentUsd,
                        onStatusMessage,
                        cancellationToken);

                    if (lead.EnrichmentStatus != EnrichmentStatus.DiscardedByNiche)
                    {
                        summary.ProcessedLeads.Add(lead);
                        if (!string.IsNullOrWhiteSpace(lead.PrimaryPhone))
                        {
                            summary.ConfirmedMobileLeadsCount++;
                        }
                    }
                }

                if (summary.StoppedByTargetLimit || summary.StoppedByBudgetCircuitBreaker || progress.IsCompleted)
                {
                    break;
                }

                pageToken = searchResult.NextPageToken;
            }

            if (summary.StoppedByTargetLimit || summary.StoppedByBudgetCircuitBreaker)
            {
                break;
            }
        }
    }

    private async Task<decimal> RunPlacesCollectionPhaseAsync(
        Campaign campaign,
        CampaignConfig config,
        ProspectingExecutionSummary summary,
        Dictionary<string, int> sessionBranchCounts,
        decimal totalSpentUsd,
        int existingValidLeads,
        int targetValidLeads,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        foreach (var rawQuery in config.SearchQueries)
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
            {
                continue;
            }

            var query = rawQuery.Trim();
            var progress = await _repository.GetOrCreateQueryProgressAsync(campaign.Id, query, cancellationToken);

            if (progress.IsCompleted)
            {
                onStatusMessage?.Invoke($"Pulando query já concluída: '{query}'");
                continue;
            }

            var pageToken = progress.NextPageToken;
            var pageNumber = progress.PagesFetched;

            while (!cancellationToken.IsCancellationRequested)
            {
                if ((existingValidLeads + summary.ValidLeadsAdded) >= targetValidLeads)
                {
                    summary.StoppedByTargetLimit = true;
                    break;
                }

                var nextEstimatedCost = _placesClient.EstimatedCostPerRequestUsd;
                if (totalSpentUsd + nextEstimatedCost > config.Limits.MaxBudgetUsd)
                {
                    summary.StoppedByBudgetCircuitBreaker = true;
                    onStatusMessage?.Invoke(
                        $"CIRCUIT BREAKER ATIVADO: gasto acumulado (${totalSpentUsd:0.000}) + próxima chamada (${nextEstimatedCost:0.000}) excede o teto de ${config.Limits.MaxBudgetUsd:0.000}.");
                    break;
                }

                pageNumber++;
                onStatusMessage?.Invoke($"[Places] Consultando '{query}' (página {pageNumber})...");

                var searchResult = await _placesClient.SearchTextAsync(query, pageToken, cancellationToken);

                await LogApiCostAsync(
                    campaign.Id,
                    "GooglePlaces",
                    "places:searchText",
                    query,
                    pageNumber,
                    searchResult.Places.Count,
                    searchResult.EstimatedCostUsd,
                    searchResult.HttpStatusCode,
                    cancellationToken: cancellationToken);

                totalSpentUsd += searchResult.EstimatedCostUsd;
                summary.SessionSpentUsd += searchResult.EstimatedCostUsd;
                summary.ApiCallsMade++;

                await ProcessPlacesPageAsync(
                    campaign,
                    config,
                    query,
                    searchResult.Places,
                    sessionBranchCounts,
                    summary,
                    cancellationToken,
                    maxLeadsRemaining: Math.Max(1, targetValidLeads - (existingValidLeads + summary.ValidLeadsAdded)));

                progress.PagesFetched = pageNumber;
                progress.NextPageToken = searchResult.NextPageToken;
                progress.IsCompleted = string.IsNullOrWhiteSpace(searchResult.NextPageToken);
                progress.UpdatedAtUtc = DateTime.UtcNow;

                await _repository.UpdateQueryProgressAsync(progress, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                if ((existingValidLeads + summary.ValidLeadsAdded) >= targetValidLeads)
                {
                    summary.StoppedByTargetLimit = true;
                    break;
                }

                if (progress.IsCompleted)
                {
                    break;
                }

                pageToken = searchResult.NextPageToken;
            }

            if (summary.StoppedByTargetLimit || summary.StoppedByBudgetCircuitBreaker)
            {
                break;
            }
        }

        return totalSpentUsd;
    }

    private async Task<List<Lead>> ProcessPlacesPageAsync(
        Campaign campaign,
        CampaignConfig config,
        string query,
        IReadOnlyList<PlaceCandidate> places,
        Dictionary<string, int> sessionBranchCounts,
        ProspectingExecutionSummary summary,
        CancellationToken cancellationToken,
        int? maxLeadsRemaining = null)
    {
        var addedLeads = new List<Lead>();

        foreach (var place in places)
        {
            if (string.IsNullOrWhiteSpace(place.PlaceId))
            {
                continue;
            }

            if (await _repository.PlaceExistsAsync(place.PlaceId, cancellationToken))
            {
                summary.SkippedDuplicates++;
                continue;
            }

            var normalizedName = NameNormalizer.Normalize(place.DisplayName);
            var branchCount = 0;
            if (!string.IsNullOrEmpty(normalizedName))
            {
                if (!sessionBranchCounts.TryGetValue(normalizedName, out branchCount))
                {
                    branchCount = await _repository.GetNormalizedNameCountAsync(campaign.Id, normalizedName, cancellationToken);
                }
            }

            var classification = _classifier.Classify(place, config, branchCount);

            if (!string.IsNullOrEmpty(normalizedName))
            {
                sessionBranchCounts[normalizedName] = branchCount + 1;
            }

            if (classification.IsQualified && classification.Tier is not null)
            {
                var (primaryPhone, secondaryPhone) = WebDataExtractor.ComposePhones(
                    classification.Phone.MobilePhone,
                    classification.Phone.LandlinePhone,
                    [],
                    []);

                string? igHandle = null;
                string? igUrl = null;
                if (WebDataExtractor.IsInstagramUrl(place.WebsiteUri))
                {
                    var extractedFromSite = WebDataExtractor.ExtractFromTexts([place.WebsiteUri], []);
                    igHandle = extractedFromSite.InstagramHandle;
                    igUrl = extractedFromSite.InstagramUrl ?? place.WebsiteUri;
                }

                var lead = new Lead
                {
                    CampaignId = campaign.Id,
                    PlaceId = place.PlaceId,
                    DisplayName = place.DisplayName,
                    NormalizedName = classification.NormalizedName,
                    FormattedAddress = place.FormattedAddress,
                    City = CityExtractor.Extract(place.FormattedAddress, query),
                    Rating = place.Rating,
                    UserRatingCount = place.UserRatingCount,
                    BusinessStatus = place.BusinessStatus,
                    RawPhoneNumber = place.NationalPhoneNumber,
                    MobilePhone = classification.Phone.MobilePhone,
                    LandlinePhone = classification.Phone.LandlinePhone,
                    PrimaryPhone = primaryPhone,
                    SecondaryPhone = secondaryPhone,
                    InstagramHandle = igHandle,
                    InstagramUrl = igUrl,
                    WebsiteUri = place.WebsiteUri,
                    GoogleMapsUri = place.GoogleMapsUri,
                    ReviewsJson = place.ReviewsJson,
                    Tier = classification.Tier.Value,
                    EnrichmentStatus = EnrichmentStatus.Pending,
                    IsExported = false,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await _repository.AddLeadAsync(lead, cancellationToken);
                summary.ValidLeadsAdded++;
                summary.NewLeads.Add(lead);
                addedLeads.Add(lead);

                if (maxLeadsRemaining.HasValue && addedLeads.Count >= maxLeadsRemaining.Value)
                {
                    break;
                }
            }
            else
            {
                var reason = classification.DiscardReason ?? "Descartado pelo filtro de qualidade";
                await RecordDiscardAsync(campaign.Id, place, classification.NormalizedName, reason, summary, cancellationToken);
            }
        }

        return addedLeads;
    }

    private async Task<decimal> RunEnrichmentPhaseAsync(
        Campaign campaign,
        DailyQuotaUsage dailyQuota,
        ProspectingExecutionSummary summary,
        decimal totalSpentUsd,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        var pendingLeads = await _repository.GetLeadsPendingEnrichmentAsync(campaign.Id, cancellationToken);
        if (pendingLeads.Count == 0)
        {
            return totalSpentUsd;
        }

        // Backfill City / PrimaryPhone / SecondaryPhone / InstagramHandle for leads collected in Etapa 1
        foreach (var lead in pendingLeads)
        {
            lead.City ??= CityExtractor.Extract(lead.FormattedAddress);
            lead.PrimaryPhone ??= lead.MobilePhone;
            lead.SecondaryPhone ??= lead.LandlinePhone;
            if (string.IsNullOrWhiteSpace(lead.InstagramHandle) && WebDataExtractor.IsInstagramUrl(lead.WebsiteUri))
            {
                var extracted = WebDataExtractor.ExtractFromTexts([lead.WebsiteUri], []);
                lead.InstagramHandle = extracted.InstagramHandle;
                lead.InstagramUrl ??= extracted.InstagramUrl ?? lead.WebsiteUri;
            }
        }

        // 1. Strict order: Tier 1 FIRST
        var tier1Leads = pendingLeads
            .Where(l => l.Tier == LeadTier.Tier1HighPriority)
            .ToList();

        // 2. Then Tier 2 leads with Instagram on Maps but no confirmed mobile phone
        var tier2InstaNoMobile = pendingLeads
            .Where(l => l.Tier == LeadTier.Tier2MediumPriority
                        && string.IsNullOrWhiteSpace(l.PrimaryPhone)
                        && WebDataExtractor.IsInstagramUrl(l.WebsiteUri))
            .ToList();

        // 3. Other leads that already have mobile or do not require Google Custom Search
        var noSearchNeeded = pendingLeads
            .Except(tier1Leads)
            .Except(tier2InstaNoMobile)
            .ToList();

        foreach (var lead in noSearchNeeded)
        {
            lead.EnrichmentStatus = EnrichmentStatus.Enriched;
            lead.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.UpdateLeadAsync(lead, cancellationToken);
        }

        if (noSearchNeeded.Count > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }

        if (!_searchEnricher.IsConfigured)
        {
            onStatusMessage?.Invoke("[Search] Google Custom Search não configurado (GOOGLE_SEARCH_API_KEY / GOOGLE_SEARCH_ENGINE_ID). Pulando enriquecimento web.");
            return totalSpentUsd;
        }

        // Execute Tier 1 completely before touching Tier 2
        foreach (var lead in tier1Leads)
        {
            if (cancellationToken.IsCancellationRequested) break;

            totalSpentUsd = await EnrichSingleLeadAsync(
                campaign,
                lead,
                dailyQuota,
                summary,
                totalSpentUsd,
                onStatusMessage,
                cancellationToken);
        }

        // Only process Tier 2 Instagram-only leads if daily quota is still available (< 95)
        foreach (var lead in tier2InstaNoMobile)
        {
            if (cancellationToken.IsCancellationRequested) break;

            totalSpentUsd = await EnrichSingleLeadAsync(
                campaign,
                lead,
                dailyQuota,
                summary,
                totalSpentUsd,
                onStatusMessage,
                cancellationToken);
        }

        return totalSpentUsd;
    }

    private async Task<decimal> EnrichSingleLeadAsync(
        Campaign campaign,
        Lead lead,
        DailyQuotaUsage dailyQuota,
        ProspectingExecutionSummary summary,
        decimal totalSpentUsd,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        var needsTier1Search = lead.Tier == LeadTier.Tier1HighPriority;
        var needsTier2Search = lead.Tier == LeadTier.Tier2MediumPriority
                               && string.IsNullOrWhiteSpace(lead.PrimaryPhone)
                               && WebDataExtractor.IsInstagramUrl(lead.WebsiteUri);

        if (!needsTier1Search && !needsTier2Search)
        {
            lead.EnrichmentStatus = EnrichmentStatus.Enriched;
            lead.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.UpdateLeadAsync(lead, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return totalSpentUsd;
        }

        if (!_searchEnricher.IsConfigured)
        {
            return totalSpentUsd;
        }

        // Rigid Daily Quota Guard (95/day)
        if (dailyQuota.QueriesUsed >= dailyQuota.DailyLimit)
        {
            summary.EnrichmentPausedByDailyQuota = true;
            summary.PendingQuotaCount++;
            lead.EnrichmentStatus = EnrichmentStatus.PendingQuota;
            lead.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.UpdateLeadAsync(lead, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            onStatusMessage?.Invoke(
                $"[Search] Cota diária atingida ({dailyQuota.QueriesUsed}/{dailyQuota.DailyLimit}). Lead '{lead.DisplayName}' marcado como PendingQuota.");
            return totalSpentUsd;
        }

        onStatusMessage?.Invoke(
            $"[Search {(needsTier1Search ? "Tier 1" : "Tier 2")}] Enriquecendo '{lead.DisplayName}' ({dailyQuota.QueriesUsed + 1}/{dailyQuota.DailyLimit})...");

        var result = await _searchEnricher.EnrichAsync(lead, isTier2InstagramOnly: needsTier2Search, cancellationToken);

        dailyQuota.QueriesUsed++;
        dailyQuota.UpdatedAtUtc = DateTime.UtcNow;
        await _repository.UpdateDailyQuotaAsync(dailyQuota, cancellationToken);

        await LogApiCostAsync(
            campaign.Id,
            "GoogleCustomSearch",
            "customsearch/v1",
            result.QueryExecuted,
            1,
            result.ResultsCount,
            result.EstimatedCostUsd,
            result.HttpStatusCode,
            cancellationToken: cancellationToken);

        totalSpentUsd += result.EstimatedCostUsd;
        summary.SessionSpentUsd += result.EstimatedCostUsd;
        summary.SearchEnrichCallsMade++;

        if (needsTier1Search)
            summary.EnrichedTier1Count++;
        else
            summary.EnrichedTier2Count++;

        var (primaryPhone, secondaryPhone) = WebDataExtractor.ComposePhones(
            lead.PrimaryPhone ?? lead.MobilePhone,
            lead.LandlinePhone,
            result.DiscoveredMobilePhones,
            result.DiscoveredLandlines);

        lead.PrimaryPhone = primaryPhone;
        lead.SecondaryPhone = secondaryPhone;
        lead.MobilePhone ??= primaryPhone;
        lead.InstagramHandle ??= result.InstagramHandle;
        lead.InstagramUrl ??= result.InstagramUrl;
        lead.WebSnippetsJson = result.SnippetsJson;
        lead.EnrichmentStatus = EnrichmentStatus.Enriched;
        lead.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateLeadAsync(lead, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return totalSpentUsd;
    }

    private async Task<decimal> RunGeminiAnalysisPhaseAsync(
        Campaign campaign,
        CampaignConfig config,
        ProspectingExecutionSummary summary,
        decimal totalSpentUsd,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        var pendingAnalysis = await _repository.GetLeadsPendingAnalysisAsync(campaign.Id, cancellationToken);
        if (pendingAnalysis.Count == 0)
        {
            return totalSpentUsd;
        }

        if (!_geminiAnalyzer.IsConfigured)
        {
            onStatusMessage?.Invoke("[Gemini] GEMINI_API_KEY não configurada. Pulando análise inteligente.");
            foreach (var lead in pendingAnalysis)
            {
                summary.ProcessedLeads.Add(lead);
                if (!string.IsNullOrWhiteSpace(lead.PrimaryPhone))
                    summary.ConfirmedMobileLeadsCount++;
            }
            return totalSpentUsd;
        }

        foreach (var lead in pendingAnalysis)
        {
            if (cancellationToken.IsCancellationRequested) break;

            totalSpentUsd = await AnalyzeSingleLeadAsync(
                campaign,
                lead,
                config,
                summary,
                totalSpentUsd,
                onStatusMessage,
                cancellationToken);

            if (lead.EnrichmentStatus != EnrichmentStatus.DiscardedByNiche)
            {
                summary.ProcessedLeads.Add(lead);
                if (!string.IsNullOrWhiteSpace(lead.PrimaryPhone))
                {
                    summary.ConfirmedMobileLeadsCount++;
                }
            }
        }

        return totalSpentUsd;
    }

    private async Task<decimal> AnalyzeSingleLeadAsync(
        Campaign campaign,
        Lead lead,
        CampaignConfig config,
        ProspectingExecutionSummary summary,
        decimal totalSpentUsd,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        if (!_geminiAnalyzer.IsConfigured)
        {
            return totalSpentUsd;
        }

        onStatusMessage?.Invoke($"[Gemini] Analisando '{lead.DisplayName}'...");

        var analysis = await _geminiAnalyzer.AnalyzeAsync(lead, config, cancellationToken);

        await LogApiCostAsync(
            campaign.Id,
            "Gemini",
            $"{analysis.ModelName}:generateContent",
            lead.DisplayName,
            1,
            1,
            analysis.EstimatedCostUsd,
            analysis.HttpStatusCode,
            analysis.InputTokens,
            analysis.OutputTokens,
            analysis.TotalTokens,
            cancellationToken);

        totalSpentUsd += analysis.EstimatedCostUsd;
        summary.SessionSpentUsd += analysis.EstimatedCostUsd;
        summary.GeminiAnalyzedCount++;
        summary.TotalInputTokens += analysis.InputTokens;
        summary.TotalOutputTokens += analysis.OutputTokens;

        lead.IsTargetNiche = analysis.IsTargetNiche;
        lead.OwnerName = analysis.OwnerName;
        lead.OwnerConfidence = analysis.OwnerConfidence;
        lead.OwnerSource = analysis.OwnerSource;
        lead.ReviewerFirstName = analysis.ReviewerFirstName;
        lead.PraisedService = analysis.PraisedService;
        lead.AnalyzedAtUtc = DateTime.UtcNow;
        lead.UpdatedAtUtc = DateTime.UtcNow;

        if (!analysis.IsTargetNiche)
        {
            lead.EnrichmentStatus = EnrichmentStatus.DiscardedByNiche;
            summary.DiscardedByNicheCount++;
            summary.DiscardedCount++;

            const string nicheReason = "Fora do nicho da campanha (Gemini)";
            summary.DiscardBreakdown[nicheReason] = summary.DiscardBreakdown.GetValueOrDefault(nicheReason) + 1;

            await _repository.AddDiscardedLeadAsync(new DiscardedLead
            {
                CampaignId = campaign.Id,
                PlaceId = lead.PlaceId,
                DisplayName = lead.DisplayName,
                NormalizedName = lead.NormalizedName,
                DiscardReason = nicheReason,
                Rating = lead.Rating,
                UserRatingCount = lead.UserRatingCount,
                BusinessStatus = lead.BusinessStatus,
                WebsiteUri = lead.WebsiteUri,
                DiscardedAtUtc = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            lead.EnrichmentStatus = EnrichmentStatus.Completed;
        }

        await _repository.UpdateLeadAsync(lead, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return totalSpentUsd;
    }

    private async Task RecordDiscardAsync(
        int campaignId,
        PlaceCandidate place,
        string normalizedName,
        string reason,
        ProspectingExecutionSummary summary,
        CancellationToken cancellationToken)
    {
        var discarded = new DiscardedLead
        {
            CampaignId = campaignId,
            PlaceId = place.PlaceId,
            DisplayName = place.DisplayName,
            NormalizedName = normalizedName,
            DiscardReason = reason,
            Rating = place.Rating,
            UserRatingCount = place.UserRatingCount,
            BusinessStatus = place.BusinessStatus,
            WebsiteUri = place.WebsiteUri,
            DiscardedAtUtc = DateTime.UtcNow
        };

        await _repository.AddDiscardedLeadAsync(discarded, cancellationToken);
        summary.DiscardedCount++;

        var reasonKey = NormalizeReasonBucket(reason);
        summary.DiscardBreakdown[reasonKey] = summary.DiscardBreakdown.GetValueOrDefault(reasonKey) + 1;
    }

    private Task LogApiCostAsync(
        int campaignId,
        string provider,
        string endpoint,
        string queryText,
        int pageNumber,
        int resultsCount,
        decimal estimatedCostUsd,
        int httpStatusCode,
        int? inputTokens = null,
        int? outputTokens = null,
        int? totalTokens = null,
        CancellationToken cancellationToken = default) =>
        _repository.AddApiCostLogAsync(new ApiCostLog
        {
            CampaignId = campaignId,
            Provider = provider,
            Endpoint = endpoint,
            QueryText = queryText.Length > 300 ? queryText[..300] : queryText,
            PageNumber = pageNumber,
            ResultsCount = resultsCount,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = estimatedCostUsd,
            HttpStatusCode = httpStatusCode,
            RequestedAtUtc = DateTime.UtcNow
        }, cancellationToken);

    private async Task RunGoogleSheetsExportPhaseAsync(
        Campaign campaign,
        ProspectingExecutionSummary summary,
        Action<string>? onStatusMessage,
        CancellationToken cancellationToken)
    {
        var reports = await _repository.GetStatusReportsAsync(campaign.Name, cancellationToken);
        var auditReport = reports.FirstOrDefault();

        if (auditReport is not null)
        {
            summary.OwnersIdentifiedCount = auditReport.OwnersIdentifiedCount;
            if (summary.DiscardBreakdown.Count == 0 && auditReport.DiscardReasons.Count > 0)
            {
                foreach (var kv in auditReport.DiscardReasons)
                {
                    summary.DiscardBreakdown[kv.Key] = kv.Value;
                }
            }
        }

        if (!_sheetsExporter.IsConfigured)
        {
            summary.SheetsExportSkippedReason =
                "service-account.json não encontrado ou GOOGLE_SERVICE_ACCOUNT_PATH não configurado.";
            onStatusMessage?.Invoke($"[Sheets] Exportação pulada: {summary.SheetsExportSkippedReason}");
            return;
        }

        var candidateReadyLeads = await _repository.GetLeadsReadyForExportAsync(campaign.Id, cancellationToken);
        var pendingQuotaLeads = await _repository.GetLeadsPendingQuotaAsync(campaign.Id, cancellationToken);
        var alreadyExportedPhones = await _repository.GetExportedPrimaryPhonesAsync(cancellationToken);

        // Deduplicate by PrimaryPhone across ALL campaigns and within the current batch
        var deduplicatedLeads = new List<Lead>();
        var batchPhones = new HashSet<string>(alreadyExportedPhones, StringComparer.Ordinal);
        var batchPlaceIds = new HashSet<string>(StringComparer.Ordinal);

        // Strict order: no website first -> social/Instagram only -> structured site -> highest rating
        var orderedCandidates = candidateReadyLeads
            .OrderBy(GetWebsiteSortRank)
            .ThenByDescending(l => l.Rating ?? 0)
            .ThenByDescending(l => l.UserRatingCount ?? 0)
            .ToList();

        foreach (var lead in orderedCandidates)
        {
            var phone = lead.PrimaryPhone ?? lead.MobilePhone;
            if (string.IsNullOrWhiteSpace(phone))
            {
                continue;
            }

            if (!batchPlaceIds.Add(lead.PlaceId) || !batchPhones.Add(phone))
            {
                summary.SkippedExportDuplicatePhoneCount++;
                continue;
            }

            lead.PrimaryPhone = phone;
            deduplicatedLeads.Add(lead);
        }

        if (auditReport is null)
        {
            return;
        }

        onStatusMessage?.Invoke(
            $"[Sheets] Exportando {deduplicatedLeads.Count} lead(s) pronto(s) e {pendingQuotaLeads.Count} pendente(s) de cota...");

        var exportResult = await _sheetsExporter.ExportAsync(
            campaign,
            deduplicatedLeads,
            pendingQuotaLeads,
            auditReport,
            cancellationToken);

        var exportedAtUtc = DateTime.UtcNow;
        if (deduplicatedLeads.Count > 0)
        {
            await _repository.MarkLeadsAsExportedAsync(deduplicatedLeads, exportedAtUtc, cancellationToken);
        }

        campaign.SpreadsheetId = exportResult.SpreadsheetId;
        campaign.SpreadsheetUrl = exportResult.SpreadsheetUrl;
        await _repository.UpdateCampaignAsync(campaign, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        summary.SpreadsheetId = exportResult.SpreadsheetId;
        summary.SpreadsheetUrl = exportResult.SpreadsheetUrl;
        summary.ExportedLeadsCount = exportResult.ExportedLeadsCount;
        summary.ExportedLeads.AddRange(exportResult.ExportedLeads);
    }

    private static int GetWebsiteSortRank(Lead lead)
    {
        if (string.IsNullOrWhiteSpace(lead.WebsiteUri))
            return 0; // Sem site primeiro

        if (LeadClassifier.IsSocialOrLinkInBioUrl(lead.WebsiteUri))
            return 1; // Depois só Instagram / rede social

        return 2; // Site próprio por último
    }

    private static string NormalizeReasonBucket(string reason)
    {
        var colonIndex = reason.IndexOf(':');
        if (colonIndex > 0)
        {
            return reason[..colonIndex].Trim();
        }

        var parenIndex = reason.IndexOf('(');
        if (parenIndex > 0)
        {
            return reason[..parenIndex].Trim();
        }

        return reason;
    }
}
