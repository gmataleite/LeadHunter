using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Services;
using LeadHunter.Infrastructure.Configuration;
using LeadHunter.Infrastructure.Gemini;
using LeadHunter.Infrastructure.GooglePlaces;
using LeadHunter.Infrastructure.GoogleSearch;
using LeadHunter.Infrastructure.GoogleSheets;
using LeadHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LeadHunter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLeadHunterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Data Source=leadhunter.db";
        }

        services.AddDbContext<LeadHunterDbContext>(options =>
            options.UseSqlite(connectionString));

        services.Configure<GooglePlacesOptions>(options =>
        {
            configuration.GetSection(GooglePlacesOptions.SectionName).Bind(options);

            var envKey = configuration["GOOGLE_PLACES_API_KEY"];
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                options.ApiKey = envKey;
            }
        });

        services.Configure<GoogleSearchOptions>(options =>
        {
            configuration.GetSection(GoogleSearchOptions.SectionName).Bind(options);

            var envKey = configuration["GOOGLE_SEARCH_API_KEY"];
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                options.ApiKey = envKey;
            }

            var envCx = configuration["GOOGLE_SEARCH_ENGINE_ID"];
            if (!string.IsNullOrWhiteSpace(envCx))
            {
                options.SearchEngineId = envCx;
            }
        });

        services.Configure<GeminiOptions>(options =>
        {
            configuration.GetSection(GeminiOptions.SectionName).Bind(options);

            var envKey = configuration["GEMINI_API_KEY"];
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                options.ApiKey = envKey;
            }
        });

        services.Configure<GoogleSheetsOptions>(options =>
        {
            configuration.GetSection(GoogleSheetsOptions.SectionName).Bind(options);

            var envSaPath = configuration["GOOGLE_SERVICE_ACCOUNT_PATH"];
            if (!string.IsNullOrWhiteSpace(envSaPath))
            {
                options.ServiceAccountJsonPath = envSaPath;
            }

            var envShareEmail = configuration["GOOGLE_SHEETS_SHARE_EMAIL"];
            if (!string.IsNullOrWhiteSpace(envShareEmail))
            {
                options.TargetShareEmail = envShareEmail;
            }

            var envSheetId = configuration["GOOGLE_SHEETS_SPREADSHEET_ID"];
            if (!string.IsNullOrWhiteSpace(envSheetId))
            {
                options.SpreadsheetId = envSheetId;
            }
        });

        services.AddHttpClient<IGooglePlacesClient, GooglePlacesClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IGoogleSearchEnricher, GoogleSearchEnricher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IGeminiLeadAnalyzer, GeminiLeadAnalyzer>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });

        services.AddSingleton<IGoogleSheetsExporter, GoogleSheetsExporter>();

        services.AddScoped<ILeadHunterRepository, LeadHunterRepository>();
        services.AddSingleton<ICampaignConfigLoader, JsonCampaignConfigLoader>();
        services.AddSingleton<LeadClassifier>();
        services.AddScoped<IProspectingService, ProspectingService>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeadHunterDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
