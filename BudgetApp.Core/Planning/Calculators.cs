using System;
using System.Collections.Generic;
using System.Linq;

namespace BudgetApp.Planning
{
    public sealed record YearRow(int Year, decimal Deposited, decimal Interest, decimal Balance);

    public sealed record CompoundResult(decimal FinalValue, decimal TotalDeposited, decimal InterestEarned, IReadOnlyList<YearRow> Years);

    public sealed record LoanResult(decimal FirstPayment, decimal LastPayment, decimal TotalPaid, decimal TotalInterest, IReadOnlyList<YearRow> Years);

    public sealed record DebtInput(string Name, decimal Balance, decimal AnnualRatePercent, decimal MinimumPayment);

    public sealed record DebtPayoffResult(bool PaysOff, int Months, decimal TotalInterest, decimal TotalPaid, IReadOnlyList<(string Name, int Month)> PayoffOrder);

    public enum PayoffStrategy { Avalanche, Snowball }

    public sealed record FiResult(decimal TargetAmount, int? Months, IReadOnlyList<YearRow> Years);

    public sealed record CarCostResult(decimal MonthlyTotal, decimal PerKm, decimal Depreciation, decimal Fuel, decimal Fixed);

    /// <summary>
    /// Financial calculators. All rates are yearly percentages (5 = 5%), amounts in euros.
    /// Interest is compounded monthly unless stated otherwise.
    /// </summary>
    public static class Calculators
    {
        private static double MonthlyRate(decimal annualPercent) => (double)annualPercent / 100.0 / 12.0;
        private static decimal R2(double v) => Math.Round((decimal)v, 2, MidpointRounding.AwayFromZero);
        private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        // ------------------------------------------------------------------ savings / compound interest

        /// <summary>Grows a starting amount plus a monthly deposit (made at the end of each month).</summary>
        public static CompoundResult CompoundInterest(decimal start, decimal monthlyDeposit, decimal annualRatePercent, int years)
        {
            years = Math.Clamp(years, 0, 100);
            double r = MonthlyRate(annualRatePercent);
            double balance = (double)start;
            double deposited = (double)start;
            var rows = new List<YearRow>();
            double yearStartBalance = balance, yearDeposits = 0;
            for (int month = 1; month <= years * 12; month++)
            {
                balance = balance * (1 + r) + (double)monthlyDeposit;
                deposited += (double)monthlyDeposit;
                yearDeposits += (double)monthlyDeposit;
                if (month % 12 == 0)
                {
                    rows.Add(new YearRow(month / 12, R2(deposited), R2(balance - yearStartBalance - yearDeposits), R2(balance)));
                    yearStartBalance = balance;
                    yearDeposits = 0;
                }
            }
            return new CompoundResult(R2(balance), R2(deposited), R2(balance - deposited), rows);
        }

        /// <summary>Months until <paramref name="target"/> is reached, or null if it never is (within 100 years).</summary>
        public static int? MonthsToReach(decimal target, decimal current, decimal monthlyDeposit, decimal annualRatePercent)
        {
            if (current >= target) return 0;
            double r = MonthlyRate(annualRatePercent);
            double balance = (double)current;
            for (int month = 1; month <= 1200; month++)
            {
                balance = balance * (1 + r) + (double)monthlyDeposit;
                if (balance >= (double)target - 0.005) return month;
            }
            return null;
        }

        /// <summary>Monthly deposit needed to reach <paramref name="target"/> in <paramref name="months"/> months.</summary>
        public static decimal RequiredMonthly(decimal target, decimal current, int months, decimal annualRatePercent)
        {
            if (months <= 0) return Math.Max(0, target - current);
            double r = MonthlyRate(annualRatePercent);
            double growth = Math.Pow(1 + r, months);
            double remaining = (double)target - (double)current * growth;
            if (remaining <= 0) return 0;
            double payment = r == 0 ? remaining / months : remaining * r / (growth - 1);
            // Round up to whole cents so the goal is really reached in time.
            return Math.Ceiling((decimal)payment * 100m) / 100m;
        }

        // ------------------------------------------------------------------ loans

