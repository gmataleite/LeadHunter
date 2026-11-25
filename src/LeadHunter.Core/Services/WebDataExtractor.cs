using System.Text.RegularExpressions;

namespace LeadHunter.Core.Services;

public sealed record ExtractedWebData(
    string? InstagramHandle,
    string? InstagramUrl,
    IReadOnlyList<string> MobilePhones,
    IReadOnlyList<string> LandlinePhones);

/// <summary>
/// Extracts Instagram @handles, wa.me links, and Brazilian phone numbers from web search results.
/// </summary>
public static partial class WebDataExtractor
{
    private static readonly HashSet<string> ReservedInstagramPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "reel", "reels", "stories", "explore", "tv", "accounts", "direct",
        "about", "legal", "directory", "channel", "developer", "graphql", "api"
    };

    public static ExtractedWebData ExtractFromTexts(IEnumerable<string?> links, IEnumerable<string?> texts)
    {
        string? instagramHandle = null;
        string? instagramUrl = null;
        var mobiles = new List<string>();
        var landlines = new List<string>();
        var seenMobiles = new HashSet<string>(StringComparer.Ordinal);
        var seenLandlines = new HashSet<string>(StringComparer.Ordinal);

        void CollectPhone(string rawCandidate)
        {
            var normalized = PhoneNormalizer.Normalize(rawCandidate);
            if (normalized.HasMobile && seenMobiles.Add(normalized.MobilePhone!))
            {
                mobiles.Add(normalized.MobilePhone!);
            }

            if (normalized.HasLandline && seenLandlines.Add(normalized.LandlinePhone!))
            {
                landlines.Add(normalized.LandlinePhone!);
            }
        }

        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            // Extract Instagram profile from URL
            if (instagramHandle is null)
            {
                var igMatch = InstagramUrlRegex().Match(link);
                if (igMatch.Success)
                {
                    var handle = igMatch.Groups["handle"].Value.Trim().TrimEnd('.');
                    if (!string.IsNullOrWhiteSpace(handle) && !ReservedInstagramPaths.Contains(handle))
                    {
                        instagramHandle = "@" + handle.ToLowerInvariant();
                        instagramUrl = $"https://www.instagram.com/{handle.ToLowerInvariant()}/";
                    }
                }
            }

            // Extract WhatsApp numbers from wa.me / api.whatsapp.com links
            foreach (Match waMatch in WhatsAppLinkRegex().Matches(link))
            {
                CollectPhone(waMatch.Groups["phone"].Value);
            }
        }

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Extract WhatsApp links embedded inside snippets
            foreach (Match waMatch in WhatsAppLinkRegex().Matches(text))
            {
                CollectPhone(waMatch.Groups["phone"].Value);
            }

            // Extract Instagram URLs inside snippets
            if (instagramHandle is null)
            {
                var igUrlMatch = InstagramUrlRegex().Match(text);
                if (igUrlMatch.Success)
                {
                    var handle = igUrlMatch.Groups["handle"].Value.Trim().TrimEnd('.');
                    if (!string.IsNullOrWhiteSpace(handle) && !ReservedInstagramPaths.Contains(handle))
                    {
                        instagramHandle = "@" + handle.ToLowerInvariant();
                        instagramUrl = $"https://www.instagram.com/{handle.ToLowerInvariant()}/";
                    }
                }
            }

            // Extract @handle from Instagram title/snippet (e.g., "Clínica X (@clinica.x) • Fotos e vídeos do Instagram")
            if (instagramHandle is null && text.Contains("instagram", StringComparison.OrdinalIgnoreCase))
            {
                var handleMatch = AtHandleRegex().Match(text);
                if (handleMatch.Success)
                {
                    var handle = handleMatch.Groups["handle"].Value.Trim().TrimEnd('.');
                    if (!string.IsNullOrWhiteSpace(handle) && !ReservedInstagramPaths.Contains(handle))
                    {
                        instagramHandle = "@" + handle.ToLowerInvariant();
                        instagramUrl ??= $"https://www.instagram.com/{handle.ToLowerInvariant()}/";
                    }
                }
            }

            // Extract Brazilian phone numbers from snippets/titles
            foreach (Match phoneMatch in BrazilianPhoneRegex().Matches(text))
            {
                CollectPhone(phoneMatch.Value);
            }
        }

        return new ExtractedWebData(instagramHandle, instagramUrl, mobiles, landlines);
    }

    public static (string? PrimaryPhone, string? SecondaryPhone) ComposePhones(
        string? existingMobile,
        string? existingLandline,
        IEnumerable<string> discoveredMobiles,
        IEnumerable<string> discoveredLandlines)
    {
        var mobiles = new List<string>();
        var landlines = new List<string>();

        if (!string.IsNullOrWhiteSpace(existingMobile))
            mobiles.Add(existingMobile);

        foreach (var m in discoveredMobiles)
        {
            if (!string.IsNullOrWhiteSpace(m) && !mobiles.Contains(m, StringComparer.Ordinal))
                mobiles.Add(m);
        }

        if (!string.IsNullOrWhiteSpace(existingLandline))
            landlines.Add(existingLandline);

        foreach (var l in discoveredLandlines)
        {
            if (!string.IsNullOrWhiteSpace(l) && !landlines.Contains(l, StringComparer.Ordinal))
                landlines.Add(l);
        }

        var primary = mobiles.Count > 0 ? mobiles[0] : null;
        string? secondary = null;

        if (mobiles.Count > 1)
        {
            secondary = mobiles[1];
        }
        else if (landlines.Count > 0)
        {
            secondary = landlines[0];
        }

        return (primary, secondary);
    }

    public static bool IsInstagramUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Contains("instagram.com", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"https?://(?:www\.|m\.)?instagram\.com/(?<handle>[A-Za-z0-9._]{2,30})(?:/|\?|$)", RegexOptions.IgnoreCase)]
    private static partial Regex InstagramUrlRegex();

    [GeneratedRegex(@"(?:wa\.me/|api\.whatsapp\.com/send\?(?:[^&\s]*&)*phone=)(?<phone>\+?\d{10,15})", RegexOptions.IgnoreCase)]
    private static partial Regex WhatsAppLinkRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9._])@(?<handle>[A-Za-z0-9._]{2,30})")]
    private static partial Regex AtHandleRegex();

    [GeneratedRegex(@"(?:\+?55[\s.-]*)?(?:\(?0?[1-9]{2}\)?[\s.-]*)(?:9[\s.-]*\d{4}[\s.-]?\d{4}|[2-8]\d{3}[\s.-]?\d{4})")]
    private static partial Regex BrazilianPhoneRegex();
}
