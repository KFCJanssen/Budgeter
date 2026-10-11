using System.Globalization;
using BudgetApp.Models;
using BudgetApp.Services;
using static T;

public static class BudgetTests
{
    public static void Run()
    {
        var today = Today;
        var oct = BudgetPeriod.ForMonth(today);

        // ---------------- Amount parsing ----------------
        foreach (var (input, expected) in new (string, decimal)[]
        {
            ("12.50", 12.50m), ("12,50", 12.50m), ("12", 12m), ("€ 12,50", 12.50m), ("€12.50", 12.50m),
            ("1.234,56", 1234.56m), ("1,234.56", 1234.56m), ("1.250", 1250m), ("1,250", 1250m),
            ("1.000.000", 1000000m), ("2.5", 2.5m), ("0,99", 0.99m), (",5", 0.5m), ("1 234,50", 1234.50m),
            ("12,345", 12345m), ("10.999", 10999m), ("3.14159", 3.14m), ("  7,5  ", 7.5m), ("12 EUR", 12m),
        })
        {
            Test($"parse '{input}' -> {expected}", () =>
            {
                True(Money.TryParse(input, out var v), "should parse");
                Eq(expected, v);
            });
        }

        foreach (var bad in new[] { "", "   ", "abc", "-5", "12,50,1", "1.23.456", "1.234.5678", "1.2.3,4,5", "12a", "€" })
            Test($"reject '{bad}'", () => True(!Money.TryParse(bad, out _), "should not parse"));

        foreach (var (input, expected) in new (string, decimal)[] { ("-30,00", -30m), ("+442,98", 442.98m), ("-1.435,00", -1435m), ("12,50-", -12.5m), ("€ -5", -5m), ("−7,10", -7.1m) })
            Test($"signed parse '{input}' -> {expected}", () => { True(Money.TryParseSigned(input, out var v), "parse"); Eq(expected, v); });

        Test("format uses euro with Dutch separators", () => Eq("€ 1.234,56", Money.Format(1234.56m)));

        // ---------------- Recurring maths ----------------
        Test("weekly -> monthly is 52/12 of the amount", () =>
            Eq(43.33m, Math.Round(Rec("Gym", "Sport", 10m, RecurringFrequency.Weekly, "2026-01-01").MonthlyAmount, 2)));

        Test("yearly -> monthly", () => Eq(10m, Rec("Ins", "Insurance", 120m, RecurringFrequency.Yearly, "2026-01-01").MonthlyAmount));

        Test("recurring counts from its start month", () =>
        {
            var r = Rec("Netflix", "Subscriptions", 13.99m, RecurringFrequency.Monthly, "2026-10-20");
            Eq(1, BudgetCalculator.MonthsActive(r, oct, today), "October");
            Eq(0, BudgetCalculator.MonthsActive(r, oct.Previous(), today), "September");
            Eq(1, BudgetCalculator.MonthsActive(r, oct.Next(), today), "November");
        });

        Test("paused recurring counts for nothing", () =>
            Eq(0, BudgetCalculator.MonthsActive(Rec("Old", "x", 5m, RecurringFrequency.Monthly, "2026-01-01", active: false), oct, today)));

        Test("all time counts every month since start (inclusive)", () =>
        {
            var r = Rec("Rent", "Housing", 800m, RecurringFrequency.Monthly, "2026-08-15");
            Eq(3, BudgetCalculator.MonthsActive(r, BudgetPeriod.AllTime, today), "Aug, Sep, Oct");
            Eq(2400m, BudgetCalculator.RecurringCost(r, BudgetPeriod.AllTime, today));
        });

        Test("all time across a year boundary", () =>
            Eq(13, BudgetCalculator.MonthsActive(Rec("R", "x", 1m, RecurringFrequency.Monthly, "2025-10-31"), BudgetPeriod.AllTime, today)));

        Test("future-start recurring is excluded from all time", () =>
            Eq(0, BudgetCalculator.MonthsActive(Rec("R", "x", 1m, RecurringFrequency.Monthly, "2027-01-01"), BudgetPeriod.AllTime, today)));

        // ---------------- Period summaries ----------------
        var txs = new List<Transaction>
        {
            Tx("2026-09-25", TransactionType.Income, "Salary", 2000m),
            Tx("2026-10-25", TransactionType.Income, "Salary", 2000m),
            Tx("2026-10-02", TransactionType.Expense, "Groceries", 300m),
            Tx("2026-10-05", TransactionType.Expense, "groceries", 50m),
            Tx("2026-09-10", TransactionType.Expense, "Groceries", 999m),
        };
        var recs = new List<RecurringItem>
        {
            Rec("Rent", "Housing", 800m, RecurringFrequency.Monthly, "2026-09-01"),
            Rec("Gym", "Sport", 10m, RecurringFrequency.Weekly, "2026-10-01"),
            Rec("Paused", "Other", 100m, RecurringFrequency.Monthly, "2026-01-01", active: false),
        };

        Test("month summary only includes that month", () =>
        {
            var s = BudgetCalculator.Summarize(txs, recs, oct, today);
            Eq(2000m, s.Income, "income");
            Eq(350m, s.OneOffExpenses, "one-off");
            Eq(843.33m, s.RecurringExpenses, "recurring (800 + 43.33)");
            Eq(2000m - 350m - 843.33m, s.Net, "net");
        });

        Test("September summary excludes recurring that started in October", () =>
        {
            var s = BudgetCalculator.Summarize(txs, recs, oct.Previous(), today);
            Eq(800m, s.RecurringExpenses);
            Eq(999m, s.OneOffExpenses);
        });

        Test("all time: lifetime income with every month of recurring", () =>
        {
            var s = BudgetCalculator.Summarize(txs, recs, BudgetPeriod.AllTime, today);
            Eq(4000m, s.Income);
            Eq(800m * 2 + 43.33m, s.RecurringExpenses, "rent Sep+Oct, gym Oct");
        });

        Test("programmed income counts as income", () =>
        {
            var withSalary = recs.Append(Rec("Salary", "Salary", 2500m, RecurringFrequency.Monthly, "2026-01-01", type: TransactionType.Income)).ToList();
            var s = BudgetCalculator.Summarize(new List<Transaction>(), withSalary, oct, today);
            Eq(2500m, s.RecurringIncome);
            Eq(2500m, s.Income);
            Eq(843.33m, s.TotalExpenses, "salary not an expense");
        });

        Test("transfers and linked bank rows don't count", () =>
        {
            var list = new List<Transaction>
            {
                Tx("2026-10-03", TransactionType.Transfer, "Savings transfer", 200m),
                new() { Date = new DateTime(2026, 10, 1), Type = TransactionType.Expense, Category = "Housing", Amount = 800m, RecurringId = 1 },
                Tx("2026-10-04", TransactionType.Expense, "Fun", 20m),
            };
            var s = BudgetCalculator.Summarize(list, recs, oct, today);
            Eq(20m, s.OneOffExpenses);
            Eq(843.33m, s.RecurringExpenses, "rent counted once, via the programmed item");
        });

        Test("pie: categories merge case-insensitively and a Left over slice completes the income", () =>
        {
            var s = BudgetCalculator.Summarize(txs, recs, oct, today);
            True(s.SlicesAreShareOfIncome, "should be share of income");
            var groceries = s.Slices.Single(x => x.Label.Equals("Groceries", StringComparison.OrdinalIgnoreCase));
            Eq(350m, groceries.Amount, "groceries");
            Eq(s.Net, s.Slices.Single(x => x.IsLeftOver).Amount, "left over");
            Eq(s.Income, s.Slices.Sum(x => x.Amount), "slices sum to income");
        });

        Test("pie: overspending switches to share of spending, no Left over slice", () =>
        {
            var s = BudgetCalculator.Summarize(new[] { Tx("2026-10-01", TransactionType.Income, "Salary", 100m),
                                                       Tx("2026-10-02", TransactionType.Expense, "Fun", 300m) },
                                               Array.Empty<RecurringItem>(), oct, today);
            True(!s.SlicesAreShareOfIncome, "should be share of spending");
            True(s.Slices.All(x => !x.IsLeftOver), "no left over");
            Eq(-2m, s.SavingsRate, "savings rate");
        });

        Test("no income: savings rate is unknown, not a crash", () =>
        {
            var s = BudgetCalculator.Summarize(new[] { Tx("2026-10-02", TransactionType.Expense, "Fun", 30m) },
                                               Array.Empty<RecurringItem>(), oct, today);
            Eq<decimal?>(null, s.SavingsRate);
        });

        Test("monthly totals give one row per month, oldest first", () =>
        {
            var m = BudgetCalculator.MonthlyTotals(txs, recs, today, 3, today);
            Eq(3, m.Count);
            Eq(new DateTime(2026, 8, 1), m[0].Month);
            Eq(2000m, m[1].Income, "September income");
            Eq(999m + 800m, m[1].Expenses, "September spending");
        });

        Test("recent months with data skip empty months before the first entry and the current month", () =>
        {
            var m = BudgetCalculator.RecentMonthsWithData(txs, recs, today, 12);
            Eq(1, m.Count, "only September is complete");
            Eq(new DateTime(2026, 9, 1), m[0].Month);
        });

        Test("budget status per category", () =>
        {
            var budgets = new[] { new Budget { Category = "Groceries", MonthlyLimit = 300m }, new Budget { Category = "Housing", MonthlyLimit = 900m } };
            var st = BudgetCalculator.BudgetStatuses(budgets, txs, recs, today, today);
            var g = st.Single(x => x.Category == "Groceries");
            Eq(350m, g.Spent); True(g.IsOver, "over"); Eq(2, g.Level);
            var h = st.Single(x => x.Category == "Housing");
            Eq(800m, h.Spent); Eq(1, h.Level, "800 of 900 = 89% -> warning");
        });

        Test("budget suggestions round the average up to €10", () =>
        {
            var sug = BudgetCalculator.SuggestBudgets(txs, recs, today);
            Eq(1000m, sug["Groceries"], "999 -> 1000");
            Eq(800m, sug["Housing"]);
        });

        Test("category suggestions: defaults first, then used ones, no duplicates", () =>
        {
            var list = BudgetCalculator.CategorySuggestions(new[] { "Salary", "Groceries" }, txs, recs);
            Eq("Salary", list[0]);
            Eq(1, list.Count(c => c.Equals("groceries", StringComparison.OrdinalIgnoreCase)), "groceries once");
            True(list.Contains("Housing") && list.Contains("Sport"), "used categories included");
        });

        Test("category groups for 50/30/20", () =>
        {
            Eq(SpendingGroup.Needs, CategoryGroups.GroupOf("Housing"));
            Eq(SpendingGroup.Needs, CategoryGroups.GroupOf("Groceries"));
            Eq(SpendingGroup.Wants, CategoryGroups.GroupOf("Eating out"));
            Eq(SpendingGroup.Savings, CategoryGroups.GroupOf("Savings"));
            Eq(SpendingGroup.Wants, CategoryGroups.GroupOf("Lovely things"), "'ov' must not match inside words");
        });

        // ---------------- CSV ----------------
        Test("CSV for Dutch Excel uses ';' and decimal comma, rows add up", () =>
        {
            var csv = CsvExportService.Build(txs, recs, oct, today, CultureInfo.GetCultureInfo("nl-NL"));
            var lines = csv.Split(Environment.NewLine);
            Eq("Budget Summary;October 2026", lines[0]);
            Eq("Total Income;2000,00", lines[1]);
            True(lines.Contains("2026-10-02;Expense;Groceries;300,00;;;"), "one-off row: " + string.Join(" | ", lines.Skip(5).Take(3)));
            True(lines.Any(l => l.StartsWith("2026-10-01;Expense (Programmed);Sport;43,33;")), "gym row");
            True(!csv.Contains("999"), "other month excluded");
            True(!csv.Contains("Paused"), "paused excluded");
        });

        Test("CSV for US Excel uses ',' and decimal point", () =>
        {
            var csv = CsvExportService.Build(txs, recs, oct, today, CultureInfo.GetCultureInfo("en-US"));
            True(csv.Contains("Total Income,2000.00"), csv.Split(Environment.NewLine)[1]);
        });

        Test("CSV escapes separators and quotes", () =>
        {
            Eq("\"a;b\"", CsvExportService.Escape("a;b", ";"));
            Eq("\"say \"\"hi\"\"\"", CsvExportService.Escape("say \"hi\"", ","));
            Eq("a,b", CsvExportService.Escape("a,b", ";"));
        });

        // ---------------- Updates ----------------
        Test("version tags compare correctly", () =>
        {
            True(VersionCheck.IsNewer("v1.3", new Version(1, 2, 0, 0)), "1.3 > 1.2");
            True(!VersionCheck.IsNewer("v1.2", new Version(1, 2, 0, 0)), "1.2 == 1.2.0.0");
            True(VersionCheck.IsNewer("v1.10", new Version(1, 9)), "1.10 > 1.9");
            True(!VersionCheck.IsNewer("nonsense", new Version(1, 0)), "bad tag");
            True(VersionCheck.IsNewer("2", new Version(1, 9)), "2 > 1.9");
        });
    }
}