        /// <summary>
        /// Annuity loan (annuïteit): the same payment every month; early on mostly interest.
        /// </summary>
        public static LoanResult Annuity(decimal principal, decimal annualRatePercent, int months)
        {
            if (principal <= 0 || months <= 0) return new LoanResult(0, 0, 0, 0, Array.Empty<YearRow>());
            double r = MonthlyRate(annualRatePercent);
            double payment = r == 0 ? (double)principal / months : (double)principal * r / (1 - Math.Pow(1 + r, -months));
            return Simulate(principal, r, months, _ => payment);
        }

        /// <summary>
        /// Linear loan (lineair): the same repayment every month plus interest on what's left, so the
        /// payment starts high and drops. Costs less interest in total than an annuity.
        /// </summary>
        public static LoanResult Linear(decimal principal, decimal annualRatePercent, int months)
        {
            if (principal <= 0 || months <= 0) return new LoanResult(0, 0, 0, 0, Array.Empty<YearRow>());
            double r = MonthlyRate(annualRatePercent);
            double repay = (double)principal / months;
            return Simulate(principal, r, months, remaining => repay + remaining * r);
        }

        private static LoanResult Simulate(decimal principal, double r, int months, Func<double, double> paymentFor)
        {
            double remaining = (double)principal, totalPaid = 0, totalInterest = 0, yearPaid = 0, yearInterest = 0;
            double first = 0, last = 0;
            var rows = new List<YearRow>();
            for (int m = 1; m <= months; m++)
            {
                double interest = remaining * r;
                double payment = Math.Min(paymentFor(remaining), remaining + interest);
                if (m == months) payment = remaining + interest;   // clear rounding leftovers
                remaining = remaining + interest - payment;
                totalPaid += payment; totalInterest += interest; yearPaid += payment; yearInterest += interest;
                if (m == 1) first = payment;
                last = payment;
                if (m % 12 == 0 || m == months)
                {
                    rows.Add(new YearRow((m + 11) / 12, R2(yearPaid), R2(yearInterest), R2(Math.Max(0, remaining))));
                    yearPaid = 0; yearInterest = 0;
                }
            }
            return new LoanResult(R2(first), R2(last), R2(totalPaid), R2(totalInterest), rows);
        }

        // ------------------------------------------------------------------ debt payoff

        /// <summary>
        /// Pays several debts with a fixed monthly budget (all minimum payments + extra).
        /// Avalanche: extra goes to the highest interest rate first (cheapest overall).
        /// Snowball: extra goes to the smallest balance first (quick wins).
        /// When a debt is paid off, its minimum payment rolls over to the next one.
        /// </summary>
        public static DebtPayoffResult DebtPayoff(IEnumerable<DebtInput> debts, decimal extraPerMonth, PayoffStrategy strategy)
        {
            var list = debts.Where(d => d.Balance > 0).Select(d => new MutableDebt(d)).ToList();
            if (list.Count == 0) return new DebtPayoffResult(true, 0, 0, 0, Array.Empty<(string, int)>());

            double budget = list.Sum(d => (double)d.Input.MinimumPayment) + (double)Math.Max(0, extraPerMonth);
            double totalInterest = 0, totalPaid = 0;
            var order = new List<(string, int)>();

            for (int month = 1; month <= 1200; month++)
            {
                foreach (var d in list.Where(d => d.Balance > 0))
                {
                    double interest = d.Balance * MonthlyRate(d.Input.AnnualRatePercent);
                    d.Balance += interest;
                    totalInterest += interest;
                }

                double money = budget;
                foreach (var d in list.Where(d => d.Balance > 0))
                {
                    double pay = Math.Min(d.Balance, (double)d.Input.MinimumPayment);
                    d.Balance -= pay; money -= pay; totalPaid += pay;
                }

                var targets = (strategy == PayoffStrategy.Avalanche
                        ? list.Where(d => d.Balance > 0).OrderByDescending(d => d.Input.AnnualRatePercent).ThenBy(d => d.Balance)
                        : list.Where(d => d.Balance > 0).OrderBy(d => d.Balance).ThenByDescending(d => d.Input.AnnualRatePercent))
                    .ToList();
                foreach (var d in targets)
                {
                    if (money <= 0) break;
                    double pay = Math.Min(d.Balance, money);
                    d.Balance -= pay; money -= pay; totalPaid += pay;
                }

                foreach (var d in list.Where(d => d.Balance <= 0.005 && d.PaidMonth == null))
                {
                    d.Balance = 0;
                    d.PaidMonth = month;
                    order.Add((d.Input.Name, month));
                }

                if (list.All(d => d.PaidMonth != null))
                    return new DebtPayoffResult(true, month, R2(totalInterest), R2(totalPaid), order);
            }
            return new DebtPayoffResult(false, 1200, R2(totalInterest), R2(totalPaid), order);
        }

