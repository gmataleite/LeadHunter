namespace LeadHunter.Infrastructure.GooglePlaces;

public sealed class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://places.googleapis.com/v1/places:searchText";
    public string LanguageCode { get; set; } = "pt-BR";
    public string RegionCode { get; set; } = "BR";
    public decimal EstimatedCostPerRequestUsd { get; set; } = 0.040m;
}
