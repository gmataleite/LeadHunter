using LeadHunter.Core.Entities;
using LeadHunter.Core.Enums;
using LeadHunter.Core.Models;
using Spectre.Console;

namespace LeadHunter.Cli;

public static class ConsoleRenderer
{
    public static void RenderBanner()
    {
        AnsiConsole.Write(
            new FigletText("LeadHunter")
                .LeftJustified()
                .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]Prospecção B2B automatizada com controle rigoroso de custos de API[/]");
        AnsiConsole.WriteLine();
    }

    public static void RenderExecutionSummary(ProspectingExecutionSummary summary)
    {
        AnsiConsole.WriteLine();

        if (summary.StoppedByBudgetCircuitBreaker)
        {
            var warningPanel = new Panel(
                $"[bold yellow]CIRCUIT BREAKER DE ORÇAMENTO ATIVADO![/]\n" +
                $"A coleta foi interrompida graciosamente para não exceder o limite de [bold]${summary.MaxBudgetUsd:0.000}[/].\n" +
                $"Gasto acumulado da campanha: [bold red]${summary.TotalCampaignSpentUsd:0.0000}[/].\n" +
                $"Use [cyan]resume[/] após ajustar o orçamento se desejar continuar de onde parou.")
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Yellow),
                Header = new PanelHeader(" Alerta de Orçamento ")
            };
            AnsiConsole.Write(warningPanel);
            AnsiConsole.WriteLine();
        }

        if (summary.EnrichmentPausedByDailyQuota)
        {
            var quotaPanel = new Panel(
                $"[bold yellow]COTA DIÁRIA DO GOOGLE CUSTOM SEARCH ATINGIDA ({summary.QuotaUsedToday}/{summary.QuotaDailyLimit})![/]\n" +
                $"[bold]{summary.PendingQuotaCount}[/] lead(s) restante(s) foram marcados como [cyan]PendingQuota[/] e listados na aba [bold]Pendentes_Cota[/].\n" +
                $"Execute [cyan]resume[/] amanhã (UTC) para continuar o enriquecimento sem perder progresso.")
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Orange1),
                Header = new PanelHeader(" Cota Diária Custom Search ")
            };
            AnsiConsole.Write(quotaPanel);
            AnsiConsole.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(summary.SpreadsheetUrl))
        {
            var sheetsPanel = new Panel(
                $"[bold green]Planilha Google Sheets atualizada com sucesso![/]\n" +
                $"Leads exportados nesta sessão ([bold]Leads_Prontos[/]): [bold green]{summary.ExportedLeadsCount}[/]\n" +
                $"Link direto: [link={Markup.Escape(summary.SpreadsheetUrl)}][bold underline cyan]{Markup.Escape(summary.SpreadsheetUrl)}[/][/]")
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green),
                Header = new PanelHeader(" Google Sheets ")
            };
            AnsiConsole.Write(sheetsPanel);
            AnsiConsole.WriteLine();
        }
        else if (!string.IsNullOrWhiteSpace(summary.SheetsExportSkippedReason))
        {
            AnsiConsole.MarkupLine($"[yellow]Aviso Google Sheets:[/] {Markup.Escape(summary.SheetsExportSkippedReason)}");
            AnsiConsole.WriteLine();
        }

        var budgetRatio = summary.MaxBudgetUsd > 0
            ? (summary.TotalCampaignSpentUsd / summary.MaxBudgetUsd) * 100m
            : 0m;
        var budgetColor = budgetRatio >= 90m ? "red" : (budgetRatio >= 70m ? "yellow" : "green");

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn();

        grid.AddRow("[bold]Campanha:[/]", Markup.Escape(summary.CampaignName));
        grid.AddRow("[bold]Modo:[/]", $"[cyan]{summary.Mode}[/]");
        grid.AddRow("[bold]Chamadas Places API:[/]", $"{summary.ApiCallsMade}");
        grid.AddRow("[bold]Chamadas Custom Search:[/]", $"{summary.SearchEnrichCallsMade} (Tier 1: {summary.EnrichedTier1Count} | Tier 2: {summary.EnrichedTier2Count})");
        grid.AddRow("[bold]Cota diária Search (UTC):[/]", $"{summary.QuotaUsedToday} / {summary.QuotaDailyLimit}");
        grid.AddRow("[bold]Análises Gemini:[/]", $"{summary.GeminiAnalyzedCount} (Tokens: {summary.TotalInputTokens:N0} in / {summary.TotalOutputTokens:N0} out)");
        grid.AddRow("[bold]Custo da sessão:[/]", $"${summary.SessionSpentUsd:0.0000}");
        grid.AddRow("[bold]Custo acumulado / Teto:[/]", $"[{budgetColor}]${summary.TotalCampaignSpentUsd:0.0000}[/] / ${summary.MaxBudgetUsd:0.000} ({budgetRatio:0.0}%)");

        if (summary.ProjectedCostForTargetUsd.HasValue)
        {
            grid.AddRow(
                $"[bold]Projeção para {summary.TargetValidLeads} leads:[/]",
                $"[bold cyan]~${summary.ProjectedCostForTargetUsd.Value:0.0000}[/]");
        }

        grid.AddRow("[bold]Leads válidos coletados:[/]", $"[green]{summary.ValidLeadsAdded}[/] (Com celular confirmado na sessão: [bold green]{summary.ConfirmedMobileLeadsCount}[/])");
        grid.AddRow("[bold]Donos identificados (Alta/Média):[/]", $"[bold cyan]{summary.OwnersIdentifiedCount}[/]");
        grid.AddRow("[bold]Exportados para Google Sheets:[/]", $"[bold green]{summary.ExportedLeadsCount}[/] (Duplicados por telefone ignorados: {summary.SkippedExportDuplicatePhoneCount})");
        grid.AddRow("[bold]Descartados (auditoria):[/]", $"[yellow]{summary.DiscardedCount}[/] (Fora do nicho Gemini: {summary.DiscardedByNicheCount})");
        grid.AddRow("[bold]Duplicados PlaceId ignorados:[/]", $"[grey]{summary.SkippedDuplicates}[/]");

        var panel = new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Resumo da Execução ")
        };
        AnsiConsole.Write(panel);

        if (summary.ExportedLeads.Count > 0)
        {
            AnsiConsole.WriteLine();
            RenderLeadsTable(summary.ExportedLeads, "[bold green]Leads Exportados (Leads_Prontos)[/]");
        }
        else
        {
            var leadsToRender = summary.ProcessedLeads.Count > 0 ? summary.ProcessedLeads : summary.NewLeads;
            if (leadsToRender.Count > 0)
            {
                AnsiConsole.WriteLine();
                RenderLeadsTable(leadsToRender, "[bold cyan]Leads Processados na Sessão[/]");
            }
        }

        if (summary.DiscardBreakdown.Count > 0)
        {
            AnsiConsole.WriteLine();
            RenderDiscardBreakdown(summary.DiscardBreakdown);
        }
    }

    public static void RenderLeadsTable(IReadOnlyList<Lead> leads, string title)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title(title)
            .AddColumn("Tier")
            .AddColumn("Empresa")
            .AddColumn("Tel. Principal")
            .AddColumn("Tel. 2")
            .AddColumn("Instagram")
            .AddColumn("Dono (Confiança)")
            .AddColumn("Elogio (Cliente)")
            .AddColumn("Status");

        foreach (var lead in leads.OrderBy(l => l.Tier).ThenByDescending(l => l.Rating))
        {
            var tierLabel = lead.Tier switch
            {
                LeadTier.Tier1HighPriority => "[bold green]T1[/]",
                LeadTier.Tier2MediumPriority => "[bold yellow]T2[/]",
                _ => "[grey]T3[/]"
            };

            var ownerCell = string.IsNullOrWhiteSpace(lead.OwnerName)
                ? "-"
                : $"{lead.OwnerName} ({lead.OwnerConfidence ?? "?"})";

            var praiseCell = string.IsNullOrWhiteSpace(lead.PraisedService)
                ? "-"
                : string.IsNullOrWhiteSpace(lead.ReviewerFirstName)
                    ? lead.PraisedService
                    : $"{lead.PraisedService} ({lead.ReviewerFirstName})";

            var statusLabel = lead.IsExported
                ? "[bold green]Exported[/]"
                : lead.EnrichmentStatus switch
                {
                    EnrichmentStatus.Completed => "[green]Completed[/]",
                    EnrichmentStatus.Enriched => "[cyan]Enriched[/]",
                    EnrichmentStatus.PendingQuota => "[yellow]PendingQuota[/]",
                    EnrichmentStatus.DiscardedByNiche => "[red]OffNiche[/]",
                    _ => "[grey]Pending[/]"
                };

            table.AddRow(
                tierLabel,
                Markup.Escape(Truncate(lead.DisplayName, 26)),
                Markup.Escape(lead.PrimaryPhone ?? lead.MobilePhone ?? "-"),
                Markup.Escape(lead.SecondaryPhone ?? lead.LandlinePhone ?? "-"),
                Markup.Escape(Truncate(lead.InstagramHandle ?? "-", 18)),
                Markup.Escape(Truncate(ownerCell, 22)),
                Markup.Escape(Truncate(praiseCell, 22)),
                statusLabel);
        }

        AnsiConsole.Write(table);
    }

    public static void RenderDiscardBreakdown(IReadOnlyDictionary<string, int> breakdown)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold yellow]Auditoria de Descartes[/]")
            .AddColumn("Motivo")
            .AddColumn(new TableColumn("Quantidade").RightAligned());

        foreach (var item in breakdown.OrderByDescending(kv => kv.Value))
        {
            table.AddRow(Markup.Escape(item.Key), item.Value.ToString());
        }

        AnsiConsole.Write(table);
    }

    public static void RenderStatusReports(IReadOnlyList<CampaignStatusReport> reports)
    {
        if (reports.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nenhuma campanha encontrada no banco de dados local.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold cyan]Status das Campanhas[/]")
            .AddColumn("Campanha")
            .AddColumn("Status")
            .AddColumn(new TableColumn("Leads (Cel.)").RightAligned())
            .AddColumn(new TableColumn("Export.").RightAligned())
            .AddColumn(new TableColumn("Donos").RightAligned())
            .AddColumn(new TableColumn("T1 / T2 / T3").Centered())
            .AddColumn(new TableColumn("Cota Hoje").Centered())
            .AddColumn(new TableColumn("Gasto / Orçamento").RightAligned());

        foreach (var r in reports)
        {
            var statusMarkup = r.Status switch
            {
                CampaignStatus.Completed => "[green]Completed[/]",
                CampaignStatus.PausedByBudget => "[yellow]PausedByBudget[/]",
                CampaignStatus.PausedByQuota => "[orange1]PausedByQuota[/]",
                CampaignStatus.Running => "[cyan]Running[/]",
                CampaignStatus.Failed => "[red]Failed[/]",
                _ => "[grey]Created[/]"
            };

            table.AddRow(
                Markup.Escape(r.CampaignName),
                statusMarkup,
                $"[bold]{r.TotalValidLeads}[/] ([green]{r.ConfirmedMobileCount}[/])",
                $"[bold green]{r.ExportedLeadsCount}[/]",
                $"[cyan]{r.OwnersIdentifiedCount}[/]",
                $"[green]{r.Tier1Count}[/] / [yellow]{r.Tier2Count}[/] / [grey]{r.Tier3Count}[/]",
                $"{r.QuotaUsedToday}/{r.QuotaDailyLimit}",
                $"${r.TotalSpentUsd:0.0000} / ${r.MaxBudgetUsd:0.000}");
        }

        AnsiConsole.Write(table);

        foreach (var report in reports)
        {
            if (!string.IsNullOrWhiteSpace(report.SpreadsheetUrl))
            {
                AnsiConsole.MarkupLine(
                    $"[bold]Planilha ({Markup.Escape(report.CampaignName)}):[/] [link={Markup.Escape(report.SpreadsheetUrl)}][cyan underline]{Markup.Escape(report.SpreadsheetUrl)}[/][/]");
            }

            if (report.DiscardReasons.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold]Descartes — {Markup.Escape(report.CampaignName)}:[/]");
                RenderDiscardBreakdown(report.DiscardReasons);
            }
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
