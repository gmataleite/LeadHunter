using System.Text.Json;
using LeadHunter.Core.Configuration;
using LeadHunter.Core.Interfaces;

namespace LeadHunter.Infrastructure.Configuration;

public sealed class JsonCampaignConfigLoader : ICampaignConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<CampaignConfig> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("O caminho do arquivo de campanha é obrigatório.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Arquivo de campanha não encontrado: '{fullPath}'", fullPath);
        }

        await using var stream = File.OpenRead(fullPath);
        var config = await JsonSerializer.DeserializeAsync<CampaignConfig>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Não foi possível desserializar o arquivo de campanha '{fullPath}'.");

        config.Validate();
        return config;
    }
}
