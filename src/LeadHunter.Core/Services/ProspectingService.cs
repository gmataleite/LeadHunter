using LeadHunter.Core.Configuration;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Enums;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;

namespace LeadHunter.Core.Services;

/// <summary>
/// Orchestrates Google Places prospecting with idempotency, quality filtering,
/// franchise deduplication, tier classification, and strict budget circuit breaking.
/// </summary>
public sealed class ProspectingService : IProspectingService
{
    private readonly IGooglePlacesClient _placesClient;
    private readonly ILeadHunterRepository _repository;
    private readonly LeadClassifier _classifier;

    public ProspectingService(
        IGooglePlacesClient placesClient,
        ILeadHunterRepository repository,
        LeadClassifier classifier)
    {
        _placesClient = placesClient;
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

        var summary = new ProspectingExecutionSummary
        {
            CampaignName = config.CampaignName,
            Mode = isPreview ? "PREVIEW" : (isResume ? "RESUME" : "RUN"),
            MaxBudgetUsd = config.Limits.MaxBudgetUsd
        };

        var totalSpentUsd = await _repository.GetTotalSpentUsdAsync(campaign.Id, cancellationToken);
        var existingValidLeads = await _repository.GetValidLeadsCountAsync(campaign.Id, cancellationToken);
        var targetForThisRun = isPreview ? config.Limits.PreviewBatchSize : config.Limits.TargetValidLeads;

        // Track in-session normalized name counts so multiple branches within the same page/session are caught accurately
        var sessionBranchCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
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
                    // Check target lead limit before spending API budget
                    if (isPreview && summary.ValidLeadsAdded >= targetForThisRun)
                    {
                        summary.StoppedByTargetLimit = true;
                        break;
                    }

                    if (!isPreview && (existingValidLeads + summary.ValidLeadsAdded) >= targetForThisRun)
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
                    onStatusMessage?.Invoke($"Consultando Google Places: '{query}' (página {pageNumber})...");

                    var searchResult = await _placesClient.SearchTextAsync(query, pageToken, cancellationToken);

                    // Log API cost immediately for strict accounting
                    var costLog = new ApiCostLog
                    {
                        CampaignId = campaign.Id,
                        Endpoint = "places:searchText",
                        QueryText = query,
                        PageNumber = pageNumber,
                        ResultsCount = searchResult.Places.Count,
                        EstimatedCostUsd = searchResult.EstimatedCostUsd,
                        HttpStatusCode = searchResult.HttpStatusCode,
                        RequestedAtUtc = DateTime.UtcNow
                    };

                    await _repository.AddApiCostLogAsync(costLog, cancellationToken);
                    totalSpentUsd += searchResult.EstimatedCostUsd;
                    summary.SessionSpentUsd += searchResult.EstimatedCostUsd;
                    summary.ApiCallsMade++;

                    foreach (var place in searchResult.Places)
                    {
                        if (string.IsNullOrWhiteSpace(place.PlaceId))
                        {
                            continue;
                        }

                        // Idempotency: never process/insert the same PlaceId twice
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
                            var lead = new Lead
                            {
                                CampaignId = campaign.Id,
                                PlaceId = place.PlaceId,
                                DisplayName = place.DisplayName,
                                NormalizedName = classification.NormalizedName,
                                FormattedAddress = place.FormattedAddress,
                                Rating = place.Rating,
                                UserRatingCount = place.UserRatingCount,
                                BusinessStatus = place.BusinessStatus,
                                RawPhoneNumber = place.NationalPhoneNumber,
                                MobilePhone = classification.Phone.MobilePhone,
                                LandlinePhone = classification.Phone.LandlinePhone,
                                WebsiteUri = place.WebsiteUri,
                                GoogleMapsUri = place.GoogleMapsUri,
                                ReviewsJson = place.ReviewsJson,
                                Tier = classification.Tier.Value,
                                IsExported = false,
                                CreatedAtUtc = DateTime.UtcNow,
                                UpdatedAtUtc = DateTime.UtcNow
                            };

                            await _repository.AddLeadAsync(lead, cancellationToken);
                            summary.ValidLeadsAdded++;
                            summary.NewLeads.Add(lead);

                            if ((isPreview && summary.ValidLeadsAdded >= targetForThisRun) ||
                                (!isPreview && (existingValidLeads + summary.ValidLeadsAdded) >= targetForThisRun))
                            {
                                summary.StoppedByTargetLimit = true;
                                break;
                            }
                        }
                        else
                        {
                            var reason = classification.DiscardReason ?? "Descartado pelo filtro de qualidade";
                            var discarded = new DiscardedLead
                            {
                                CampaignId = campaign.Id,
                                PlaceId = place.PlaceId,
                                DisplayName = place.DisplayName,
                                NormalizedName = classification.NormalizedName,
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
                    }

                    // Persist progress after every page so resume is lossless
                    progress.PagesFetched = pageNumber;
                    progress.NextPageToken = searchResult.NextPageToken;
                    progress.IsCompleted = string.IsNullOrWhiteSpace(searchResult.NextPageToken);
                    progress.UpdatedAtUtc = DateTime.UtcNow;

                    await _repository.UpdateQueryProgressAsync(progress, cancellationToken);
                    await _repository.SaveChangesAsync(cancellationToken);

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

            campaign.Status = summary.StoppedByBudgetCircuitBreaker
                ? CampaignStatus.PausedByBudget
                : CampaignStatus.Completed;
            campaign.LastRunAtUtc = DateTime.UtcNow;
            await _repository.UpdateCampaignAsync(campaign, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            campaign.Status = CampaignStatus.Failed;
            campaign.LastRunAtUtc = DateTime.UtcNow;
            await _repository.UpdateCampaignAsync(campaign, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }

        summary.TotalCampaignSpentUsd = totalSpentUsd;
        return summary;
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
