namespace LeadHunter.Infrastructure.Gemini;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models";

    // Gemini 2.5 Flash standard REST pricing (USD per 1M tokens)
    public decimal InputCostPerMillionTokensUsd { get; set; } = 0.15m;
    public decimal OutputCostPerMillionTokensUsd { get; set; } = 0.60m;
}
