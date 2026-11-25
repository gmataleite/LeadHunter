using System.CommandLine;
using dotenv.net;
using LeadHunter.Cli;
using LeadHunter.Core.Interfaces;
using LeadHunter.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

// Load .env from current working directory first, then probe parent directories from BaseDirectory
var cwdEnvPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(cwdEnvPath))
{
    DotEnv.Load(new DotEnvOptions(
        ignoreExceptions: true,
        envFilePaths: [cwdEnvPath]));
}
else
{
    DotEnv.Load(new DotEnvOptions(
        ignoreExceptions: true,
        probeForEnv: true,
        probeLevelsToSearch: 8));
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLeadHunterInfrastructure(configuration);

await using var serviceProvider = services.BuildServiceProvider();

// Apply automatic EF Core SQLite migrations on startup
await serviceProvider.InitializeDatabaseAsync();

var rootCommand = new RootCommand("LeadHunter — Prospecção B2B automatizada com controle rigoroso de custos de API");

// 1. preview command
var previewConfigOption = new Option<string>(
    aliases: ["--config", "-c"],
    description: "Caminho do arquivo JSON da campanha (ex: campaigns/dentistas-abc.json)")
{
    IsRequired = true
};

var previewCommand = new Command("preview", "Executa um lote reduzido de validação (PreviewBatchSize) para inspecionar leads e custos.");
previewCommand.AddOption(previewConfigOption);
previewCommand.SetHandler(async (string configPath) =>
{
    await ExecuteProspectingAsync(serviceProvider, configPath, isPreview: true, isResume: false);
}, previewConfigOption);

// 2. run command
var runConfigOption = new Option<string>(
    aliases: ["--config", "-c"],
    description: "Caminho do arquivo JSON da campanha (ex: campaigns/dentistas-abc.json)")
{
    IsRequired = true
};

var runCommand = new Command("run", "Executa a coleta completa respeitando TargetValidLeads e o Circuit Breaker de orçamento.");
runCommand.AddOption(runConfigOption);
runCommand.SetHandler(async (string configPath) =>
{
    await ExecuteProspectingAsync(serviceProvider, configPath, isPreview: false, isResume: false);
}, runConfigOption);

// 3. resume command
var resumeConfigOption = new Option<string?>(
    aliases: ["--config", "-c"],
    description: "Caminho do arquivo JSON da campanha (opcional se --campaign for informado)");

var resumeCampaignOption = new Option<string?>(
    aliases: ["--campaign", "-n"],
    description: "Nome da campanha salva no banco para retomar de onde parou");

var resumeCommand = new Command("resume", "Retoma uma campanha interrompida sem repetir queries concluídas nem pagar duas vezes pelo mesmo PlaceId.");
resumeCommand.AddOption(resumeConfigOption);
resumeCommand.AddOption(resumeCampaignOption);
resumeCommand.SetHandler(async (string? configPath, string? campaignName) =>
{
    var resolvedConfigPath = configPath;

    if (string.IsNullOrWhiteSpace(resolvedConfigPath))
    {
        if (string.IsNullOrWhiteSpace(campaignName))
        {
            AnsiConsole.MarkupLine("[red]Erro:[/] Informe [cyan]--config <arquivo.json>[/] ou [cyan]--campaign <nome>[/].");
            Environment.ExitCode = 1;
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILeadHunterRepository>();
        var campaign = await repo.GetCampaignByNameAsync(campaignName);

        if (campaign is null)
        {
            AnsiConsole.MarkupLine($"[red]Erro:[/] Campanha '{Markup.Escape(campaignName)}' não encontrada no banco de dados.");
            Environment.ExitCode = 1;
            return;
        }

        resolvedConfigPath = campaign.ConfigPath;
    }

    await ExecuteProspectingAsync(serviceProvider, resolvedConfigPath, isPreview: false, isResume: true);
}, resumeConfigOption, resumeCampaignOption);

// 4. status command
var statusCampaignOption = new Option<string?>(
    aliases: ["--campaign", "-n"],
    description: "Filtra o status por uma campanha específica (opcional)");

var statusCommand = new Command("status", "Exibe o painel consolidado de leads por Tier, descartes e custos de API.");
statusCommand.AddOption(statusCampaignOption);
statusCommand.SetHandler(async (string? campaignName) =>
{
    ConsoleRenderer.RenderBanner();

    using var scope = serviceProvider.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ILeadHunterRepository>();
    var reports = await repo.GetStatusReportsAsync(campaignName);

    ConsoleRenderer.RenderStatusReports(reports);
}, statusCampaignOption);

rootCommand.AddCommand(previewCommand);
rootCommand.AddCommand(runCommand);
rootCommand.AddCommand(resumeCommand);
rootCommand.AddCommand(statusCommand);

return await rootCommand.InvokeAsync(args);

static async Task ExecuteProspectingAsync(
    IServiceProvider provider,
    string configPath,
    bool isPreview,
    bool isResume)
{
    ConsoleRenderer.RenderBanner();

    try
    {
        using var scope = provider.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<ICampaignConfigLoader>();
        var service = scope.ServiceProvider.GetRequiredService<IProspectingService>();

        var config = await loader.LoadAsync(configPath);
        var fullConfigPath = Path.GetFullPath(configPath);

        AnsiConsole.MarkupLine(
            $"[grey]Campanha:[/] [bold]{Markup.Escape(config.CampaignName)}[/]  |  " +
            $"[grey]Queries:[/] {config.SearchQueries.Count}  |  " +
            $"[grey]Teto:[/] [green]${config.Limits.MaxBudgetUsd:0.000}[/]");
        AnsiConsole.WriteLine();

        var summary = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Iniciando prospecção...", async ctx =>
            {
                return await service.ExecuteAsync(
                    config,
                    fullConfigPath,
                    isPreview,
                    isResume,
                    onStatusMessage: msg =>
                    {
                        ctx.Status(Markup.Escape(msg));
                        AnsiConsole.MarkupLine($"[grey]>[/] {Markup.Escape(msg)}");
                    });
            });

        ConsoleRenderer.RenderExecutionSummary(summary);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[bold red]Erro durante a execução:[/] {Markup.Escape(ex.Message)}");
        Environment.ExitCode = 1;
    }
}
