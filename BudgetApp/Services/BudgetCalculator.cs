using System;
using System.Collections.Generic;
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
            ? m.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : "All time";
    }

    public sealed record PieSlice(string Label, decimal Amount, bool IsLeftOver = false);

    public sealed record BudgetSummary(
        decimal Income,
        decimal OneOffExpenses,
        decimal RecurringExpenses,
        IReadOnlyList<PieSlice> Slices,
        bool SlicesAreShareOfIncome)
    {
        public decimal TotalExpenses => OneOffExpenses + RecurringExpenses;
        public decimal Net => Income - TotalExpenses;

        /// <summary>Share of income not spent (negative when overspending). Null when there is no income.</summary>
        public decimal? SavingsRate => Income > 0 ? Net / Income : null;
    }

    /// <summary>
    /// Pure budget maths, kept out of the window code so it can be tested on its own.
    /// </summary>
    public static class BudgetCalculator
    {
        /// <summary>
        /// How many months a recurring cost counts for in the period. A cost counts from the month
        /// of its start date onwards. For "All time" that is every month from the start up to and
        /// including the month of <paramref name="today"/>.
        /// </summary>
        public static int MonthsActive(RecurringExpense r, BudgetPeriod period, DateTime today)
        {
            if (!r.IsActive) return 0;
            var startMonth = new DateTime(r.StartDate.Year, r.StartDate.Month, 1);

            if (period.Month is DateTime m)
                return startMonth <= m ? 1 : 0;

            var thisMonth = new DateTime(today.Year, today.Month, 1);
            if (startMonth > thisMonth) return 0;
            return (thisMonth.Year - startMonth.Year) * 12 + thisMonth.Month - startMonth.Month + 1;
        }

        public static decimal RecurringCost(RecurringExpense r, BudgetPeriod period, DateTime today) =>
            Math.Round(r.MonthlyAmount * MonthsActive(r, period, today), 2, MidpointRounding.AwayFromZero);

        public static BudgetSummary Summarize(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringExpense> recurring,
            BudgetPeriod period,
            DateTime today)
        {
            var inPeriod = transactions.Where(t => period.Contains(t.Date)).ToList();
            var recurringList = recurring.ToList();

            decimal income = inPeriod.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            decimal oneOff = inPeriod.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            decimal rec = recurringList.Sum(r => RecurringCost(r, period, today));

            var byCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var displayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(string category, decimal amount)
            {
                if (amount <= 0) return;
                var key = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();
                byCategory[key] = byCategory.GetValueOrDefault(key) + amount;
                displayName.TryAdd(key, key);
            }

            foreach (var t in inPeriod.Where(t => t.Type == TransactionType.Expense)) Add(t.Category, t.Amount);
            foreach (var r in recurringList) Add(r.Category, RecurringCost(r, period, today));

            var slices = byCategory
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new PieSlice(displayName[kv.Key], kv.Value))
                .ToList();

            decimal totalExpenses = oneOff + rec;
            bool shareOfIncome = income > 0 && income >= totalExpenses;
            if (shareOfIncome && income - totalExpenses > 0)
                slices.Add(new PieSlice("Left over", income - totalExpenses, IsLeftOver: true));

            return new BudgetSummary(income, oneOff, rec, slices, shareOfIncome);
        }

        /// <summary>Categories the user has used before, after the defaults, without duplicates.</summary>
        public static List<string> CategorySuggestions(
            IEnumerable<string> defaults,
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringExpense> recurring)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var c in defaults
                         .Concat(transactions.Select(t => t.Category))
                         .Concat(recurring.Select(r => r.Category)))
            {
                var trimmed = c?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed)) result.Add(trimmed);
            }
            return result;
        }
    }
}
