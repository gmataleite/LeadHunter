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
                $"Gasto acumulado da campanha: [bold red]${summary.TotalCampaignSpentUsd:0.000}[/].\n" +
                $"Use [cyan]resume[/] após ajustar o orçamento se desejar continuar de onde parou.")
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Yellow),
                Header = new PanelHeader(" Alerta de Orçamento ")
            };
            AnsiConsole.Write(warningPanel);
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
        grid.AddRow("[bold]Chamadas API (sessão):[/]", $"{summary.ApiCallsMade}");
        grid.AddRow("[bold]Custo da sessão:[/]", $"${summary.SessionSpentUsd:0.000}");
        grid.AddRow("[bold]Custo acumulado / Teto:[/]", $"[{budgetColor}]${summary.TotalCampaignSpentUsd:0.000}[/] / ${summary.MaxBudgetUsd:0.000} ({budgetRatio:0.0}%)");
        grid.AddRow("[bold]Novos leads válidos:[/]", $"[green]{summary.ValidLeadsAdded}[/]");
        grid.AddRow("[bold]Descartados (auditoria):[/]", $"[yellow]{summary.DiscardedCount}[/]");
        grid.AddRow("[bold]Duplicados ignorados:[/]", $"[grey]{summary.SkippedDuplicates}[/]");

        var panel = new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Resumo da Execução ")
        };
        AnsiConsole.Write(panel);

        if (summary.NewLeads.Count > 0)
        {
            AnsiConsole.WriteLine();
            RenderLeadsTable(summary.NewLeads);
        }

        if (summary.DiscardBreakdown.Count > 0)
        {
            AnsiConsole.WriteLine();
            RenderDiscardBreakdown(summary.DiscardBreakdown);
        }
    }

    public static void RenderLeadsTable(IReadOnlyList<Lead> leads)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold cyan]Leads Coletados na Sessão[/]")
            .AddColumn("Tier")
            .AddColumn("Empresa")
            .AddColumn("Celular")
            .AddColumn("Fixo")
            .AddColumn(new TableColumn("Nota").RightAligned())
            .AddColumn(new TableColumn("Avaliações").RightAligned())
            .AddColumn("Website / Social");

        foreach (var lead in leads.OrderBy(l => l.Tier).ThenByDescending(l => l.Rating))
        {
            var tierLabel = lead.Tier switch
            {
                LeadTier.Tier1HighPriority => "[bold green]Tier 1 (Alta)[/]",
                LeadTier.Tier2MediumPriority => "[bold yellow]Tier 2 (Média)[/]",
                _ => "[grey]Tier 3 (Baixa)[/]"
            };

            table.AddRow(
                tierLabel,
                Markup.Escape(Truncate(lead.DisplayName, 36)),
                Markup.Escape(lead.MobilePhone ?? "-"),
                Markup.Escape(lead.LandlinePhone ?? "-"),
                lead.Rating?.ToString("0.0") ?? "-",
                lead.UserRatingCount?.ToString() ?? "0",
                Markup.Escape(Truncate(lead.WebsiteUri ?? "-", 34)));
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
            .AddColumn(new TableColumn("Leads").RightAligned())
            .AddColumn(new TableColumn("T1 / T2 / T3").Centered())
            .AddColumn(new TableColumn("Descartados").RightAligned())
            .AddColumn(new TableColumn("Queries").Centered())
            .AddColumn(new TableColumn("Chamadas").RightAligned())
            .AddColumn(new TableColumn("Gasto / Orçamento").RightAligned());

        foreach (var r in reports)
        {
            var statusMarkup = r.Status switch
            {
                CampaignStatus.Completed => "[green]Completed[/]",
                CampaignStatus.PausedByBudget => "[yellow]PausedByBudget[/]",
                CampaignStatus.Running => "[cyan]Running[/]",
                CampaignStatus.Failed => "[red]Failed[/]",
                _ => "[grey]Created[/]"
            };

            table.AddRow(
                Markup.Escape(r.CampaignName),
                statusMarkup,
                $"[bold]{r.TotalValidLeads}[/]",
                $"[green]{r.Tier1Count}[/] / [yellow]{r.Tier2Count}[/] / [grey]{r.Tier3Count}[/]",
                r.TotalDiscardedLeads.ToString(),
                $"{r.CompletedQueries}/{r.TotalQueries}",
                r.TotalApiCalls.ToString(),
                $"${r.TotalSpentUsd:0.000} / ${r.MaxBudgetUsd:0.000}");
        }

        AnsiConsole.Write(table);

        foreach (var report in reports.Where(r => r.DiscardReasons.Count > 0))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]Descartes — {Markup.Escape(report.CampaignName)}:[/]");
            RenderDiscardBreakdown(report.DiscardReasons);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