        private sealed class MutableDebt
        {
            public MutableDebt(DebtInput input) { Input = input; Balance = (double)input.Balance; }
            public DebtInput Input { get; }
            public double Balance { get; set; }
            public int? PaidMonth { get; set; }
        }

        // ------------------------------------------------------------------ other calculators

        /// <summary>What something costing <paramref name="amount"/> today costs after <paramref name="years"/> of inflation.</summary>
        public static decimal FutureCost(decimal amount, decimal inflationPercent, int years) =>
            R2((double)amount * Math.Pow(1 + (double)inflationPercent / 100, years));

        /// <summary>What <paramref name="amount"/> will still buy, in today's money, after <paramref name="years"/>.</summary>
        public static decimal PurchasingPower(decimal amount, decimal inflationPercent, int years) =>
            R2((double)amount / Math.Pow(1 + (double)inflationPercent / 100, years));

        /// <summary>
        /// Financial independence: the amount at which a safe withdrawal rate covers yearly spending,
        /// and how long it takes to get there by investing every month (return after inflation).
        /// </summary>
        public static FiResult FinancialIndependence(decimal yearlySpending, decimal invested, decimal monthlySaving, decimal realReturnPercent, decimal withdrawalRatePercent)
        {
            if (withdrawalRatePercent <= 0) return new FiResult(0, null, Array.Empty<YearRow>());
            decimal target = R2(yearlySpending / (withdrawalRatePercent / 100m));
            var months = MonthsToReach(target, invested, monthlySaving, realReturnPercent);
            int years = Math.Min(60, ((months ?? 720) + 11) / 12);
            var growth = CompoundInterest(invested, monthlySaving, realReturnPercent, years);
            return new FiResult(target, months, growth.Years);
        }

        /// <summary>The 50/30/20 guideline: needs, wants and savings as shares of net income.</summary>
        public static (decimal Needs, decimal Wants, decimal Savings) Split503020(decimal netIncome) =>
            (R2(netIncome * 0.50m), R2(netIncome * 0.30m), R2(netIncome * 0.20m));

        /// <summary>How many months of essential costs your savings cover.</summary>
        public static decimal? MonthsCovered(decimal liquidSavings, decimal monthlyEssentials) =>
            monthlyEssentials > 0 ? Math.Round(liquidSavings / monthlyEssentials, 1) : null;

        /// <summary>Real monthly cost of owning a car, including value loss.</summary>
        public static CarCostResult CarCost(decimal price, decimal resaleValue, int years, decimal kmPerYear,
            decimal fuelPricePerLitre, decimal kmPerLitre, decimal insurancePerMonth, decimal roadTaxPerMonth, decimal maintenancePerYear)
        {
            if (years <= 0) years = 1;
            decimal months = years * 12m;
            decimal depreciation = Math.Max(0, price - resaleValue) / months;
            decimal fuel = kmPerLitre > 0 ? kmPerYear / kmPerLitre * fuelPricePerLitre / 12m : 0;
            decimal fixedCosts = insurancePerMonth + roadTaxPerMonth + maintenancePerYear / 12m;
            decimal total = depreciation + fuel + fixedCosts;
            decimal perKm = kmPerYear > 0 ? total * 12m / kmPerYear : 0;
            return new CarCostResult(R2(total), Math.Round(perKm, 3), R2(depreciation), R2(fuel), R2(fixedCosts));
        }
    }
}
