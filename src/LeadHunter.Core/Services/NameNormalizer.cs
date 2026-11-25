using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LeadHunter.Core.Services;

/// <summary>
/// Produces a normalized, accent-free, lowercased base business name
/// for franchise branch deduplication and keyword filtering.
/// </summary>
public static partial class NameNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = RemoveDiacritics(input).ToLowerInvariant().Trim();

        // Strip common branch/location separators (e.g., "OdontoCompany - Unidade Mauá" -> "odontocompany")
        text = BranchSuffixRegex().Replace(text, string.Empty);

        // Remove punctuation and collapse whitespace
        text = PunctuationRegex().Replace(text, " ");
        text = MultiSpaceRegex().Replace(text, " ").Trim();

        return text;
    }

    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s*[-|–—]\s*(unidade|filial|loja|matriz|centro|shopping|sp|rj|mg|ba|pr|rs|sc|pe|ce|df|go|es).*$", RegexOptions.IgnoreCase)]
    private static partial Regex BranchSuffixRegex();

    [GeneratedRegex(@"[^a-z0-9\s]")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
