using System.Text.Json;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;
using LeadHunter.Core.Services;
using Microsoft.Extensions.Options;

namespace LeadHunter.Infrastructure.GoogleSearch;

public sealed class GoogleSearchEnricher : IGoogleSearchEnricher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly GoogleSearchOptions _options;

    public GoogleSearchEnricher(HttpClient httpClient, IOptions<GoogleSearchOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public int DailySafeLimit => _options.DailySafeLimit;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ResolveApiKey()) &&
        !string.IsNullOrWhiteSpace(ResolveSearchEngineId());

    public async Task<WebEnrichmentResult> EnrichAsync(
        Lead lead,
        bool isTier2InstagramOnly,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        var cx = ResolveSearchEngineId();

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(cx))
        {
            throw new InvalidOperationException(
                "Google Custom Search não configurado. Defina GOOGLE_SEARCH_API_KEY e GOOGLE_SEARCH_ENGINE_ID no .env ou em appsettings.json (GoogleSearch).");
        }

        var query = BuildQuery(lead.DisplayName, lead.City, isTier2InstagramOnly);
        var url = $"{_options.BaseUrl}" +
                  $"?key={Uri.EscapeDataString(apiKey)}" +
                  $"&cx={Uri.EscapeDataString(cx)}" +
                  $"&q={Uri.EscapeDataString(query)}" +
                  $"&num={Math.Clamp(_options.MaxResultsPerQuery, 1, 10)}" +
                  "&gl=br&hl=pt-BR";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Google Custom Search API retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {rawBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<CustomSearchResponse>(rawBody, JsonOptions);
        var items = parsed?.Items ?? [];

        var links = new List<string?>();
        var texts = new List<string?>();
        var snippetDtos = new List<SearchSnippetDto>();

        foreach (var item in items)
        {
            links.Add(item.Link);
            texts.Add(item.Title);
            texts.Add(item.Snippet);

            var metaDescription = ExtractOgDescription(item.Pagemap);
            if (!string.IsNullOrWhiteSpace(metaDescription))
            {
                texts.Add(metaDescription);
            }

            snippetDtos.Add(new SearchSnippetDto(
                item.Title,
                item.Link,
                string.IsNullOrWhiteSpace(metaDescription)
                    ? item.Snippet
                    : $"{item.Snippet} | {metaDescription}"));
        }

        var extracted = WebDataExtractor.ExtractFromTexts(links, texts);
        var snippetsJson = snippetDtos.Count > 0
            ? JsonSerializer.Serialize(snippetDtos)
            : null;

        return new WebEnrichmentResult
        {
            QueryExecuted = query,
            InstagramHandle = extracted.InstagramHandle,
            InstagramUrl = extracted.InstagramUrl,
            DiscoveredMobilePhones = extracted.MobilePhones,
            DiscoveredLandlines = extracted.LandlinePhones,
            SnippetsJson = snippetsJson,
            ResultsCount = items.Count,
            HttpStatusCode = (int)response.StatusCode,
            EstimatedCostUsd = _options.EstimatedCostPerRequestUsd
        };
    }

    public static string BuildQuery(string displayName, string? city, bool isTier2InstagramOnly)
    {
        var cleanName = displayName.Replace("\"", string.Empty).Trim();
        var cleanCity = city?.Replace("\"", string.Empty).Trim();

        if (isTier2InstagramOnly)
        {
            return string.IsNullOrWhiteSpace(cleanCity)
                ? $"site:instagram.com \"{cleanName}\""
                : $"site:instagram.com \"{cleanName}\" \"{cleanCity}\"";
        }

        return string.IsNullOrWhiteSpace(cleanCity)
            ? $"\"{cleanName}\" instagram OR whatsapp"
            : $"\"{cleanName}\" \"{cleanCity}\" instagram OR whatsapp";
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return _options.ApiKey.Trim();

        return Environment.GetEnvironmentVariable("GOOGLE_SEARCH_API_KEY")?.Trim() ?? string.Empty;
    }

    private string ResolveSearchEngineId()
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchEngineId))
            return _options.SearchEngineId.Trim();

        return Environment.GetEnvironmentVariable("GOOGLE_SEARCH_ENGINE_ID")?.Trim() ?? string.Empty;
    }

    private static string? ExtractOgDescription(JsonElement pagemap)
    {
        if (pagemap.ValueKind != JsonValueKind.Object)
            return null;

        if (!pagemap.TryGetProperty("metatags", out var metatags) || metatags.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var tag in metatags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.Object)
                continue;

            if (tag.TryGetProperty("og:description", out var ogDesc) && ogDesc.ValueKind == JsonValueKind.String)
            {
                var value = ogDesc.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private sealed record SearchSnippetDto(string? Title, string? Link, string? Snippet);

    private sealed class CustomSearchResponse
    {
        public List<SearchItemDto>? Items { get; init; }
    }

    private sealed class SearchItemDto
    {
        public string? Title { get; init; }
        public string? Link { get; init; }
        public string? Snippet { get; init; }
        public JsonElement Pagemap { get; init; }
    }
}
