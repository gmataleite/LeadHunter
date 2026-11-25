using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using LeadHunter.Core.Entities;
using LeadHunter.Core.Interfaces;
using LeadHunter.Core.Models;
using LeadHunter.Core.Services;
using Microsoft.Extensions.Options;

namespace LeadHunter.Infrastructure.GoogleSheets;

public sealed class GoogleSheetsExporter : IGoogleSheetsExporter
{
    private const string TabReadyLeads = "Leads_Prontos";
    private const string TabPendingQuota = "Pendentes_Cota";
    private const string TabAuditAndCosts = "Auditoria_e_Custos";

    private static readonly IList<object> ReadyLeadsHeaders =
    [
        "Nome do Contato",
        "Nome da Empresa",
        "Telefone Principal",
        "Telefone 2",
        "Endereço com Bairro",
        "Site",
        "Fonte do Contato",
        "Tag",
        "Dono",
        "Confiança do Dono",
        "Fonte do Dono",
        "Instagram",
        "CNPJ",
        "Nota Google",
        "Número de Avaliações",
        "Tem Site?",
        "Link do Google Maps",
        "Cliente da Avaliação",
        "Serviço Elogiado"
    ];

    private static readonly IList<object> PendingQuotaHeaders =
    [
        "Tier",
        "Nome da Empresa",
        "Cidade",
        "Endereço com Bairro",
        "Telefone Maps",
        "Website Maps",
        "Nota Google",
        "Avaliações",
        "Link do Google Maps"
    ];

    private readonly GoogleSheetsOptions _options;

    public GoogleSheetsExporter(IOptions<GoogleSheetsOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => ResolveServiceAccountPath() is not null;

    public async Task<SheetsExportResult> ExportAsync(
        Campaign campaign,
        IReadOnlyList<Lead> readyLeads,
        IReadOnlyList<Lead> pendingQuotaLeads,
        CampaignStatusReport auditReport,
        CancellationToken cancellationToken = default)
    {
        var serviceAccountPath = ResolveServiceAccountPath()
            ?? throw new FileNotFoundException(
                $"Arquivo de Service Account não encontrado ('{_options.ServiceAccountJsonPath}'). Configure GOOGLE_SERVICE_ACCOUNT_PATH no .env ou coloque service-account.json na raiz do projeto.");

        var credential = CredentialFactory.FromFile<ServiceAccountCredential>(serviceAccountPath)
            .ToGoogleCredential()
            .CreateScoped(SheetsService.Scope.Spreadsheets, DriveService.Scope.Drive);

        using var sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "LeadHunter"
        });

