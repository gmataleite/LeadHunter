using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadHunter.Core.Configuration;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;
using Microsoft.Extensions.Options;

namespace LeadHunter.Infrastructure.Gemini;

public sealed class GeminiLeadAnalyzer : IGeminiLeadAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SystemPrompt = """
        Você é um analista sênior de inteligência comercial B2B.
        Sua tarefa é analisar dados públicos de uma empresa (Google Maps e snippets da web/Instagram) e retornar EXCLUSIVAMENTE um JSON estruturado seguindo o schema informado.

        Regras obrigatórias:
        1. isTargetNiche: retorne true APENAS se a empresa realmente pertence ao nicho-alvo descrito na campanha. Retorne false se for um segmento diferente (ex: clínica veterinária, laboratório de prótese, loja de equipamentos, faculdade, etc.).
        2. ownerName: extraia o nome do dono, sócio ou profissional principal SOMENTE se houver evidência explícita nas respostas do proprietário no Google Maps ou na bio/snippet do Instagram (ex: "Dr. João Silva", "Dra. Maria Costa", "Atenciosamente, Carlos"). NUNCA invente ou suponha nomes; se não houver evidência clara, retorne null.
        3. ownerConfidence:
           - "Alta" se o mesmo nome for confirmado em duas ou mais fontes distintas (ex: resposta do Google Maps + bio do Instagram).
           - "Média" se o nome aparecer em apenas uma fonte.
           - null se ownerName for null.
        4. ownerSource: indique a origem da evidência (ex: "Resposta Google Maps + Instagram", "Instagram", "Resposta Google Maps") ou null se ownerName for null.
        5. reviewerFirstName: extraia APENAS o primeiro nome do autor de uma das avaliações de 5 estrelas mais recentes que contenha texto elogiando um serviço específico (ou null se não houver avaliações 5 estrelas úteis).
        6. praisedService: extraia o serviço específico elogiado nessa mesma avaliação de 5 estrelas em NO MÁXIMO 3 palavras em português (ex: "Limpeza e clareamento", "Canal", "Alinhadores", "Implante dentário") ou null se não houver serviço identificável.
        """;

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiLeadAnalyzer(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

    public async Task<GeminiAnalysisResult> AnalyzeAsync(
        Lead lead,
        CampaignConfig config,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Chave da Gemini API não configurada. Defina GEMINI_API_KEY no .env ou em appsettings.json (Gemini:ApiKey).");
        }

        var userPrompt = BuildLeadContextPrompt(lead, config);
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/{_options.Model}:generateContent";

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        isTargetNiche = new { type = "BOOLEAN" },
                        ownerName = new { type = "STRING", nullable = true },
                        ownerConfidence = new
                        {
                            type = "STRING",
                            nullable = true,
                            @enum = new[] { "Alta", "Média" }
                        },
                        ownerSource = new { type = "STRING", nullable = true },
                        reviewerFirstName = new { type = "STRING", nullable = true },
                        praisedService = new { type = "STRING", nullable = true }
                    },
                    required = new[]
                    {
                        "isTargetNiche",
                        "ownerName",
                        "ownerConfidence",
                        "ownerSource",
                        "reviewerFirstName",
                        "praisedService"
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini API retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {rawBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var geminiResponse = JsonSerializer.Deserialize<GeminiApiResponse>(rawBody, JsonOptions);
        var jsonText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            throw new InvalidOperationException("Gemini API retornou resposta vazia sem conteúdo JSON.");
        }

        var structured = JsonSerializer.Deserialize<StructuredLeadAnalysisDto>(jsonText, JsonOptions)
            ?? throw new InvalidOperationException("Não foi possível desserializar o JSON estruturado retornado pelo Gemini.");

        var inputTokens = geminiResponse?.UsageMetadata?.PromptTokenCount ?? 0;
        var outputTokens = geminiResponse?.UsageMetadata?.CandidatesTokenCount ?? 0;
        var totalTokens = geminiResponse?.UsageMetadata?.TotalTokenCount ?? (inputTokens + outputTokens);

        var estimatedCost =
            (inputTokens / 1_000_000m) * _options.InputCostPerMillionTokensUsd +
            (outputTokens / 1_000_000m) * _options.OutputCostPerMillionTokensUsd;

        var ownerName = CleanNullable(structured.OwnerName);
        var ownerConfidence = ownerName is null ? null : CleanNullable(structured.OwnerConfidence);
        var ownerSource = ownerName is null ? null : CleanNullable(structured.OwnerSource);

        return new GeminiAnalysisResult
        {
            IsTargetNiche = structured.IsTargetNiche,
            OwnerName = ownerName,
            OwnerConfidence = ownerConfidence,
            OwnerSource = ownerSource,
            ReviewerFirstName = ExtractFirstName(structured.ReviewerFirstName),
            PraisedService = CleanNullable(structured.PraisedService),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = Math.Round(estimatedCost, 6),
            HttpStatusCode = (int)response.StatusCode,
            ModelName = _options.Model
        };
    }

    private static string BuildLeadContextPrompt(Lead lead, CampaignConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Campanha: {config.CampaignName}");
        sb.AppendLine($"Descrição do Nicho-Alvo: {config.NicheDescription}");
        sb.AppendLine();
        sb.AppendLine($"Nome da Empresa: {lead.DisplayName}");
        if (!string.IsNullOrWhiteSpace(lead.FormattedAddress))
            sb.AppendLine($"Endereço: {lead.FormattedAddress}");
        if (!string.IsNullOrWhiteSpace(lead.InstagramHandle))
            sb.AppendLine($"Instagram identificado: {lead.InstagramHandle} ({lead.InstagramUrl})");
        if (!string.IsNullOrWhiteSpace(lead.WebsiteUri))
            sb.AppendLine($"Website no Maps: {lead.WebsiteUri}");

        var (fiveStarReviews, ownerResponses) = ExtractReviewsContext(lead.ReviewsJson);

        sb.AppendLine();
        sb.AppendLine("=== Respostas do Proprietário no Google Maps ===");
        if (ownerResponses.Count == 0)
        {
            sb.AppendLine("(Nenhuma resposta do proprietário encontrada)");
        }
        else
        {
            foreach (var reply in ownerResponses)
                sb.AppendLine($"- {reply}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Snippets Web / Instagram ===");
        if (string.IsNullOrWhiteSpace(lead.WebSnippetsJson))
        {
            sb.AppendLine("(Nenhum snippet web disponível)");
        }
        else
        {
            sb.AppendLine(lead.WebSnippetsJson);
        }

        sb.AppendLine();
        sb.AppendLine("=== Até 5 Avaliações de 5 Estrelas Mais Recentes ===");
        if (fiveStarReviews.Count == 0)
        {
            sb.AppendLine("(Nenhuma avaliação de 5 estrelas disponível)");
        }
        else
        {
            foreach (var review in fiveStarReviews)
                sb.AppendLine($"- Autor: {review.Author} | Data: {review.PublishTime ?? "N/A"} | Texto: {review.Text}");
        }

        return sb.ToString();
    }

    private static (List<ReviewSummary> FiveStarReviews, List<string> OwnerResponses) ExtractReviewsContext(string? reviewsJson)
    {
        var fiveStar = new List<ReviewSummary>();
        var ownerResponses = new List<string>();

        if (string.IsNullOrWhiteSpace(reviewsJson))
            return (fiveStar, ownerResponses);

        try
        {
            using var doc = JsonDocument.Parse(reviewsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (fiveStar, ownerResponses);

            foreach (var review in doc.RootElement.EnumerateArray())
            {
                if (review.ValueKind != JsonValueKind.Object)
                    continue;

                var rating = review.TryGetProperty("rating", out var ratingEl) && ratingEl.TryGetInt32(out var r)
                    ? r
                    : 0;

                var publishTime = review.TryGetProperty("publishTime", out var pubEl) && pubEl.ValueKind == JsonValueKind.String
                    ? pubEl.GetString()
                    : null;

                var author = "Anônimo";
                if (review.TryGetProperty("authorAttribution", out var attrEl)
                    && attrEl.ValueKind == JsonValueKind.Object
                    && attrEl.TryGetProperty("displayName", out var nameEl)
                    && nameEl.ValueKind == JsonValueKind.String)
                {
                    author = nameEl.GetString() ?? "Anônimo";
                }

                string? text = null;
                if (review.TryGetProperty("text", out var textObj)
                    && textObj.ValueKind == JsonValueKind.Object
                    && textObj.TryGetProperty("text", out var innerText)
                    && innerText.ValueKind == JsonValueKind.String)
                {
                    text = innerText.GetString();
                }
                else if (review.TryGetProperty("originalText", out var origObj)
                         && origObj.ValueKind == JsonValueKind.Object
                         && origObj.TryGetProperty("text", out var origText)
                         && origText.ValueKind == JsonValueKind.String)
                {
                    text = origText.GetString();
                }

                // Extract owner response if present in review payload
                if (review.TryGetProperty("ownerResponse", out var ownerRespEl) && ownerRespEl.ValueKind == JsonValueKind.Object)
                {
                    if (ownerRespEl.TryGetProperty("text", out var respText) && respText.ValueKind == JsonValueKind.String)
                    {
                        var val = respText.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            ownerResponses.Add(val);
                    }
                }

                if (rating == 5 && !string.IsNullOrWhiteSpace(text))
                {
                    fiveStar.Add(new ReviewSummary(author, publishTime, text));
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed review JSON safely
        }

        var topFiveRecent = fiveStar
            .OrderByDescending(r => r.PublishTime, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        return (topFiveRecent, ownerResponses);
    }

    private static string? CleanNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static string? ExtractFirstName(string? name)
    {
        var cleaned = CleanNullable(name);
        if (cleaned is null)
            return null;

        var firstToken = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstToken) ? null : firstToken;
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return _options.ApiKey.Trim();

        return Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim() ?? string.Empty;
    }

    private sealed record ReviewSummary(string Author, string? PublishTime, string Text);

    private sealed class StructuredLeadAnalysisDto
    {
        public bool IsTargetNiche { get; init; }
        public string? OwnerName { get; init; }
        public string? OwnerConfidence { get; init; }
        public string? OwnerSource { get; init; }
        public string? ReviewerFirstName { get; init; }
        public string? PraisedService { get; init; }
    }

    private sealed class GeminiApiResponse
    {
        public List<CandidateDto>? Candidates { get; init; }
        public UsageMetadataDto? UsageMetadata { get; init; }
    }

    private sealed class CandidateDto
    {
        public ContentDto? Content { get; init; }
    }

    private sealed class ContentDto
    {
        public List<PartDto>? Parts { get; init; }
    }

    private sealed class PartDto
    {
        public string? Text { get; init; }
    }

    private sealed class UsageMetadataDto
    {
        public int PromptTokenCount { get; init; }
        public int CandidatesTokenCount { get; init; }
        public int TotalTokenCount { get; init; }
    }
}
