using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BudgetApp.Models;

namespace BudgetApp.Services
{
    /// <summary>
    /// The period the dashboard is looking at: one calendar month, or everything.
    /// </summary>
    public readonly record struct BudgetPeriod(DateTime? Month)
    {
        public static BudgetPeriod AllTime => new(null);
        public static BudgetPeriod ForMonth(DateTime anyDayInMonth) => new(new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1));

        public bool IsAllTime => Month is null;

        public bool Contains(DateTime date) =>
            Month is not DateTime m || (date.Year == m.Year && date.Month == m.Month);

        public BudgetPeriod Previous() => Month is DateTime m ? new(m.AddMonths(-1)) : this;
        public BudgetPeriod Next() => Month is DateTime m ? new(m.AddMonths(1)) : this;

        public string Label => Month is DateTime m
            ? m.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            : "All time";
    }

    public sealed record PieSlice(string Label, decimal Amount, bool IsLeftOver = false);

    public sealed record BudgetSummary(
        decimal OneOffIncome,
        decimal RecurringIncome,
        decimal OneOffExpenses,
        decimal RecurringExpenses,
        IReadOnlyList<PieSlice> Slices,
        bool SlicesAreShareOfIncome)
    {
        public decimal Income => OneOffIncome + RecurringIncome;
        public decimal TotalExpenses => OneOffExpenses + RecurringExpenses;
        public decimal Net => Income - TotalExpenses;

        /// <summary>Share of income not spent (negative when overspending). Null when there is no income.</summary>
        public decimal? SavingsRate => Income > 0 ? Net / Income : null;
    }

    /// <summary>Income and spending for one calendar month.</summary>
    public sealed record MonthTotals(DateTime Month, decimal Income, decimal Expenses)
    {
        public decimal Net => Income - Expenses;
        public decimal? SavingsRate => Income > 0 ? Net / Income : null;
        public string Label => Month.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        public string ShortLabel => Month.ToString("MMM", CultureInfo.InvariantCulture);
    }

    /// <summary>How a category is doing against its monthly budget.</summary>
    public sealed record BudgetStatus(string Category, decimal Limit, decimal Spent)
    {
        public decimal Remaining => Limit - Spent;
        public decimal Ratio => Limit > 0 ? Spent / Limit : (Spent > 0 ? 1m : 0m);
        public bool IsOver => Spent > Limit;
        /// <summary>0 = fine, 1 = above 85%, 2 = over budget.</summary>
        public int Level => IsOver ? 2 : Ratio >= 0.85m ? 1 : 0;
    }

    /// <summary>
    /// Pure budget maths, kept out of the window code so it can be tested on its own.
    /// Rules:
    /// - Programmed (recurring) items count as a monthly equivalent in every month from their start date while active.
    /// - Transfers between your own accounts never count.
    /// - Bank rows linked to a programmed item don't count either - the programmed item already does.
    /// </summary>
    public static class BudgetCalculator
    {
        public const string Uncategorized = "Uncategorized";

        /// <summary>
        /// How many months a recurring item counts for in the period. "All time" means every month
        /// from the start up to and including the month of <paramref name="today"/>.
        /// </summary>
        public static int MonthsActive(RecurringItem r, BudgetPeriod period, DateTime today)
        {
            if (!r.IsActive) return 0;
            var startMonth = new DateTime(r.StartDate.Year, r.StartDate.Month, 1);

            if (period.Month is DateTime m)
                return startMonth <= m ? 1 : 0;

            var thisMonth = new DateTime(today.Year, today.Month, 1);
            if (startMonth > thisMonth) return 0;
            return (thisMonth.Year - startMonth.Year) * 12 + thisMonth.Month - startMonth.Month + 1;
        }

        public static decimal RecurringCost(RecurringItem r, BudgetPeriod period, DateTime today) =>
            Math.Round(r.MonthlyAmount * MonthsActive(r, period, today), 2, MidpointRounding.AwayFromZero);

        public static BudgetSummary Summarize(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            BudgetPeriod period,
            DateTime today)
        {
            var inPeriod = transactions.Where(t => t.CountsInTotals && period.Contains(t.Date)).ToList();
            var recurringList = recurring.ToList();

            decimal oneOffIncome = inPeriod.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            decimal oneOff = inPeriod.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            decimal recIncome = recurringList.Where(r => r.IsIncome).Sum(r => RecurringCost(r, period, today));
            decimal recExpense = recurringList.Where(r => !r.IsIncome).Sum(r => RecurringCost(r, period, today));

            var slices = SpendingByCategory(inPeriod, recurringList, period, today)
                .Select(kv => new PieSlice(kv.Key, kv.Value))
                .ToList();

            decimal income = oneOffIncome + recIncome;
            decimal totalExpenses = oneOff + recExpense;
            bool shareOfIncome = income > 0 && income >= totalExpenses;
            if (shareOfIncome && income - totalExpenses > 0)
                slices.Add(new PieSlice("Left over", income - totalExpenses, IsLeftOver: true));

            return new BudgetSummary(oneOffIncome, recIncome, oneOff, recExpense, slices, shareOfIncome);
        }

        /// <summary>Spending per category in the period (one-off + programmed), largest first.</summary>
        public static List<KeyValuePair<string, decimal>> SpendingByCategory(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            BudgetPeriod period,
            DateTime today)
        {
            var byCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var displayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(string category, decimal amount)
            {
                if (amount <= 0) return;
                var key = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();
                byCategory[key] = byCategory.GetValueOrDefault(key) + amount;
                displayName.TryAdd(key, key);
            }

            foreach (var t in transactions.Where(t => t.CountsInTotals && t.Type == TransactionType.Expense && period.Contains(t.Date)))
                Add(t.Category, t.Amount);
            foreach (var r in recurring.Where(r => !r.IsIncome))
                Add(r.Category, RecurringCost(r, period, today));

            return byCategory
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new KeyValuePair<string, decimal>(displayName[kv.Key], kv.Value))
                .ToList();
        }

        /// <summary>Totals for each of the <paramref name="count"/> months ending with <paramref name="lastMonth"/>.</summary>
        public static List<MonthTotals> MonthlyTotals(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            DateTime lastMonth,
            int count,
            DateTime today)
        {
            var txList = transactions.ToList();
            var recList = recurring.ToList();
            var end = new DateTime(lastMonth.Year, lastMonth.Month, 1);
            var result = new List<MonthTotals>();
            for (int i = count - 1; i >= 0; i--)
            {
                var m = end.AddMonths(-i);
                var s = Summarize(txList, recList, BudgetPeriod.ForMonth(m), today);
                result.Add(new MonthTotals(m, s.Income, s.TotalExpenses));
            }
            return result;
        }

        /// <summary>
        /// Months that have any data, so averages don't get dragged down by empty months before
        /// the user started using the app.
        /// </summary>
        public static List<MonthTotals> RecentMonthsWithData(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            DateTime today,
            int maxMonths,
            bool includeCurrentMonth = false)
        {
            var txList = transactions.ToList();
            var recList = recurring.ToList();
            var last = new DateTime(today.Year, today.Month, 1);
            if (!includeCurrentMonth) last = last.AddMonths(-1);

            var firstData = txList.Select(t => (DateTime?)t.Date)
                .Concat(recList.Where(r => r.IsActive).Select(r => (DateTime?)r.StartDate))
                .Min();
            if (firstData is not DateTime first) return new List<MonthTotals>();
            var firstMonth = new DateTime(first.Year, first.Month, 1);
            if (firstMonth > last) return new List<MonthTotals>();

            int available = (last.Year - firstMonth.Year) * 12 + last.Month - firstMonth.Month + 1;
            return MonthlyTotals(txList, recList, last, Math.Min(maxMonths, available), today);
        }

        public static List<BudgetStatus> BudgetStatuses(
            IEnumerable<Budget> budgets,
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            DateTime month,
            DateTime today)
        {
            var spending = SpendingByCategory(transactions, recurring, BudgetPeriod.ForMonth(month), today)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            return budgets
                .Select(b => new BudgetStatus(b.Category, b.MonthlyLimit, spending.GetValueOrDefault(b.Category.Trim())))
                .OrderByDescending(s => s.Ratio)
                .ToList();
        }

        /// <summary>
        /// Suggests a monthly budget per category: the average of the last 3 months with data,
        /// rounded up to the next €10.
        /// </summary>
        public static Dictionary<string, decimal> SuggestBudgets(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            DateTime today,
            int months = 3)
        {
            var txList = transactions.ToList();
            var recList = recurring.ToList();
            var monthsWithData = RecentMonthsWithData(txList, recList, today, months);
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (monthsWithData.Count == 0) return result;

            foreach (var m in monthsWithData)
                foreach (var kv in SpendingByCategory(txList, recList, BudgetPeriod.ForMonth(m.Month), today))
                    result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;

            foreach (var key in result.Keys.ToList())
                result[key] = Math.Ceiling(result[key] / monthsWithData.Count / 10m) * 10m;
            return result;
        }

        /// <summary>Categories the user has used before, after the defaults, without duplicates.</summary>
        public static List<string> CategorySuggestions(
            IEnumerable<string> defaults,
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            IEnumerable<string>? more = null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var c in defaults
                         .Concat(transactions.Select(t => t.Category))
                         .Concat(recurring.Select(r => r.Category))
                         .Concat(more ?? Enumerable.Empty<string>()))
            {
                var trimmed = c?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed)) result.Add(trimmed);
            }
            return result;
        }

        public static readonly string[] DefaultCategories =
        {
            "Salary", "Allowances", "Housing", "Groceries", "Eating out", "Transport", "Utilities",
            "Insurance", "Health", "Subscriptions", "Shopping", "Education", "Going out", "Gifts",
            "Travel", "Savings", "Debt repayment", "Other"
        };
    }

    /// <summary>
    /// Rough "needs / wants / savings" grouping for the 50/30/20 rule, based on category names.
    /// </summary>
    public enum SpendingGroup { Needs, Wants, Savings }

    public static class CategoryGroups
    {
        private static readonly string[] NeedsWords =
        {
            "housing", "rent", "huur", "mortgage", "hypotheek", "grocer", "boodschap", "supermarket",
            "utilit", "energy", "energie", "water", "internet", "phone", "telefoon", "insurance", "verzeker",
            "health", "zorg", "medic", "pharma", "apotheek", "transport", "fuel", "benzine", "travel to work",
            "ov", "childcare", "kinderopvang", "education", "study", "studie", "school", "tax", "belasting",
            "debt", "loan", "lening", "aflossing", "fixed"
        };

        private static readonly string[] SavingsWords = { "saving", "sparen", "spaar", "invest", "beleg", "pension", "pensioen" };

        public static SpendingGroup GroupOf(string category)
        {
            var c = (category ?? "").Trim().ToLowerInvariant();
            if (SavingsWords.Any(w => c.Contains(w))) return SpendingGroup.Savings;
            if (c == "ov" || NeedsWords.Where(w => w != "ov").Any(w => c.Contains(w))) return SpendingGroup.Needs;
            return SpendingGroup.Wants;
        }
    }
}
