using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Planning
{
    /// <summary>What the user tells about themselves on the Profile page (stored as settings).</summary>
    public sealed class ProfileInfo
    {
        public string Name { get; set; } = "";
        public int? BirthYear { get; set; }
        public string Household { get; set; } = "";
        public string Housing { get; set; } = "";
        public string Work { get; set; } = "";
        public string Goals { get; set; } = "";
        /// <summary>How many past months the report and health check look at.</summary>
        public int ReportMonths { get; set; } = 6;
    }

    public sealed record CategoryAverage(string Category, decimal PerMonth, decimal ShareOfIncome, SpendingGroup Group, decimal? Budget);

    public sealed record GoalProgress(Goal Goal, decimal Remaining, int? MonthsLeft, decimal? RequiredPerMonth)
    {
        public decimal Ratio => Goal.TargetAmount > 0 ? Math.Min(1m, Goal.SavedAmount / Goal.TargetAmount) : 0m;
    }

    /// <summary>
    /// Everything the health check and the PDF report need, computed from your data.
    /// Averages use full months only (the current, unfinished month is left out) and skip
    /// months before you started using the app.
    /// </summary>
    public sealed class FinancialSnapshot
    {
        public DateTime Today { get; init; }
        public ProfileInfo Profile { get; init; } = new();
        public int? Age { get; init; }
        public IReadOnlyList<MonthTotals> Months { get; init; } = Array.Empty<MonthTotals>();
        public bool HasData => Months.Count > 0 && Months.Any(m => m.Income > 0 || m.Expenses > 0);

        public decimal AvgIncome { get; init; }
        public decimal AvgExpenses { get; init; }
        public decimal AvgNet => AvgIncome - AvgExpenses;
        public decimal? SavingsRate => AvgIncome > 0 ? AvgNet / AvgIncome : null;
        /// <summary>How much income varies month to month (0 = always the same).</summary>
        public decimal IncomeVariation { get; init; }

        public decimal FixedCostsPerMonth { get; init; }
        public decimal ProgrammedIncomePerMonth { get; init; }
        public decimal EssentialsPerMonth { get; init; }
        public IReadOnlyList<CategoryAverage> Categories { get; init; } = Array.Empty<CategoryAverage>();
        public decimal NeedsShare { get; init; }
        public decimal WantsShare { get; init; }
        public decimal SavingsShare { get; init; }

        public IReadOnlyList<BalanceItem> Assets { get; init; } = Array.Empty<BalanceItem>();
        public IReadOnlyList<BalanceItem> Debts { get; init; } = Array.Empty<BalanceItem>();
        public decimal TotalAssets => Assets.Sum(a => a.Amount);
        public decimal TotalDebts => Debts.Sum(d => d.Amount);
        public decimal NetWorth => TotalAssets - TotalDebts;
        public decimal LiquidSavings => Assets.Where(a => a.IsLiquid).Sum(a => a.Amount);
        public decimal? EmergencyMonths { get; init; }
        public decimal MonthlyDebtPayments => Debts.Sum(d => d.MonthlyPayment ?? 0);
        public decimal? DebtPaymentShare => AvgIncome > 0 ? MonthlyDebtPayments / AvgIncome : null;

        public IReadOnlyList<string> CategoriesOverBudget { get; init; } = Array.Empty<string>();
        public int SubscriptionCount { get; init; }
        public decimal SubscriptionsPerMonth { get; init; }
        /// <summary>Change of spending in the last 3 months vs the 3 before (0.15 = 15% more). Null if not enough data.</summary>
        public decimal? SpendingTrend { get; init; }
        public IReadOnlyList<GoalProgress> Goals { get; init; } = Array.Empty<GoalProgress>();

        public static FinancialSnapshot Build(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurring,
            IEnumerable<Budget> budgets,
            IEnumerable<Goal> goals,
            IEnumerable<BalanceItem> balanceItems,
            ProfileInfo profile,
            DateTime today)
        {
            var tx = transactions.ToList();
            var rec = recurring.ToList();
            var budgetList = budgets.ToList();
            var balances = balanceItems.ToList();
            int monthsWanted = Math.Clamp(profile.ReportMonths, 1, 24);

            var months = BudgetCalculator.RecentMonthsWithData(tx, rec, today, monthsWanted);
            if (months.Count == 0)
                months = BudgetCalculator.RecentMonthsWithData(tx, rec, today, 1, includeCurrentMonth: true);
            int n = Math.Max(1, months.Count);

            decimal avgIncome = months.Sum(m => m.Income) / n;
            decimal avgExpenses = months.Sum(m => m.Expenses) / n;

            decimal variation = 0;
            if (months.Count >= 3 && avgIncome > 0)
            {
                double mean = (double)avgIncome;
                double sd = Math.Sqrt(months.Sum(m => Math.Pow((double)m.Income - mean, 2)) / months.Count);
                variation = (decimal)(sd / mean);
            }

            // Category averages over the same months.
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in months)
                foreach (var kv in BudgetCalculator.SpendingByCategory(tx, rec, BudgetPeriod.ForMonth(m.Month), today))
                    totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
            var budgetByCat = budgetList.GroupBy(b => b.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().MonthlyLimit, StringComparer.OrdinalIgnoreCase);
            var categories = totals
                .Select(kv => new CategoryAverage(kv.Key, Math.Round(kv.Value / n, 2), avgIncome > 0 ? kv.Value / n / avgIncome : 0,
                    CategoryGroups.GroupOf(kv.Key), budgetByCat.TryGetValue(kv.Key, out var b) ? b : null))
                .OrderByDescending(c => c.PerMonth)
                .ToList();

            decimal needs = categories.Where(c => c.Group == SpendingGroup.Needs).Sum(c => c.PerMonth);
            decimal wants = categories.Where(c => c.Group == SpendingGroup.Wants).Sum(c => c.PerMonth);
            decimal savingsCats = categories.Where(c => c.Group == SpendingGroup.Savings).Sum(c => c.PerMonth);
            decimal saved = Math.Max(0, avgIncome - avgExpenses) + savingsCats;

            decimal fixedCosts = rec.Where(r => r.IsActive && !r.IsIncome).Sum(r => r.MonthlyAmount);
            decimal programmedIncome = rec.Where(r => r.IsActive && r.IsIncome).Sum(r => r.MonthlyAmount);
            decimal essentials = needs > 0 ? needs : avgExpenses;

            var assets = balances.Where(b => !b.IsDebt && b.Amount > 0).OrderByDescending(b => b.Amount).ToList();
            var debts = balances.Where(b => b.IsDebt && b.Amount > 0).OrderByDescending(b => b.Amount).ToList();
            decimal liquid = assets.Where(a => a.IsLiquid).Sum(a => a.Amount);

            // Budgets: over the limit in at least 2 of the last 3 months.
            var over = new List<string>();
            var last3 = months.TakeLast(3).ToList();
            foreach (var b in budgetList)
            {
                int overCount = last3.Count(m =>
                    BudgetCalculator.SpendingByCategory(tx, rec, BudgetPeriod.ForMonth(m.Month), today)
                        .Where(kv => string.Equals(kv.Key, b.Category.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Sum(kv => kv.Value) > b.MonthlyLimit);
                if (overCount >= 2 || (last3.Count == 1 && overCount == 1)) over.Add(b.Category);
            }

            var subs = rec.Where(r => r.IsActive && !r.IsIncome &&
                                      (r.Category.Contains("subscri", StringComparison.OrdinalIgnoreCase) ||
                                       r.Category.Contains("abonnement", StringComparison.OrdinalIgnoreCase))).ToList();

            decimal? trend = null;
            if (months.Count >= 6)
            {
                var recent = months.TakeLast(3).Average(m => m.Expenses);
                var before = months.Skip(months.Count - 6).Take(3).Average(m => m.Expenses);
                if (before > 0) trend = (recent - before) / before;
            }

            var goalProgress = goals.Select(g =>
            {
                decimal remaining = Math.Max(0, g.TargetAmount - g.SavedAmount);
                int? monthsLeft = g.TargetDate is DateTime d
                    ? Math.Max(0, (d.Year - today.Year) * 12 + d.Month - today.Month)
                    : null;
                decimal? required = monthsLeft is int ml && remaining > 0 ? Math.Round(remaining / Math.Max(1, ml), 2) : null;
                return new GoalProgress(g, remaining, monthsLeft, required);
            }).ToList();

            int? age = profile.BirthYear is int y && y > 1900 && y <= today.Year ? today.Year - y : null;

            return new FinancialSnapshot
            {
                Today = today,
                Profile = profile,
                Age = age,
                Months = months,
                AvgIncome = Math.Round(avgIncome, 2),
                AvgExpenses = Math.Round(avgExpenses, 2),
                IncomeVariation = Math.Round(variation, 3),
                FixedCostsPerMonth = Math.Round(fixedCosts, 2),
                ProgrammedIncomePerMonth = Math.Round(programmedIncome, 2),
                EssentialsPerMonth = Math.Round(essentials, 2),
                Categories = categories,
                NeedsShare = avgIncome > 0 ? needs / avgIncome : 0,
                WantsShare = avgIncome > 0 ? wants / avgIncome : 0,
                SavingsShare = avgIncome > 0 ? saved / avgIncome : 0,
                Assets = assets,
                Debts = debts,
                EmergencyMonths = Calculators.MonthsCovered(liquid, essentials),
                CategoriesOverBudget = over,
                SubscriptionCount = subs.Count,
                SubscriptionsPerMonth = Math.Round(subs.Sum(s => s.MonthlyAmount), 2),
                SpendingTrend = trend,
                Goals = goalProgress
            };
        }
    }

    public enum AdviceLevel { Good, Info, Attention, Action }

    public sealed record Advice(AdviceLevel Level, string Area, string Title, string Finding, string Suggestion);

    /// <summary>
    /// Transparent, rule-based health check. Every item shows the numbers it is based on and refers to a
    /// common guideline (e.g. a 3-6 month emergency buffer, the 50/30/20 split). It is general
    /// information to think about, not personal financial advice.
    /// </summary>
    public static class AdviceEngine
    {
        private static string Eur(decimal d) => Money.Format(d);
        private static string Pct(decimal d) => d.ToString("P0", Money.Culture);

        public static List<Advice> Evaluate(FinancialSnapshot s)
        {
            var list = new List<Advice>();

            if (!s.HasData)
            {
                list.Add(new Advice(AdviceLevel.Info, "Getting started", "Not enough data yet",
                    "There are no complete months with income or spending to look at.",
                    "Import a few months of bank transactions (Import page) or add your income and costs, then check again."));
                AddBalanceSheetAdvice(s, list);
                return list;
            }

            // 1. Savings rate
            if (s.SavingsRate is decimal rate)
            {
                if (rate < 0)
                    list.Add(new Advice(AdviceLevel.Action, "Spending", "You spend more than you earn",
                        $"On average you spent {Eur(s.AvgExpenses)} a month against {Eur(s.AvgIncome)} income: {Eur(-s.AvgNet)} short each month.",
                        "Look at the biggest categories below and pick one or two to cut back, or set budgets for them. A shortfall that continues eats into savings or builds debt."));
                else if (rate < 0.10m)
                    list.Add(new Advice(AdviceLevel.Attention, "Saving", $"You keep {Pct(rate)} of your income",
                        $"Average income {Eur(s.AvgIncome)}, spending {Eur(s.AvgExpenses)}, left over {Eur(s.AvgNet)} a month.",
                        "A common guideline is to save 10-20% of net income. Even a small automatic transfer to savings on payday helps."));
                else if (rate < 0.20m)
                    list.Add(new Advice(AdviceLevel.Good, "Saving", $"You keep {Pct(rate)} of your income",
                        $"About {Eur(s.AvgNet)} a month is left over on average.",
                        "That's within the common 10-20% range. Make sure the left-over money actually moves to savings or your goals."));
                else
                    list.Add(new Advice(AdviceLevel.Good, "Saving", $"Strong savings rate: {Pct(rate)}",
                        $"About {Eur(s.AvgNet)} a month is left over on average.",
                        "Well above the common 20% guideline. Once your buffer is in place, give the surplus a purpose: goals, paying off debt, or long-term investing."));
            }
            else
            {
                list.Add(new Advice(AdviceLevel.Info, "Income", "No income recorded",
                    "Spending was recorded but no income in these months.",
                    "Add your income (or import it) so the savings rate and the other checks can be calculated."));
            }

            // 2. Emergency fund
            if (s.Assets.Count == 0)
            {
                list.Add(new Advice(AdviceLevel.Info, "Buffer", "Add your savings to check your buffer",
                    "No account balances are filled in on the Profile page.",
                    "Add your current and savings account balances so the app can tell how many months of costs your savings would cover."));
            }
            else if (s.EmergencyMonths is decimal em)
            {
                if (em < 1)
                    list.Add(new Advice(AdviceLevel.Action, "Buffer", $"Your buffer covers {em:0.0} months",
                        $"Liquid savings {Eur(s.LiquidSavings)} vs. about {Eur(s.EssentialsPerMonth)} of essential costs a month.",
                        "A common guideline is 3-6 months of essential costs for unexpected bills or loss of income. Building a first month's buffer is a good first goal."));
                else if (em < 3)
                    list.Add(new Advice(AdviceLevel.Attention, "Buffer", $"Your buffer covers {em:0.0} months",
                        $"Liquid savings {Eur(s.LiquidSavings)} vs. about {Eur(s.EssentialsPerMonth)} of essential costs a month.",
                        $"The common guideline is 3-6 months: between {Eur(s.EssentialsPerMonth * 3)} and {Eur(s.EssentialsPerMonth * 6)}. Consider a savings goal for the difference."));
                else if (em <= 12)
                    list.Add(new Advice(AdviceLevel.Good, "Buffer", $"Your buffer covers {em:0.0} months",
                        $"Liquid savings {Eur(s.LiquidSavings)} vs. about {Eur(s.EssentialsPerMonth)} of essential costs a month.",
                        "That meets the common 3-6 month guideline."));
                else
                    list.Add(new Advice(AdviceLevel.Info, "Buffer", $"Large cash buffer: {em:0.0} months",
                        $"Liquid savings {Eur(s.LiquidSavings)} vs. about {Eur(s.EssentialsPerMonth)} of essential costs a month.",
                        "Savings accounts often pay less than inflation. If you have no near-term plans for this money, it may be worth looking into paying off debt or long-term options, such as investing. That depends on your situation and how much risk you are comfortable with."));
            }

            // 3. High-interest debt
            var expensive = s.Debts.Where(d => (d.InterestRate ?? 0) >= 8m).ToList();
            if (expensive.Count > 0)
                list.Add(new Advice(AdviceLevel.Action, "Debt", "Expensive debt",
                    string.Join(", ", expensive.Select(d => $"{d.Name}: {Eur(d.Amount)} at {d.InterestRate:0.#}%")) + ".",
                    "Debt at 8% or more costs more than savings or investments usually earn. Paying it off first (highest rate first) is usually the best 'return'. The Debt payoff calculator shows how fast."));

            // 4. Debt payments vs income
            if (s.DebtPaymentShare is decimal dps && s.MonthlyDebtPayments > 0)
            {
                if (dps > 0.30m)
                    list.Add(new Advice(AdviceLevel.Attention, "Debt", $"Debt payments take {Pct(dps)} of income",
                        $"{Eur(s.MonthlyDebtPayments)} a month in repayments vs. {Eur(s.AvgIncome)} income.",
                        "Above roughly 30% leaves little room for surprises. Avoid new loans and see whether repayments can be spread or reduced."));
                else
                    list.Add(new Advice(AdviceLevel.Good, "Debt", $"Debt payments take {Pct(dps)} of income",
                        $"{Eur(s.MonthlyDebtPayments)} a month in repayments.", "That's a manageable share."));
            }

            // 5. 50/30/20
            if (s.AvgIncome > 0)
            {
                var finding = $"Needs {Pct(s.NeedsShare)}, wants {Pct(s.WantsShare)}, saved {Pct(s.SavingsShare)} of income (guideline 50/30/20).";
                if (s.NeedsShare > 0.60m)
                    list.Add(new Advice(AdviceLevel.Attention, "Spending", "Fixed needs take a large share", finding,
                        "When essentials take more than about 60%, there's little room left. The biggest lever is usually housing, transport or insurance - compare contracts once a year."));
                else if (s.WantsShare > 0.40m)
                    list.Add(new Advice(AdviceLevel.Attention, "Spending", "Lots of spending on wants", finding,
                        "Setting budgets for the largest 'wants' categories (eating out, shopping, going out) is the easiest way to free up money."));
                else
                    list.Add(new Advice(AdviceLevel.Good, "Spending", "Balanced spending", finding,
                        "Category groups are estimated from category names, so treat this as a rough indicator."));
            }

            // 6. Budgets
            if (s.CategoriesOverBudget.Count > 0)
                list.Add(new Advice(AdviceLevel.Attention, "Budgets", "Budgets that keep running over",
                    $"Over budget in most recent months: {string.Join(", ", s.CategoriesOverBudget)}.",
                    "Either the budget is unrealistic (raise it and lower another one), or this is a spending habit worth looking at."));

            // 7. Subscriptions
            if (s.SubscriptionCount >= 4 || (s.AvgIncome > 0 && s.SubscriptionsPerMonth / s.AvgIncome > 0.05m))
                list.Add(new Advice(AdviceLevel.Info, "Subscriptions", $"{s.SubscriptionCount} subscriptions, {Eur(s.SubscriptionsPerMonth)} a month",
                    $"That's {Eur(s.SubscriptionsPerMonth * 12)} a year.",
                    "Go through the list on the Programmed page and pause what you don't use every month."));

            // 8. Spending trend
            if (s.SpendingTrend is decimal t && t > 0.15m)
                list.Add(new Advice(AdviceLevel.Attention, "Spending", $"Spending is up {Pct(t)}",
                    "Your last 3 months cost more than the 3 months before.",
                    "Check the Trends page to see which categories grew. Sometimes it's a one-off; if not, adjust your budgets."));

            // 9. Irregular income
            if (s.IncomeVariation > 0.30m)
                list.Add(new Advice(AdviceLevel.Info, "Income", "Your income varies a lot",
                    $"Monthly income moves around by about {Pct(s.IncomeVariation)} from the average.",
                    "With irregular income, a bigger buffer (closer to 6 months) and budgeting on a low month instead of an average month helps."));

            // 10. Goals
            foreach (var g in s.Goals.Where(g => g.Remaining > 0 && g.RequiredPerMonth is decimal))
            {
                decimal req = g.RequiredPerMonth!.Value;
                bool onTrack = s.AvgNet >= req;
                list.Add(new Advice(onTrack ? AdviceLevel.Good : AdviceLevel.Attention, "Goals",
                    onTrack ? $"\"{g.Goal.Name}\" looks reachable" : $"\"{g.Goal.Name}\" needs more per month",
                    $"{Eur(g.Remaining)} to go in {g.MonthsLeft} months: {Eur(req)} a month. " +
                    (s.AvgNet > 0 ? $"You have about {Eur(s.AvgNet)} a month left over." : "Right now you spend more than you earn, so nothing is left over for it yet."),
                    onTrack ? "Set up an automatic transfer on payday so it actually happens."
                            : "Move the target date, lower the target, or free up money in your budget."));
            }

            AddBalanceSheetAdvice(s, list);
            return list.OrderByDescending(a => a.Level).ToList();
        }

        private static void AddBalanceSheetAdvice(FinancialSnapshot s, List<Advice> list)
        {
            var student = s.Debts.Where(d => d.Kind == BalanceKind.StudentLoan).ToList();
            if (student.Count > 0)
                list.Add(new Advice(AdviceLevel.Info, "Debt", "Student debt",
                    $"Student debt of {Eur(student.Sum(d => d.Amount))} is filled in.",
                    "Student debt usually has a low interest rate and repayments that depend on income. Check the current rate and your repayment amount in Mijn DUO; the Student loan calculator shows the effect of paying extra. Mortgage lenders take student debt into account."));
        }
    }
}
