using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;
using Microsoft.Extensions.Options;

namespace LeadHunter.Infrastructure.GooglePlaces;

public sealed class GooglePlacesClient : IGooglePlacesClient
{
    public const string StrictFieldMask =
        "places.id,places.displayName,places.formattedAddress,places.rating,places.userRatingCount,places.businessStatus,places.nationalPhoneNumber,places.websiteUri,places.googleMapsUri,places.reviews,nextPageToken";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly GooglePlacesOptions _options;

    public GooglePlacesClient(HttpClient httpClient, IOptions<GooglePlacesOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public decimal EstimatedCostPerRequestUsd => _options.EstimatedCostPerRequestUsd;

    public async Task<PlaceSearchResult> SearchTextAsync(
        string textQuery,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Chave da Google Places API não configurada. Defina GOOGLE_PLACES_API_KEY no arquivo .env, nas variáveis de ambiente ou em appsettings.json (GooglePlaces:ApiKey).");
        }

        var payload = new SearchTextRequest
        {
            TextQuery = textQuery,
            LanguageCode = _options.LanguageCode,
            RegionCode = _options.RegionCode,
            PageToken = string.IsNullOrWhiteSpace(pageToken) ? null : pageToken
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        request.Headers.Add("X-Goog-Api-Key", apiKey);
        request.Headers.Add("X-Goog-FieldMask", StrictFieldMask);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Google Places API retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {rawBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<SearchTextResponse>(rawBody, SerializerOptions);
        var candidates = new List<PlaceCandidate>();

        if (parsed?.Places is not null)
        {
            foreach (var p in parsed.Places)
            {
                if (string.IsNullOrWhiteSpace(p.Id))
                {
                    continue;
                }

                candidates.Add(new PlaceCandidate
                {
                    PlaceId = p.Id,
                    DisplayName = p.DisplayName?.Text ?? string.Empty,
                    FormattedAddress = p.FormattedAddress,
                    Rating = p.Rating,
                    UserRatingCount = p.UserRatingCount,
                    BusinessStatus = p.BusinessStatus,
                    NationalPhoneNumber = p.NationalPhoneNumber,
                    WebsiteUri = p.WebsiteUri,
                    GoogleMapsUri = p.GoogleMapsUri,
                    ReviewsJson = p.Reviews.ValueKind == JsonValueKind.Undefined || p.Reviews.ValueKind == JsonValueKind.Null
                        ? null
                        : p.Reviews.GetRawText()
                });
            }
        }

        return new PlaceSearchResult
        {
            Places = candidates,
            NextPageToken = parsed?.NextPageToken,
            HttpStatusCode = (int)response.StatusCode,
            EstimatedCostUsd = _options.EstimatedCostPerRequestUsd
        };
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey.Trim();
        }

        return Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY")?.Trim() ?? string.Empty;
    }

    private sealed class SearchTextRequest
    {
        public string TextQuery { get; init; } = string.Empty;
        public string? LanguageCode { get; init; }
        public string? RegionCode { get; init; }
        public string? PageToken { get; init; }
    }

    private sealed class SearchTextResponse
    {
        public List<PlaceDto>? Places { get; init; }
        public string? NextPageToken { get; init; }
    }

    private sealed class PlaceDto
    {
        public string? Id { get; init; }
        public LocalizedTextDto? DisplayName { get; init; }
        public string? FormattedAddress { get; init; }
        public double? Rating { get; init; }
        public int? UserRatingCount { get; init; }
        public string? BusinessStatus { get; init; }
        public string? NationalPhoneNumber { get; init; }
        public string? WebsiteUri { get; init; }
        public string? GoogleMapsUri { get; init; }
        public JsonElement Reviews { get; init; }
    }

    private sealed class LocalizedTextDto
    {
        public string? Text { get; init; }
        public string? LanguageCode { get; init; }
    }
}
