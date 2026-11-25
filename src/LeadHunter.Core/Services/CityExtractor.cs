using System.Text.RegularExpressions;

namespace LeadHunter.Core.Services;

/// <summary>
/// Extracts city names from Brazilian Google Maps formatted addresses or search queries.
/// </summary>
public static partial class CityExtractor
{
    public static string? Extract(string? formattedAddress, string? fallbackQuery = null)
    {
        if (!string.IsNullOrWhiteSpace(formattedAddress))
        {
            // Typical Brazilian Maps format: "Av. Barão de Mauá, 123 - Centro, Mauá - SP, 09310-000, Brasil"
            var match = AddressCityStateRegex().Match(formattedAddress);
            if (match.Success)
            {
                var city = match.Groups["city"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(city))
                {
                    return city;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackQuery))
        {
            // Fallback: "Dentista em Mauá SP" -> "Mauá"
            var queryMatch = QueryCityRegex().Match(fallbackQuery);
            if (queryMatch.Success)
            {
                var city = queryMatch.Groups["city"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(city))
                {
                    return city;
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@",\s*(?<city>[^,\-0-9]{2,60}?)\s*-\s*[A-Z]{2}\b")]
    private static partial Regex AddressCityStateRegex();

    [GeneratedRegex(@"\bem\s+(?<city>[\p{L}\s]{2,50}?)(?:\s+[A-Z]{2})?$", RegexOptions.IgnoreCase)]
    private static partial Regex QueryCityRegex();
}
