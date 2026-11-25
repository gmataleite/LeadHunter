using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Services;
using LeadHunter.Infrastructure.Configuration;
using LeadHunter.Infrastructure.GooglePlaces;
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

            // Allow GOOGLE_PLACES_API_KEY env var / .env to override empty or default appsettings value
            var envKey = configuration["GOOGLE_PLACES_API_KEY"];
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                options.ApiKey = envKey;
            }
        });

        services.AddHttpClient<IGooglePlacesClient, GooglePlacesClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

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
