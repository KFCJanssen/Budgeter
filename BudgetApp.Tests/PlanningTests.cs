using BudgetApp.Models;
using BudgetApp.Planning;
using static T;

public static class PlanningTests
{
    public static void Run()
    {
        // ---------------- Calculators ----------------
        Test("compound interest: no interest is just the deposits", () =>
        {
            var r = Calculators.CompoundInterest(1000m, 100m, 0m, 2);
            Eq(3400m, r.FinalValue);
            Eq(0m, r.InterestEarned);
            Eq(2, r.Years.Count);
        });

        Test("compound interest: 5% on €10.000 for 10 years (monthly compounding)", () =>
            Near(16470.09m, Calculators.CompoundInterest(10000m, 0m, 5m, 10).FinalValue, 0.05m));

        Test("months to reach a goal and required monthly deposit agree", () =>
        {
            var monthly = Calculators.RequiredMonthly(5000m, 1000m, 24, 3m);
            var months = Calculators.MonthsToReach(5000m, 1000m, monthly, 3m);
            Eq(24, months);
            Eq(0, Calculators.MonthsToReach(100m, 200m, 0m, 0m), "already there");
            Eq<int?>(null, Calculators.MonthsToReach(100m, 0m, 0m, 0m), "never");
            Eq(0m, Calculators.RequiredMonthly(100m, 200m, 12, 0m));
        });

        Test("annuity: €200.000 at 4% over 30 years ≈ €954,83 a month", () =>
        {
            var a = Calculators.Annuity(200000m, 4m, 360);
            Near(954.83m, a.FirstPayment, 0.01m);
            Near(a.FirstPayment, a.LastPayment, 0.05m, "same payment every month");
            Near(143739m, a.TotalInterest, 5m);
            Eq(30, a.Years.Count);
            Eq(0m, a.Years[^1].Balance);
        });

        Test("linear: starts higher, ends lower, less interest than annuity", () =>
        {
            var l = Calculators.Linear(200000m, 4m, 360);
            var a = Calculators.Annuity(200000m, 4m, 360);
            Near(1222.22m, l.FirstPayment, 0.01m);
            True(l.LastPayment < 560m, $"last {l.LastPayment}");
            True(l.TotalInterest < a.TotalInterest, "cheaper overall");
            Near(120333m, l.TotalInterest, 5m);
        });

        Test("zero-interest loan", () => Eq(100m, Calculators.Annuity(1200m, 0m, 12).FirstPayment));

        Test("debt payoff: avalanche pays less interest than snowball", () =>
        {
            var debts = new[]
            {
                new DebtInput("Credit card", 2000m, 20m, 60m),
                new DebtInput("Phone", 500m, 0m, 25m),
                new DebtInput("Loan", 5000m, 7m, 120m),
            };
            var av = Calculators.DebtPayoff(debts, 200m, PayoffStrategy.Avalanche);
            var sb = Calculators.DebtPayoff(debts, 200m, PayoffStrategy.Snowball);
            True(av.PaysOff && sb.PaysOff, "both pay off");
            True(av.TotalInterest <= sb.TotalInterest, $"avalanche {av.TotalInterest} vs snowball {sb.TotalInterest}");
            Eq("Phone", sb.PayoffOrder[0].Name, "snowball clears the smallest first");
            Eq("Credit card", av.PayoffOrder[0].Name, "avalanche clears the most expensive first");
        });

        Test("debt payoff: payments below the interest never pay off", () =>
            True(!Calculators.DebtPayoff(new[] { new DebtInput("X", 10000m, 24m, 100m) }, 0m, PayoffStrategy.Avalanche).PaysOff, "never"));

        Test("inflation both ways", () =>
        {
            Near(121.90m, Calculators.FutureCost(100m, 2m, 10), 0.01m);
            Near(82.03m, Calculators.PurchasingPower(100m, 2m, 10), 0.01m);
        });

        Test("financial independence target uses the withdrawal rate", () =>
        {
            var fi = Calculators.FinancialIndependence(24000m, 0m, 1000m, 4m, 4m);
            Eq(600000m, fi.TargetAmount);
            True(fi.Months is > 300 and < 420, $"months {fi.Months}");
        });

        Test("50/30/20 and months covered", () =>
        {
            var (n, w, s) = Calculators.Split503020(2000m);
            Eq(1000m, n); Eq(600m, w); Eq(400m, s);
            Eq(2.5m, Calculators.MonthsCovered(2500m, 1000m));
            Eq<decimal?>(null, Calculators.MonthsCovered(2500m, 0m));
        });

        Test("car cost per month", () =>
        {
            var c = Calculators.CarCost(12000m, 6000m, 5, 12000m, 2m, 20m, 50m, 30m, 600m);
            Eq(100m, c.Depreciation);
            Eq(100m, c.Fuel);
            Eq(130m, c.Fixed);
            Eq(330m, c.MonthlyTotal);
        });

        // ---------------- Profile & advice ----------------
        var txs = new List<Transaction>();
        for (int m = 4; m <= 9; m++)
        {
            txs.Add(Tx($"2026-{m:00}-25", TransactionType.Income, "Salary", 2000m));
            txs.Add(Tx($"2026-{m:00}-05", TransactionType.Expense, "Groceries", 300m));
            txs.Add(Tx($"2026-{m:00}-12", TransactionType.Expense, "Eating out", m >= 7 ? 400m : 100m));
        }
        var recs = new List<RecurringItem>
        {
            Rec("Rent", "Housing", 800m, RecurringFrequency.Monthly, "2026-04-01"),
            Rec("Netflix", "Subscriptions", 14m, RecurringFrequency.Monthly, "2026-04-01"),
        };
        var balances = new List<BalanceItem>
        {
            new() { Name = "Current", Kind = BalanceKind.Checking, Amount = 800m },
            new() { Name = "Savings", Kind = BalanceKind.Savings, Amount = 1200m },
            new() { Name = "Credit card", Kind = BalanceKind.CreditCard, Amount = 900m, InterestRate = 14m, MonthlyPayment = 50m },
            new() { Name = "DUO", Kind = BalanceKind.StudentLoan, Amount = 15000m, InterestRate = 2.33m, MonthlyPayment = 45m },
        };
        var goals = new List<Goal> { new() { Name = "Laptop", TargetAmount = 1200m, SavedAmount = 200m, TargetDate = new DateTime(2027, 4, 1) } };
        var snap = FinancialSnapshot.Build(txs, recs, new[] { new Budget { Category = "Eating out", MonthlyLimit = 150m } },
            goals, balances, new ProfileInfo { BirthYear = 2003, ReportMonths = 6 }, Today);

        Test("snapshot: averages over complete months only", () =>
        {
            Eq(6, snap.Months.Count);
            Eq(2000m, snap.AvgIncome);
            Eq(300m + 250m + 814m, snap.AvgExpenses, "groceries + avg eating out + rent + netflix");
            Eq(23, snap.Age);
        });

        Test("snapshot: buffer, debts, net worth, trend", () =>
        {
            Eq(2000m, snap.LiquidSavings);
            Eq(-13900m, snap.NetWorth);
            Eq(95m, snap.MonthlyDebtPayments);
            True(snap.EmergencyMonths is > 1.7m and < 1.9m, $"essentials {snap.EssentialsPerMonth}, months {snap.EmergencyMonths}");
            True(snap.SpendingTrend is > 0.15m, $"trend {snap.SpendingTrend}");
            Eq("Eating out", snap.CategoriesOverBudget.Single());
        });

        Test("advice: flags the right things, most urgent first", () =>
        {
            var advice = AdviceEngine.Evaluate(snap);
            True(advice.Any(a => a.Title == "Expensive debt" && a.Level == AdviceLevel.Action), "credit card at 14%");
            True(advice.Any(a => a.Area == "Buffer" && a.Level == AdviceLevel.Attention), "buffer under 3 months");
            True(advice.Any(a => a.Area == "Budgets"), "eating out budget");
            True(advice.Any(a => a.Title.StartsWith("Spending is up")), "trend");
            True(advice.Any(a => a.Title == "Student debt"), "DUO info");
            True(advice.Any(a => a.Area == "Goals"), "goal check");
            True(advice.First().Level == AdviceLevel.Action, "sorted by urgency");
            True(advice.All(a => a.Finding.Length > 0 && a.Suggestion.Length > 0), "every item explains itself");
        });

        Test("advice: no data gives a helpful start message, not a crash", () =>
        {
            var empty = FinancialSnapshot.Build(Array.Empty<Transaction>(), Array.Empty<RecurringItem>(), Array.Empty<Budget>(),
                Array.Empty<Goal>(), Array.Empty<BalanceItem>(), new ProfileInfo(), Today);
            var advice = AdviceEngine.Evaluate(empty);
            Eq("Not enough data yet", advice[0].Title);
        });
    }
}
