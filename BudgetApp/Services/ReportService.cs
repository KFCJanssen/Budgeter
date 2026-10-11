using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BudgetApp.Models;
using BudgetApp.Pages;
using BudgetApp.Planning;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BudgetApp.Services
{
    /// <summary>
    /// Builds the "Financial profile" PDF with QuestPDF (Community license, free for personal use).
    /// Uses QuestPDF's built-in Lato font so it looks the same on every PC.
    /// </summary>
    public static class ReportService
    {
        private const string Ink = "#22243B";
        private const string Muted = "#767A8A";
        private const string Faint = "#9A9DAB";
        private const string Line = "#E4E6ED";
        private const string Soft = "#F4F5F9";
        private const string Accent = "#4361EE";
        private const string Good = "#1F9D8F";
        private const string Bad = "#D64545";
        private const string Warn = "#C77C0E";
        private const string IncomeColor = "#2EC4B6";
        private const string ExpenseColor = "#FF6B6B";

        private static string Eur(decimal d) => Money.Format(d).Replace(' ', ' ');
        private static string Pct(decimal d) => d.ToString("P0", Money.Culture).Replace(' ', ' ');

        public static void Create(FinancialSnapshot s, IReadOnlyList<Advice> advice, string path, IReadOnlyList<RecurringItem>? recurring = null) =>
            Build(s, advice, recurring).GeneratePdf(path);

        /// <summary>PNG images of the pages (used by the self-test to check the layout).</summary>
        public static List<byte[]> CreatePreviewImages(FinancialSnapshot s, IReadOnlyList<Advice> advice, IReadOnlyList<RecurringItem>? recurring = null) =>
            Build(s, advice, recurring).GenerateImages(new ImageGenerationSettings { RasterDpi = 110 }).ToList();

        private static IDocument Build(FinancialSnapshot s, IReadOnlyList<Advice> advice, IReadOnlyList<RecurringItem>? recurring)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
            recurring ??= AppState.Store?.Recurring.ToList() ?? new List<RecurringItem>();
            var period = s.Months.Count > 0 ? $"{s.Months.First().Label} - {s.Months.Last().Label}" : "no complete months yet";

            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

                    page.Header().PaddingBottom(14).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Financial profile").FontSize(22).Bold();
                            var who = string.IsNullOrWhiteSpace(s.Profile.Name) ? "" : s.Profile.Name + "  ·  ";
                            c.Item().Text($"{who}{s.Today.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}  ·  based on {period}").FontColor(Muted);
                        });
                        row.ConstantItem(90).AlignRight().AlignMiddle().Text("Budgeter").FontSize(12).SemiBold().FontColor(Accent);
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(16);
                        col.Item().Element(c => Summary(c, s));
                        if (HasAbout(s.Profile)) col.Item().Element(c => About(c, s));
                        col.Item().Element(c => HealthCheck(c, advice));
                        if (s.Months.Count > 0) col.Item().Element(c => Monthly(c, s));
                        if (s.Categories.Count > 0) col.Item().Element(c => Categories(c, s));
                        if (recurring.Any(r => r.IsActive)) col.Item().Element(c => Programmed(c, recurring));
                        if (s.Assets.Count > 0 || s.Debts.Count > 0) col.Item().Element(c => NetWorth(c, s));
                        if (s.Goals.Count > 0) col.Item().Element(c => Goals(c, s));
                        col.Item().PaddingTop(6).Text(
                            "This report is general information based on your own data and common guidelines (such as a 3-6 month buffer and the 50/30/20 split). " +
                            "It is not personal financial advice. For decisions about mortgages, investing or debt, talk to a qualified adviser.")
                            .FontSize(8).FontColor(Faint).Italic();
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Faint));
                        t.Span("Made with Budgeter  ·  page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            });
        }

        // ------------------------------------------------------------------ sections

        private static void SectionTitle(ColumnDescriptor c, string title, string? sub = null)
        {
            c.Item().Text(title).FontSize(13).Bold();
            if (sub != null) c.Item().PaddingBottom(4).Text(sub).FontColor(Muted).FontSize(8.5f);
            else c.Item().PaddingBottom(4);
        }

        private static void Summary(IContainer container, FinancialSnapshot s)
        {
            var tiles = new List<(string Label, string Value, string Color)>
            {
                ("Average income / month", Eur(s.AvgIncome), Good),
                ("Average spending / month", Eur(s.AvgExpenses), ExpenseColor),
                ("Left over / month", Eur(s.AvgNet), s.AvgNet >= 0 ? Accent : Bad),
                ("Savings rate", s.SavingsRate is decimal r ? Pct(r) : "-", s.SavingsRate is decimal r2 && r2 < 0 ? Bad : Ink),
                ("Net worth", s.Assets.Count + s.Debts.Count > 0 ? Eur(s.NetWorth) : "-", s.NetWorth >= 0 ? Ink : Bad),
                ("Emergency buffer", s.Assets.Count > 0 && s.EmergencyMonths is decimal em ? $"{em:0.0} months" : "-", Ink),
                ("Programmed costs / month", Eur(s.FixedCostsPerMonth), Ink),
                ("Debt payments / month", Eur(s.MonthlyDebtPayments), Ink),
            };
            container.Column(c =>
            {
                c.Spacing(8);
                foreach (var chunk in tiles.Chunk(4))
                {
                    c.Item().Row(row =>
                    {
                        row.Spacing(8);
                        foreach (var t in chunk)
                            row.RelativeItem().Background(Soft).CornerRadius(6).Padding(10).Column(tc =>
                            {
                                tc.Item().Text(t.Label.ToUpperInvariant()).FontSize(7).FontColor(Muted);
                                tc.Item().PaddingTop(3).Text(t.Value).FontSize(14).Bold().FontColor(t.Color);
                            });
                    });
                }
            });
        }

        private static bool HasAbout(ProfileInfo p) =>
            p.BirthYear != null || !string.IsNullOrWhiteSpace(p.Household) || !string.IsNullOrWhiteSpace(p.Housing) ||
            !string.IsNullOrWhiteSpace(p.Work) || !string.IsNullOrWhiteSpace(p.Goals);

        private static void About(IContainer container, FinancialSnapshot s)
        {
            var p = s.Profile;
            var facts = new List<string>();
            if (s.Age is int age) facts.Add($"Age {age}");
            if (!string.IsNullOrWhiteSpace(p.Household)) facts.Add(p.Household);
            if (!string.IsNullOrWhiteSpace(p.Housing)) facts.Add(p.Housing);
            if (!string.IsNullOrWhiteSpace(p.Work)) facts.Add(p.Work);
            container.Column(c =>
            {
                SectionTitle(c, "About you");
                if (facts.Count > 0) c.Item().Text(string.Join("  ·  ", facts));
                if (!string.IsNullOrWhiteSpace(p.Goals))
                    c.Item().PaddingTop(4).Text(t => { t.Span("Your goals: ").SemiBold(); t.Span(p.Goals); });
            });
        }

        private static void HealthCheck(IContainer container, IReadOnlyList<Advice> advice)
        {
            container.Column(c =>
            {
                SectionTitle(c, "Health check", "Your numbers compared with common guidelines, most important first.");
                c.Spacing(6);
                foreach (var a in advice)
                {
                    var (label, color) = a.Level switch
                    {
                        AdviceLevel.Action => ("ACT ON THIS", Bad),
                        AdviceLevel.Attention => ("WORTH A LOOK", Warn),
                        AdviceLevel.Good => ("GOOD", Good),
                        _ => ("INFO", Accent)
                    };
                    c.Item().ShowEntire().Row(row =>
                    {
                        row.ConstantItem(3).Background(color);
                        row.RelativeItem().Background(Soft).PaddingVertical(7).PaddingHorizontal(10).Column(ac =>
                        {
                            ac.Item().Text(t =>
                            {
                                t.Span(label).FontSize(7).Bold().FontColor(color);
                                t.Span("   " + a.Area.ToUpperInvariant()).FontSize(7).FontColor(Faint);
                            });
                            ac.Item().PaddingTop(2).Text(a.Title).SemiBold().FontSize(10.5f);
                            ac.Item().PaddingTop(1).Text(a.Finding).FontColor(Muted);
                            ac.Item().PaddingTop(2).Text(a.Suggestion);
                        });
                    });
                }
            });
        }

        private static void Monthly(IContainer container, FinancialSnapshot s)
        {
            decimal max = Math.Max(1, s.Months.Max(m => Math.Max(m.Income, m.Expenses)));
            container.Column(c =>
            {
                SectionTitle(c, "Month by month");
                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(1.1f); cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); cd.ConstantColumn(45); cd.RelativeColumn(2.2f);
                    });
                    table.Header(h => Headers(h, "Month", ">Income", ">Spending", ">Left over", ">Kept", ""));
                    foreach (var m in s.Months)
                    {
                        table.Cell().Element(Cell).Text(m.Label);
                        table.Cell().Element(Cell).AlignRight().Text(Eur(m.Income));
                        table.Cell().Element(Cell).AlignRight().Text(Eur(m.Expenses));
                        table.Cell().Element(Cell).AlignRight().Text(Eur(m.Net)).FontColor(m.Net >= 0 ? Ink : Bad);
                        table.Cell().Element(Cell).AlignRight().Text(m.SavingsRate is decimal r ? Pct(r) : "-");
                        table.Cell().Element(Cell).PaddingLeft(10).AlignMiddle().Column(bars =>
                        {
                            bars.Item().Element(b => Bar(b, m.Income / max, IncomeColor));
                            bars.Item().PaddingTop(2).Element(b => Bar(b, m.Expenses / max, ExpenseColor));
                        });
                    }
                });
                c.Item().PaddingTop(4).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                    t.Span("Bars: ");
                    t.Span("income").FontColor(IncomeColor).SemiBold();
                    t.Span(" and ");
                    t.Span("spending").FontColor(ExpenseColor).SemiBold();
                });
            });
        }

        private static void Categories(IContainer container, FinancialSnapshot s)
        {
            var cats = s.Categories.Take(14).ToList();
            decimal max = Math.Max(1, cats.Max(x => x.PerMonth));
            container.Column(c =>
            {
                SectionTitle(c, "Where the money goes", $"Average per month over {s.Months.Count} month{(s.Months.Count == 1 ? "" : "s")}, including programmed costs. " +
                    $"Needs {Pct(s.NeedsShare)}, wants {Pct(s.WantsShare)}, saved {Pct(s.SavingsShare)} of income (guideline 50/30/20).");
                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd => { cd.RelativeColumn(1.6f); cd.RelativeColumn(); cd.ConstantColumn(55); cd.ConstantColumn(55); cd.RelativeColumn(); cd.RelativeColumn(1.6f); });
                    table.Header(h => Headers(h, "Category", ">Per month", ">Of income", "Type", ">Budget", ""));
                    foreach (var cat in cats)
                    {
                        bool over = cat.Budget is decimal b && cat.PerMonth > b;
                        table.Cell().Element(Cell).Text(cat.Category);
                        table.Cell().Element(Cell).AlignRight().Text(Eur(cat.PerMonth));
                        table.Cell().Element(Cell).AlignRight().Text(s.AvgIncome > 0 ? Pct(cat.ShareOfIncome) : "-");
                        table.Cell().Element(Cell).PaddingLeft(6).Text(cat.Group.ToString().ToLowerInvariant()).FontColor(Muted);
                        table.Cell().Element(Cell).AlignRight().Text(cat.Budget is decimal bb ? Eur(bb) : "").FontColor(over ? Bad : Ink);
                        table.Cell().Element(Cell).PaddingLeft(10).AlignMiddle().Element(b => Bar(b, cat.PerMonth / max, over ? Bad : Accent));
                    }
                });
            });
        }

        private static void Programmed(IContainer container, IReadOnlyList<RecurringItem> recurring)
        {
            var active = recurring.Where(r => r.IsActive).OrderBy(r => r.IsIncome ? 0 : 1).ThenByDescending(r => r.MonthlyAmount).ToList();
            container.Column(c =>
            {
                SectionTitle(c, "Programmed income and costs",
                    $"Income {Eur(active.Where(r => r.IsIncome).Sum(r => r.MonthlyAmount))} / month  ·  costs {Eur(active.Where(r => !r.IsIncome).Sum(r => r.MonthlyAmount))} / month");
                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(1.5f); cd.RelativeColumn(); cd.RelativeColumn(); });
                    table.Header(h => Headers(h, "Name", "Category", ">Amount", ">Per month"));
                    foreach (var r in active)
                    {
                        var every = r.Frequency switch { RecurringFrequency.Weekly => "week", RecurringFrequency.Yearly => "year", _ => "month" };
                        table.Cell().Element(Cell).Text(r.Name);
                        table.Cell().Element(Cell).Text(r.Category).FontColor(Muted);
                        table.Cell().Element(Cell).AlignRight().Text($"{Eur(r.Amount)} / {every}");
                        table.Cell().Element(Cell).AlignRight().Text((r.IsIncome ? "+" : "-") + Eur(Math.Round(r.MonthlyAmount, 2))).FontColor(r.IsIncome ? Good : Ink);
                    }
                });
            });
        }

        private static void NetWorth(IContainer container, FinancialSnapshot s)
        {
            container.Column(c =>
            {
                SectionTitle(c, "What you own and owe", $"Own {Eur(s.TotalAssets)}  ·  owe {Eur(s.TotalDebts)}  ·  net worth {Eur(s.NetWorth)}");
                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(1.5f); cd.RelativeColumn(); cd.ConstantColumn(55); cd.RelativeColumn(); });
                    table.Header(h => Headers(h, "Name", "Kind", ">Balance", ">Interest", ">Pay / month"));
                    foreach (var b in s.Assets.Concat(s.Debts))
                    {
                        table.Cell().Element(Cell).Text(b.Name);
                        table.Cell().Element(Cell).Text(ProfilePage.KindLabel(b.Kind)).FontColor(Muted);
                        table.Cell().Element(Cell).AlignRight().Text((b.IsDebt ? "-" : "") + Eur(b.Amount)).FontColor(b.IsDebt ? Bad : Good);
                        table.Cell().Element(Cell).AlignRight().Text(b.InterestRate is decimal r ? r.ToString("0.##", Money.Culture) + "%" : "");
                        table.Cell().Element(Cell).AlignRight().Text(b.MonthlyPayment is decimal p ? Eur(p) : "");
                    }
                });
            });
        }

        private static void Goals(IContainer container, FinancialSnapshot s)
        {
            container.Column(c =>
            {
                SectionTitle(c, "Savings goals");
                c.Spacing(6);
                foreach (var g in s.Goals)
                {
                    c.Item().ShowEntire().Column(gc =>
                    {
                        gc.Item().Row(r =>
                        {
                            r.RelativeItem().Text(g.Goal.Name).SemiBold();
                            r.AutoItem().Text($"{Eur(g.Goal.SavedAmount)} of {Eur(g.Goal.TargetAmount)}").FontColor(Muted);
                        });
                        gc.Item().PaddingTop(3).Element(b => Bar(b, g.Ratio, g.Ratio >= 1 ? Good : Accent, 7));
                        var plan = g.Remaining <= 0 ? "Reached."
                            : g.RequiredPerMonth is decimal req && g.Goal.TargetDate is DateTime d
                                ? $"{Eur(g.Remaining)} to go: {Eur(req)} a month to make it by {d.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}."
                                : $"{Eur(g.Remaining)} to go.";
                        gc.Item().PaddingTop(2).Text(plan).FontColor(Muted).FontSize(8.5f);
                    });
                }
            });
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Column titles; a leading '>' right-aligns the title (for number columns).</summary>
        private static void Headers(TableCellDescriptor h, params string[] titles)
        {
            foreach (var t in titles)
            {
                bool right = t.StartsWith('>');
                var cell = h.Cell().Element(HeaderCell);
                (right ? cell.AlignRight() : cell).Text(right ? t[1..] : t);
            }
        }

        private static IContainer HeaderCell(IContainer c) =>
            c.BorderBottom(1).BorderColor(Line).PaddingBottom(4).DefaultTextStyle(t => t.FontSize(7.5f).FontColor(Muted).SemiBold());

        private static IContainer Cell(IContainer c) =>
            c.BorderBottom(0.5f).BorderColor("#EEF0F5").PaddingVertical(4);

        private static void Bar(IContainer c, decimal ratio, string color, float height = 5)
        {
            float r = (float)Math.Clamp(ratio, 0m, 1m);
            c.Height(height).Row(row =>
            {
                if (r > 0.002f) row.RelativeItem(r).Background(color);
                if (r < 0.998f) row.RelativeItem(1 - r).Background("#EEF0F5");
            });
        }
    }
}