        using var driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "LeadHunter"
        });

        var spreadsheetId = await EnsureSpreadsheetAsync(sheetsService, driveService, campaign, cancellationToken);
        var spreadsheetUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit";

        // 1. Aba Leads_Prontos (append incremental preservando linhas já exportadas)
        await ExportReadyLeadsAsync(sheetsService, spreadsheetId, readyLeads, cancellationToken);

        // 2. Aba Pendentes_Cota (estado atual da fila de cota)
        await ExportPendingQuotaAsync(sheetsService, spreadsheetId, pendingQuotaLeads, cancellationToken);

        // 3. Aba Auditoria_e_Custos (resumo consolidado de execução, descartes e custos)
        await ExportAuditAndCostsAsync(sheetsService, spreadsheetId, auditReport, readyLeads.Count, cancellationToken);

        return new SheetsExportResult
        {
            SpreadsheetId = spreadsheetId,
            SpreadsheetUrl = spreadsheetUrl,
            ExportedLeadsCount = readyLeads.Count,
            PendingQuotaRowsCount = pendingQuotaLeads.Count,
            ExportedLeads = readyLeads
        };
    }

    private async Task<string> EnsureSpreadsheetAsync(
        SheetsService sheetsService,
        DriveService driveService,
        Campaign campaign,
        CancellationToken cancellationToken)
    {
        var existingId = !string.IsNullOrWhiteSpace(_options.SpreadsheetId)
            ? _options.SpreadsheetId.Trim()
            : campaign.SpreadsheetId;

        if (!string.IsNullOrWhiteSpace(existingId))
        {
            var existingSheet = await sheetsService.Spreadsheets.Get(existingId).ExecuteAsync(cancellationToken);
            var existingTitles = existingSheet.Sheets?
                .Select(s => s.Properties?.Title)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

            var missingTabs = new[] { TabReadyLeads, TabPendingQuota, TabAuditAndCosts }
                .Where(t => !existingTitles.Contains(t))
                .ToList();

            if (missingTabs.Count > 0)
            {
                var batchRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = missingTabs.Select(title => new Request
                    {
                        AddSheet = new AddSheetRequest
                        {
                            Properties = new SheetProperties
                            {
                                Title = title,
                                GridProperties = new GridProperties { FrozenRowCount = 1 }
                            }
                        }
                    }).ToList()
                };

                await sheetsService.Spreadsheets.BatchUpdate(batchRequest, existingId).ExecuteAsync(cancellationToken);
            }

            return existingId;
        }

        // Create a brand new spreadsheet with the 3 required tabs
        var newSpreadsheet = new Spreadsheet
        {
            Properties = new SpreadsheetProperties
            {
                Title = $"LeadHunter - {campaign.Name}"
            },
            Sheets =
            [
                new Sheet
                {
                    Properties = new SheetProperties
                    {
                        Title = TabReadyLeads,
                        GridProperties = new GridProperties { FrozenRowCount = 1 }
                    }
                },
                new Sheet
                {
                    Properties = new SheetProperties
                    {
                        Title = TabPendingQuota,
                        GridProperties = new GridProperties { FrozenRowCount = 1 }
                    }
                },
                new Sheet
                {
                    Properties = new SheetProperties
                    {
                        Title = TabAuditAndCosts,
                        GridProperties = new GridProperties { FrozenRowCount = 1 }
                    }
                }
            ]
        };

        var created = await sheetsService.Spreadsheets.Create(newSpreadsheet).ExecuteAsync(cancellationToken);
        var spreadsheetId = created.SpreadsheetId
            ?? throw new InvalidOperationException("Google Sheets API não retornou o SpreadsheetId ao criar a planilha.");

        // Format header rows as bold
        if (created.Sheets is { Count: > 0 })
        {
            var formatRequests = created.Sheets
                .Where(s => s.Properties?.SheetId is not null)
                .Select(s => new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange
                        {
                            SheetId = s.Properties.SheetId,
                            StartRowIndex = 0,
                            EndRowIndex = 1
                        },
                        Cell = new CellData
                        {
                            UserEnteredFormat = new CellFormat
                            {
                                TextFormat = new TextFormat { Bold = true }
                            }
                        },
                        Fields = "userEnteredFormat.textFormat.bold"
                    }
                }).ToList();

            if (formatRequests.Count > 0)
            {
                await sheetsService.Spreadsheets
                    .BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = formatRequests }, spreadsheetId)
                    .ExecuteAsync(cancellationToken);
            }
        }

        // Share with TargetShareEmail via Google Drive API
        var shareEmail = ResolveTargetShareEmail();
        if (!string.IsNullOrWhiteSpace(shareEmail))
        {
            var permission = new Permission
            {
                Type = "user",
                Role = "writer",
                EmailAddress = shareEmail
            };

            var createPermission = driveService.Permissions.Create(permission, spreadsheetId);
            createPermission.SendNotificationEmail = false;
            await createPermission.ExecuteAsync(cancellationToken);
        }

        return spreadsheetId;
    }

    private static async Task ExportReadyLeadsAsync(
        SheetsService sheetsService,
        string spreadsheetId,
        IReadOnlyList<Lead> readyLeads,
        CancellationToken cancellationToken)
    {
        // Check if header row already exists
        var existingHeader = await sheetsService.Spreadsheets.Values
            .Get(spreadsheetId, $"{TabReadyLeads}!A1:S1")
            .ExecuteAsync(cancellationToken);

        var rowsToAppend = new List<IList<object>>();
        if (existingHeader.Values is null || existingHeader.Values.Count == 0)
        {
            rowsToAppend.Add(ReadyLeadsHeaders);
        }

        foreach (var lead in readyLeads)
        {
            rowsToAppend.Add(MapLeadToReadyRow(lead));
        }

        if (rowsToAppend.Count == 0)
        {
            return;
        }

        var valueRange = new ValueRange { Values = rowsToAppend };
        var appendRequest = sheetsService.Spreadsheets.Values.Append(valueRange, spreadsheetId, $"{TabReadyLeads}!A1");
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        appendRequest.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await appendRequest.ExecuteAsync(cancellationToken);
    }

    private static async Task ExportPendingQuotaAsync(
        SheetsService sheetsService,
        string spreadsheetId,
        IReadOnlyList<Lead> pendingQuotaLeads,
        CancellationToken cancellationToken)
    {
        await sheetsService.Spreadsheets.Values
            .Clear(new ClearValuesRequest(), spreadsheetId, $"{TabPendingQuota}!A:Z")
            .ExecuteAsync(cancellationToken);

        var rows = new List<IList<object>> { PendingQuotaHeaders };
        foreach (var lead in pendingQuotaLeads)
        {
            rows.Add(
            [
                lead.Tier.ToString(),
                lead.DisplayName,
                lead.City ?? string.Empty,
                lead.FormattedAddress ?? string.Empty,
                lead.RawPhoneNumber ?? string.Empty,
                lead.WebsiteUri ?? string.Empty,
                lead.Rating?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
                lead.UserRatingCount?.ToString(CultureInfo.InvariantCulture) ?? "0",
                lead.GoogleMapsUri ?? string.Empty
            ]);
        }

        var updateRequest = sheetsService.Spreadsheets.Values.Update(
            new ValueRange { Values = rows },
            spreadsheetId,
            $"{TabPendingQuota}!A1");
        updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await updateRequest.ExecuteAsync(cancellationToken);
    }

    private static async Task ExportAuditAndCostsAsync(
        SheetsService sheetsService,
        string spreadsheetId,
        CampaignStatusReport report,
        int exportedInThisSession,
        CancellationToken cancellationToken)
    {
        await sheetsService.Spreadsheets.Values
            .Clear(new ClearValuesRequest(), spreadsheetId, $"{TabAuditAndCosts}!A:Z")
            .ExecuteAsync(cancellationToken);

        var rows = new List<IList<object>>
        {
            new List<object> { "Métrica", "Valor" },
            new List<object> { "Campanha", report.CampaignName },
            new List<object> { "Nicho", report.NicheDescription },
            new List<object> { "Atualizado em (UTC)", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
            new List<object> { "Status da Campanha", report.Status.ToString() },
            new List<object> { "Total de Leads Válidos Encontrados", report.TotalValidLeads },
            new List<object> { "Leads com Celular Confirmado", report.ConfirmedMobileCount },
            new List<object> { "Leads Exportados (Total Acumulado)", report.ExportedLeadsCount + exportedInThisSession },
            new List<object> { "Donos Identificados (Alta/Média)", report.OwnersIdentifiedCount },
            new List<object> { "Pendentes de Cota (Pendentes_Cota)", report.PendingQuotaCount },
            new List<object> { "Cota Custom Search Usada Hoje (UTC)", $"{report.QuotaUsedToday} / {report.QuotaDailyLimit}" },
            new List<object> { "Total de Chamadas de API", report.TotalApiCalls },
            new List<object> { "Tokens Gemini Consumidos", report.TotalGeminiTokens },
            new List<object> { "Custo Total Estimado (USD)", $"${report.TotalSpentUsd:0.0000}" },
            new List<object> { "Orçamento Máximo (USD)", $"${report.MaxBudgetUsd:0.0000}" },
            new List<object> { string.Empty, string.Empty },
            new List<object> { "Descartes / Não Exportáveis por Motivo", "Quantidade" }
        };

        if (report.WithoutMobileCount > 0)
        {
            rows.Add(["Sem celular", report.WithoutMobileCount]);
        }

        foreach (var kv in report.DiscardReasons.OrderByDescending(x => x.Value))
        {
            rows.Add([kv.Key, kv.Value]);
        }

        var updateRequest = sheetsService.Spreadsheets.Values.Update(
            new ValueRange { Values = rows },
            spreadsheetId,
            $"{TabAuditAndCosts}!A1");
        updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await updateRequest.ExecuteAsync(cancellationToken);
    }

    private static IList<object> MapLeadToReadyRow(Lead lead)
    {
        var hasTrustedOwner = !string.IsNullOrWhiteSpace(lead.OwnerName) &&
                              (string.Equals(lead.OwnerConfidence, "Alta", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(lead.OwnerConfidence, "Média", StringComparison.OrdinalIgnoreCase));

        var contactName = hasTrustedOwner ? lead.OwnerName! : lead.DisplayName;

        var hasWebsite = !string.IsNullOrWhiteSpace(lead.WebsiteUri);
        var isSocialOnly = hasWebsite && LeadClassifier.IsSocialOrLinkInBioUrl(lead.WebsiteUri!);

        var siteColumn = (!hasWebsite || isSocialOnly) ? string.Empty : lead.WebsiteUri!;
        var hasSiteLabel = !hasWebsite
            ? "Não"
            : (isSocialOnly ? "Só Instagram" : "Sim");

        var confidenceLabel = hasTrustedOwner ? (lead.OwnerConfidence ?? string.Empty) : string.Empty;

        return
        [
            contactName,
            lead.DisplayName,
            lead.PrimaryPhone ?? lead.MobilePhone ?? string.Empty,
            lead.SecondaryPhone ?? lead.LandlinePhone ?? string.Empty,
            lead.FormattedAddress ?? string.Empty,
            siteColumn,
            "Google Maps",
            "lista-maps",
            lead.OwnerName ?? string.Empty,
            confidenceLabel,
            lead.OwnerSource ?? string.Empty,
            lead.InstagramHandle ?? string.Empty,
            lead.Cnpj ?? string.Empty,
            lead.Rating?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            lead.UserRatingCount?.ToString(CultureInfo.InvariantCulture) ?? "0",
            hasSiteLabel,
            lead.GoogleMapsUri ?? string.Empty,
            lead.ReviewerFirstName ?? string.Empty,
            lead.PraisedService ?? string.Empty
        ];
    }

    private string? ResolveServiceAccountPath()
    {
        var configuredPath = !string.IsNullOrWhiteSpace(_options.ServiceAccountJsonPath)
            ? _options.ServiceAccountJsonPath.Trim()
            : Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_PATH")?.Trim();

        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        if (Path.IsPathRooted(configuredPath))
            return System.IO.File.Exists(configuredPath) ? configuredPath : null;

        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        if (System.IO.File.Exists(cwdCandidate))
            return cwdCandidate;

        var baseCandidate = Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (System.IO.File.Exists(baseCandidate))
            return baseCandidate;

        return null;
    }

    private string ResolveTargetShareEmail()
    {
        if (!string.IsNullOrWhiteSpace(_options.TargetShareEmail))
            return _options.TargetShareEmail.Trim();

        return Environment.GetEnvironmentVariable("GOOGLE_SHEETS_SHARE_EMAIL")?.Trim() ?? string.Empty;
    }
}
