using System.Globalization;
using BudgetApp.Models;
using BudgetApp.Services;

// Tiny test runner: each Test(...) prints PASS/FAIL; the process exits non-zero on any failure.
int failures = 0, passed = 0;

void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"PASS  {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL  {name}\n      {ex.Message}"); }
}

void Eq<T>(T expected, T actual, string what = "")
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{what} expected <{expected}> but got <{actual}>");
}

void True(bool cond, string what) { if (!cond) throw new Exception(what); }

var today = new DateTime(2026, 10, 11);
var oct = BudgetPeriod.ForMonth(today);

Transaction Tx(string date, TransactionType type, string cat, decimal amount, string desc = "") =>
    new() { Date = DateTime.Parse(date, CultureInfo.InvariantCulture), Type = type, Category = cat, Amount = amount, Description = desc };

RecurringExpense Rec(string name, string cat, decimal amount, RecurringFrequency f, string start, bool active = true) =>
    new() { Name = name, Category = cat, Amount = amount, Frequency = f, StartDate = DateTime.Parse(start, CultureInfo.InvariantCulture), IsActive = active };

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
    Tx("2026-10-05", TransactionType.Expense, "groceries", 50m),   // different case, same category
    Tx("2026-09-10", TransactionType.Expense, "Groceries", 999m),  // other month
};
var recs = new List<RecurringExpense>
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

Test("old bug: lifetime income no longer mixed with a single month of recurring", () =>
{
    var s = BudgetCalculator.Summarize(txs, recs, BudgetPeriod.AllTime, today);
    Eq(4000m, s.Income);
    Eq(800m * 2 + 43.33m, s.RecurringExpenses, "rent Sep+Oct, gym Oct");
});

Test("pie: categories merge case-insensitively and a Left over slice completes the income", () =>
{
    var s = BudgetCalculator.Summarize(txs, recs, oct, today);
    True(s.SlicesAreShareOfIncome, "should be share of income");
    var groceries = s.Slices.Single(x => x.Label.Equals("Groceries", StringComparison.OrdinalIgnoreCase));
    Eq(350m, groceries.Amount, "groceries");
    var left = s.Slices.Single(x => x.IsLeftOver);
    Eq(s.Net, left.Amount, "left over");
    Eq(s.Income, s.Slices.Sum(x => x.Amount), "slices sum to income");
});

Test("pie: overspending switches to share of spending, no Left over slice", () =>
{
    var s = BudgetCalculator.Summarize(new[] { Tx("2026-10-01", TransactionType.Income, "Salary", 100m),
                                               Tx("2026-10-02", TransactionType.Expense, "Fun", 300m) },
                                       Array.Empty<RecurringExpense>(), oct, today);
    True(!s.SlicesAreShareOfIncome, "should be share of spending");
    True(s.Slices.All(x => !x.IsLeftOver), "no left over");
    Eq(-2m, s.SavingsRate, "savings rate");
});

Test("no income: savings rate is unknown, not a crash", () =>
{
    var s = BudgetCalculator.Summarize(new[] { Tx("2026-10-02", TransactionType.Expense, "Fun", 30m) },
                                       Array.Empty<RecurringExpense>(), oct, today);
    Eq<decimal?>(null, s.SavingsRate);
});

Test("category suggestions: defaults first, then used ones, no duplicates", () =>
{
    var list = BudgetCalculator.CategorySuggestions(new[] { "Salary", "Groceries" }, txs, recs);
    Eq("Salary", list[0]);
    Eq(1, list.Count(c => c.Equals("groceries", StringComparison.OrdinalIgnoreCase)), "groceries once");
    True(list.Contains("Housing") && list.Contains("Sport"), "used categories included");
});

// ---------------- CSV ----------------
Test("CSV for Dutch Excel uses ';' and decimal comma, rows add up", () =>
{
    var csv = CsvExportService.Build(txs, recs, oct, today, CultureInfo.GetCultureInfo("nl-NL"));
    var lines = csv.Split(Environment.NewLine);
    Eq("Budget Summary;October 2026", lines[0]);
    Eq("Total Income;2000,00", lines[1]);
    True(lines.Contains("2026-10-02;Expense;Groceries;300,00;"), "one-off row");
    True(lines.Any(l => l.StartsWith("2026-10-01;Expense (Recurring);Sport;43,33;")), "gym row");
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

Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
