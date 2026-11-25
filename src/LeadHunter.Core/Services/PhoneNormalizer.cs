using System.Text.RegularExpressions;
using LeadHunter.Core.Models;

namespace LeadHunter.Core.Services;

/// <summary>
/// Normalizes Brazilian phone numbers, separating mobile phones (+55 DD 9XXXX-XXXX)
/// from landlines (+55 DD XXXX-XXXX).
/// </summary>
public static partial class PhoneNormalizer
{
    private static readonly char[] Separators = ['/', ';', '|'];

    public static PhoneNormalizationResult Normalize(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return new PhoneNormalizationResult(rawPhone, null, null);
        }

        string? mobile = null;
        string? landline = null;

        var parts = rawPhone.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var digits = NonDigitsRegex().Replace(part, string.Empty);
            if (string.IsNullOrEmpty(digits))
            {
                continue;
            }

            // Strip international prefix (0055 or 55) when present
            if (digits.StartsWith("0055", StringComparison.Ordinal) && digits.Length >= 14)
            {
                digits = digits[4..];
            }
            else if (digits.StartsWith("55", StringComparison.Ordinal) && digits.Length is 12 or 13)
            {
                digits = digits[2..];
            }

            // Strip leading trunk '0' (e.g., 011 98765-4321 -> 11987654321)
            if (digits.StartsWith('0') && digits.Length is 11 or 12)
            {
                digits = digits[1..];
            }

            // Mobile: DDD (2 digits, 11..99 without 0) + '9' + 8 digits = 11 digits
            if (digits.Length == 11 && IsValidBrazilianDdd(digits[..2]) && digits[2] == '9')
            {
                mobile ??= $"+55 {digits[..2]} {digits[2..7]}-{digits[7..]}";
                continue;
            }

            // Landline: DDD (2 digits, 11..99 without 0) + 8 digits (typically 2-5, accepting 2-8) = 10 digits
            if (digits.Length == 10 && IsValidBrazilianDdd(digits[..2]) && digits[2] is >= '2' and <= '8')
            {
                landline ??= $"+55 {digits[..2]} {digits[2..6]}-{digits[6..]}";
            }
        }

        return new PhoneNormalizationResult(rawPhone, mobile, landline);
    }

    private static bool IsValidBrazilianDdd(string ddd)
    {
        if (ddd.Length != 2)
        {
            return false;
        }

        return ddd[0] is >= '1' and <= '9' && ddd[1] is >= '1' and <= '9';
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitsRegex();
}
