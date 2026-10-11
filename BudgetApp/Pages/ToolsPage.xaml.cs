using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BudgetApp.Models;
using BudgetApp.Planning;
using BudgetApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;

namespace BudgetApp.Pages
{
    /// <summary>
    /// Financial calculators. Each one builds its own small form; results update while you type.
    /// The maths lives in BudgetApp.Core (Calculators), where it is unit tested.
    /// </summary>
    public partial class ToolsPage : UserControl, IPage
    {
        private sealed record CalcDef(string Id, string Title, string Blurb, string Summary, Action Build);

        private readonly List<CalcDef> _calcs;
        private readonly Dictionary<string, RadioButton> _buttons = new();
        private string? _currentId;
        private Action? _compute;
        private FinancialSnapshot _snap = null!;
        private WrapPanel _tiles = new();

        public ToolsPage()
        {
            InitializeComponent();
            _calcs = new List<CalcDef>
            {
                new("savings-goal", "Savings goal", "How long, or how much per month?",
                    "Work out how long it takes to save for something, or how much you need to put aside each month to make a date.", BuildSavingsGoal),
                new("compound", "Compound interest", "What saving regularly grows into",
                    "See how a starting amount plus a monthly deposit grows over the years, and how much of it is interest.", BuildCompound),
                new("loan", "Loan & mortgage", "Monthly payment, annuity vs. linear",
                    "Monthly payment and total interest for a loan or mortgage. Compares an annuity (same payment every month) with a linear loan (payment drops over time).", BuildLoan),
                new("debt", "Debt payoff planner", "Fastest way out of several debts",
                    "Plan paying off several debts with a fixed monthly amount. Avalanche pays the highest interest first (cheapest); snowball pays the smallest debt first (quick wins).", BuildDebt),
                new("emergency", "Emergency fund", "How many months your savings last",
                    "How many months of essential costs your savings would cover, and what a 3-6 month buffer would be.", BuildEmergency),
                new("student", "Student loan (DUO)", "Monthly repayment and interest",
                    "Estimate the monthly repayment of student debt and the effect of paying extra. DUO calculates your real minimum from your income; check Mijn DUO for that.", BuildStudentLoan),
                new("split", "50/30/20 budget", "A simple way to split your income",
                    "A popular guideline: 50% of net income for needs, 30% for wants, 20% for saving or paying off debt. Compared with your actual spending.", BuildSplit),
                new("inflation", "Inflation", "What money is worth later",
                    "What something will cost in the future, and what your money will still buy, at a steady inflation rate.", BuildInflation),
                new("fi", "Financial independence", "When could investments pay your costs?",
                    "The amount at which investment returns could cover your yearly spending (based on a 'safe withdrawal rate'), and how long saving towards it takes.", BuildFi),
                new("car", "Car costs", "The real monthly cost of a car",
                    "All-in monthly cost of owning a car: value loss, fuel, insurance, road tax and maintenance.", BuildCar),
            };

            foreach (var c in _calcs)
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = c.Title, FontSize = 14 });
                content.Children.Add(new TextBlock { Text = c.Blurb, FontSize = 11, Foreground = Look.Faint, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Normal });
                var button = new RadioButton { Content = content, GroupName = "Calc", Style = (Style)FindResource("ListChoice"), Tag = c.Id };
                button.Checked += (_, _) => Select(c.Id);
                _buttons[c.Id] = button;
                CalculatorList.Children.Add(button);
            }
        }

        public void OnShown(object? argument)
        {
            var store = AppState.Store;
            var profile = store.GetProfile();
            profile.ReportMonths = 6;
            _snap = FinancialSnapshot.Build(store.Transactions, store.Recurring, store.Budgets, store.Goals, store.Balances, profile, DateTime.Today);

            var id = argument as string ?? _currentId ?? _calcs[0].Id;
            if (_buttons.TryGetValue(id, out var b))
            {
                if (b.IsChecked == true && _currentId != id) Select(id);
                b.IsChecked = true;
            }
        }

        internal IEnumerable<string> CalculatorIds => _calcs.Select(c => c.Id);
        internal void ShowCalculator(string id) { _buttons[id].IsChecked = true; Select(id); }

        private void Select(string id)
        {
            var calc = _calcs.First(c => c.Id == id);
            _currentId = id;
            CalcTitle.Text = calc.Title;
            CalcSummary.Text = calc.Summary;
            InputPanel.Children.Clear();
            ResultPanel.Children.Clear();
            _compute = null;
            calc.Build();
            Recalculate();
            CalcScroll.ScrollToTop();
        }

        // ================================================================== form helpers

        private TextBox Field(string label, decimal? value, string? tip = null, double width = 190, bool whole = false)
        {
            var box = new TextBox
            {
                Text = value is decimal v ? (whole ? Math.Round(v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", Money.Culture)) : "",
                ToolTip = tip
            };
            box.TextChanged += (_, _) => Recalculate();
            AddInput(label, box, width);
            return box;
        }

        private ComboBox Pick(string label, string[] options, int selected = 0, double width = 190)
        {
            var box = new ComboBox { ItemsSource = options, SelectedIndex = selected };
            box.SelectionChanged += (_, _) => Recalculate();
            AddInput(label, box, width);
            return box;
        }

        private void AddInput(string label, FrameworkElement input, double width)
        {
            var panel = new StackPanel { Width = width, Margin = new Thickness(0, 0, 16, 0) };
            panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InputLabel"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = label });
            panel.Children.Add(input);
            InputPanel.Children.Add(panel);
        }

        private static decimal? Num(TextBox box)
        {
            var t = (box.Text ?? "").Replace("%", "").Trim();
            if (t.Length == 0) return null;
            return Money.TryParseSigned(t, out var v) ? v : null;
        }

        private static decimal NumOr(TextBox box, decimal fallback) => Num(box) ?? fallback;

        private void Recalculate()
        {
            if (_compute == null) return;
            ResultPanel.Children.Clear();
            _tiles = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            ResultPanel.Children.Add(_tiles);
            try { _compute(); }
            catch (Exception ex)
            {
                ResultPanel.Children.Clear();
                Note("Check the numbers you filled in. (" + ex.GetBaseException().Message + ")");
            }
        }

        private void Tile(string label, string value, string? sub = null, Brush? brush = null)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Style = (Style)FindResource("StatLabel") });
            panel.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = brush ?? Look.Text });
            if (sub != null) panel.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("StatSub") });
            _tiles.Children.Add(new Border
            {
                Background = Look.Res("PageBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 12, 12), MinWidth = 190, MaxWidth = 300, Child = panel
            });
        }

        private void Note(string text, bool warn = false)
        {
            ResultPanel.Children.Add(new Border
            {
                Background = Look.Res(warn ? "WarnSoftBrush" : "AccentSoftBrush"), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = text, Style = (Style)FindResource("BodyText") }
            });
        }

        private void Heading(string text) =>
            ResultPanel.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("SectionHeader"), FontSize = 14, Margin = new Thickness(0, 8, 0, 8) });

        private void Table(IEnumerable rows, params (string Header, string Path, bool Money)[] columns)
        {
            var grid = new DataGrid { ItemsSource = rows, MaxHeight = 320, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var (header, path, money) in columns)
            {
                var binding = new Binding(path);
                if (money) binding.Converter = (IValueConverter)FindResource("Euro");
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = header, Binding = binding, Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    ElementStyle = money ? (Style)FindResource("RightAligned") : null
                });
            }
            ResultPanel.Children.Add(grid);
        }

        private void GrowthChart(IReadOnlyList<YearRow> years, string balanceName, string depositName, decimal start = 0)
        {
            if (years.Count == 0) return;
            var chart = new CartesianChart { Height = 240, LegendPosition = LiveChartsCore.Measure.LegendPosition.Top, Margin = new Thickness(0, 0, 0, 12) };
            Look.Prepare(chart);
            chart.Series = new ISeries[]
            {
                new LineSeries<decimal>
                {
                    Name = balanceName, Values = years.Select(y => y.Balance).ToArray(), GeometrySize = 0,
                    Stroke = new SolidColorPaint(Look.NetColor) { StrokeThickness = 3 }, Fill = new SolidColorPaint(Look.NetColor.WithAlpha(35)),
                    YToolTipLabelFormatter = p => Money.Format((decimal)p.Coordinate.PrimaryValue)
                },
                new LineSeries<decimal>
                {
                    Name = depositName, Values = years.Select(y => y.Deposited).ToArray(), GeometrySize = 0,
                    Stroke = new SolidColorPaint(Look.AxisText) { StrokeThickness = 2 }, Fill = null,
                    YToolTipLabelFormatter = p => Money.Format((decimal)p.Coordinate.PrimaryValue)
                }
            };
            chart.XAxes = new[] { new Axis { Labels = years.Select(y => $"Year {y.Year}").ToArray(), LabelsPaint = new SolidColorPaint(Look.AxisText), TextSize = 11 } };
            chart.YAxes = new[] { new Axis { Labeler = v => Money.Format((decimal)Math.Round(v)).Replace(",00", ""), LabelsPaint = new SolidColorPaint(Look.AxisText),
                                             SeparatorsPaint = new SolidColorPaint(Look.GridLine), TextSize = 11 } };
            ResultPanel.Children.Add(chart);
        }

        private static string MonthsText(int months)
        {
            if (months <= 0) return "now";
            int y = months / 12, m = months % 12;
            var parts = new List<string>();
            if (y > 0) parts.Add($"{y} year{(y == 1 ? "" : "s")}");
            if (m > 0) parts.Add($"{m} month{(m == 1 ? "" : "s")}");
            return string.Join(" and ", parts);
        }

        private static string When(int months) => DateTime.Today.AddMonths(months).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        private decimal AvgLeftOver => Math.Max(0, Math.Round(_snap.AvgNet, 0));

        // ================================================================== calculators

        private void BuildSavingsGoal()
        {
            var goal = AppState.Store.Goals.FirstOrDefault(g => g.SavedAmount < g.TargetAmount);
            var target = Field("Target amount (€)", goal?.TargetAmount ?? 1500m);
            var saved = Field("Already saved (€)", goal?.SavedAmount ?? 0m);
            var monthly = Field("You can save per month (€)", AvgLeftOver > 0 ? AvgLeftOver : 100m, "Pre-filled with what you have left over on average");
            var rate = Field("Interest per year (%)", 1.5m, "Savings account rate", 150);
            var months = Field("Or: reach it in (months)", goal?.TargetDate is DateTime d ? Math.Max(1, (d.Year - DateTime.Today.Year) * 12 + d.Month - DateTime.Today.Month) : 12m, whole: true, width: 170);

            _compute = () =>
            {
                decimal t = NumOr(target, 0), s = NumOr(saved, 0), m = NumOr(monthly, 0), r = NumOr(rate, 0);
                int n = (int)Math.Max(1, NumOr(months, 12));
                var need = Calculators.MonthsToReach(t, s, m, r);
                Tile("Time needed", need is int k ? MonthsText(k) : "Never", need is int k2 ? $"Around {When(k2)}, at {Money.Format(m)}/month" : "Increase the monthly amount", need == null ? Look.Bad : Look.Accent);
                var req = Calculators.RequiredMonthly(t, s, n, r);
                Tile($"Needed for {n} months", Money.Format(req) + " /month", $"To have {Money.Format(t)} by {When(n)}");
                Tile("Still to save", Money.Format(Math.Max(0, t - s)));
                if (goal != null) Note($"Filled in from your goal \"{goal.Name}\". Change the numbers to try other plans.");
                if (_snap.HasData && req > AvgLeftOver)
                    Note($"You have about {Money.Format(AvgLeftOver)} left over a month on average, less than the {Money.Format(req)} needed. Move the date or free up room in your budget.", warn: true);
            };
        }

        private void BuildCompound()
        {
            var start = Field("Starting amount (€)", _snap.LiquidSavings > 0 ? Math.Round(_snap.LiquidSavings) : 1000m);
            var monthly = Field("Monthly deposit (€)", AvgLeftOver > 0 ? Math.Min(AvgLeftOver, 250m) : 100m);
            var rate = Field("Return per year (%)", 5m, "Savings accounts pay a few %; stock-market funds have historically returned more on average, with ups and downs.", 150);
            var years = Field("Years", 20m, whole: true, width: 120);

            _compute = () =>
            {
                int y = (int)Math.Clamp(NumOr(years, 10), 1, 60);
                var res = Calculators.CompoundInterest(NumOr(start, 0), NumOr(monthly, 0), NumOr(rate, 0), y);
                Tile($"After {y} years", Money.Format(res.FinalValue), null, Look.Accent);
                Tile("You deposited", Money.Format(res.TotalDeposited));
                Tile("Growth (interest)", Money.Format(res.InterestEarned), res.FinalValue > 0 ? $"{Look.Pct(res.InterestEarned / res.FinalValue)} of the total" : null, Look.Good);
                GrowthChart(res.Years, "Total", "Deposited");
                Note("Returns are never guaranteed. This uses one steady rate and no taxes (in the Netherlands, savings and investments above a threshold count for box 3).");
                Table(res.Years, ("Year", "Year", false), ("Deposited", "Deposited", true), ("Growth that year", "Interest", true), ("Total", "Balance", true));
            };
        }

        private void BuildLoan()
        {
            var amount = Field("Loan amount (€)", 250000m);
            var rate = Field("Interest per year (%)", 3.9m, null, 150);
            var years = Field("Term (years)", 30m, whole: true, width: 120);

            _compute = () =>
            {
                decimal p = NumOr(amount, 0), r = NumOr(rate, 0);
                int months = (int)Math.Clamp(NumOr(years, 30), 1, 50) * 12;
                var a = Calculators.Annuity(p, r, months);
                var l = Calculators.Linear(p, r, months);
                Heading("Annuity (annuïteit) - same payment every month");
                NewTiles();
                Tile("Monthly payment", Money.Format(a.FirstPayment), "Gross, before any tax deduction", Look.Accent);
                Tile("Total interest", Money.Format(a.TotalInterest));
                Tile("Total paid", Money.Format(a.TotalPaid));
                Heading("Linear (lineair) - payment drops over time");
                NewTiles();
                Tile("First payment", Money.Format(l.FirstPayment), null, Look.Accent);
                Tile("Last payment", Money.Format(l.LastPayment));
                Tile("Total interest", Money.Format(l.TotalInterest), $"{Money.Format(a.TotalInterest - l.TotalInterest)} less than annuity", Look.Good);
                Note("For a mortgage, how much you may borrow depends on your income and rules that change every year. Use this to compare monthly costs; a mortgage adviser or the lender's calculator gives your real maximum.");
                Heading("Remaining debt per year (annuity)");
                Table(a.Years, ("Year", "Year", false), ("Paid that year", "Deposited", true), ("of which interest", "Interest", true), ("Remaining debt", "Balance", true));
            };
        }

        /// <summary>Starts a new row of result tiles below what's already shown.</summary>
        private void NewTiles()
        {
            _tiles = new WrapPanel { Orientation = Orientation.Horizontal };
            ResultPanel.Children.Add(_tiles);
        }

        // ------------------------------------------------------------------ debt payoff

        public sealed class DebtRow : INotifyPropertyChanged
        {
            private string _name = "", _balance = "", _rate = "", _minimum = "";
            public string Name { get => _name; set { _name = value; Changed(); } }
            public string Balance { get => _balance; set { _balance = value; Changed(); } }
            public string Rate { get => _rate; set { _rate = value; Changed(); } }
            public string Minimum { get => _minimum; set { _minimum = value; Changed(); } }
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private ObservableCollection<DebtRow>? _debts;

        private void BuildDebt()
        {
            if (_debts == null)
            {
                var fromProfile = _snap.Debts.Where(d => d.Kind != BalanceKind.Mortgage).Select(d => new DebtRow
                {
                    Name = d.Name,
                    Balance = d.Amount.ToString("0.##", Money.Culture),
                    Rate = (d.InterestRate ?? 0).ToString("0.##", Money.Culture),
                    Minimum = (d.MonthlyPayment ?? Math.Max(25, Math.Round(d.Amount * 0.03m))).ToString("0.##", Money.Culture)
                }).ToList();
                if (fromProfile.Count == 0)
                    fromProfile = new List<DebtRow>
                    {
                        new() { Name = "Credit card", Balance = "1500", Rate = "14", Minimum = "50" },
                        new() { Name = "Phone instalments", Balance = "400", Rate = "0", Minimum = "25" },
                    };
                _debts = new ObservableCollection<DebtRow>(fromProfile);
            }

            var extra = Field("Extra per month on top of the minimums (€)", 100m, null, 280);
            var strategy = Pick("Strategy", new[] { "Avalanche - highest interest first", "Snowball - smallest debt first" }, 0, 280);

            var grid = new DataGrid
            {
                ItemsSource = _debts, IsReadOnly = false, CanUserAddRows = true, CanUserDeleteRows = true, SelectionMode = DataGridSelectionMode.Single,
                Margin = new Thickness(0, 4, 0, 12), MaxHeight = 220, Width = 640, HorizontalAlignment = HorizontalAlignment.Left
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Debt", Binding = new Binding(nameof(DebtRow.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Balance (€)", Binding = new Binding(nameof(DebtRow.Balance)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Interest %", Binding = new Binding(nameof(DebtRow.Rate)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Minimum / month (€)", Binding = new Binding(nameof(DebtRow.Minimum)), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
            grid.RowEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(Recalculate));
            grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(Recalculate));
            var holder = new StackPanel { Width = 680 };
            holder.Children.Add(new TextBlock { Text = "Your debts (click a cell to change it, type in the empty row to add one, select a row and press Delete to remove it)", Style = (Style)FindResource("InputLabel"), TextWrapping = TextWrapping.Wrap });
            holder.Children.Add(grid);
            InputPanel.Children.Insert(0, holder);
            if (_snap.Debts.Count > 0)
                holder.Children.Add(new TextBlock { Text = "Filled in from the debts on your Profile page (mortgage left out).", Style = (Style)FindResource("HintText"), Margin = new Thickness(0, -6, 0, 10) });

            _compute = () =>
            {
                var debts = _debts!.Where(d => !string.IsNullOrWhiteSpace(d.Name) || !string.IsNullOrWhiteSpace(d.Balance))
                    .Select(d => new DebtInput(
                        string.IsNullOrWhiteSpace(d.Name) ? "Debt" : d.Name,
                        Money.TryParse(d.Balance, out var b) ? b : 0,
                        Look.ReadPercent(d.Rate) ?? 0,
                        Money.TryParse(d.Minimum, out var m) ? m : 0))
                    .Where(d => d.Balance > 0).ToList();
                if (debts.Count == 0) { Note("Add at least one debt with a balance."); return; }

                var chosen = strategy.SelectedIndex == 1 ? PayoffStrategy.Snowball : PayoffStrategy.Avalanche;
                var plan = Calculators.DebtPayoff(debts, NumOr(extra, 0), chosen);
                var minimumOnly = Calculators.DebtPayoff(debts, 0, chosen);
                var other = Calculators.DebtPayoff(debts, NumOr(extra, 0), chosen == PayoffStrategy.Avalanche ? PayoffStrategy.Snowball : PayoffStrategy.Avalanche);

                if (!plan.PaysOff)
                {
                    Tile("Debt-free", "Never", "The payments don't cover the interest", Look.Bad);
                    Note("Increase the minimum payments or the extra amount: at this pace the interest grows faster than you pay off.", warn: true);
                    return;
                }
                Tile("Debt-free in", MonthsText(plan.Months), $"Around {When(plan.Months)}", Look.Good);
                Tile("Interest you'll pay", Money.Format(plan.TotalInterest));
                Tile("Monthly budget", Money.Format(debts.Sum(d => d.MinimumPayment) + NumOr(extra, 0)), "Minimums plus extra; stays the same as debts are paid off");
                if (minimumOnly.PaysOff && minimumOnly.Months > plan.Months)
                    Note($"Paying only the minimums would take {MonthsText(minimumOnly.Months)} and cost {Money.Format(minimumOnly.TotalInterest)} in interest. The extra saves {Money.Format(minimumOnly.TotalInterest - plan.TotalInterest)}.");
                if (other.PaysOff)
                {
                    var diff = other.TotalInterest - plan.TotalInterest;
                    Note(diff > 0.5m ? $"This strategy saves {Money.Format(diff)} interest compared with the other one."
                       : diff < -0.5m ? $"The other strategy would save {Money.Format(-diff)} interest - but this one may keep you motivated with quicker wins."
                       : "Both strategies cost about the same here.");
                }
                Heading("Order in which debts are cleared");
                Table(plan.PayoffOrder.Select((p, i) => new { Step = i + 1, p.Name, When = $"{MonthsText(p.Month)} ({When(p.Month)})" }).ToList(),
                    ("#", "Step", false), ("Debt", "Name", false), ("Paid off after", "When", false));
            };
        }

        // ------------------------------------------------------------------ others

        private void BuildEmergency()
        {
            var essentials = Field("Essential costs per month (€)", _snap.EssentialsPerMonth > 0 ? Math.Round(_snap.EssentialsPerMonth) : 1000m,
                "Rent, groceries, insurance, energy, transport - what you can't skip. Pre-filled from your 'needs' categories.", 230);
            var savings = Field("Savings you can access quickly (€)", _snap.LiquidSavings > 0 ? Math.Round(_snap.LiquidSavings) : 0m,
                "Current + savings accounts (from your Profile)", 230);
            var target = Pick("Your target", new[] { "3 months", "4 months", "6 months", "12 months" }, 0, 150);

            _compute = () =>
            {
                decimal e = NumOr(essentials, 0), s = NumOr(savings, 0);
                int months = target.SelectedIndex switch { 1 => 4, 2 => 6, 3 => 12, _ => 3 };
                var covered = Calculators.MonthsCovered(s, e);
                var goal = e * months;
                Tile("Your buffer lasts", covered is decimal c ? $"{c:0.0} months" : "–", null,
                    covered is decimal c2 ? c2 < 1 ? Look.Bad : c2 < 3 ? Look.Warn : Look.Good : null);
                Tile($"{months}-month buffer", Money.Format(goal));
                Tile(s >= goal ? "Above target by" : "Still to save", Money.Format(Math.Abs(goal - s)), null, s >= goal ? Look.Good : Look.Warn);
                if (s < goal && AvgLeftOver > 0)
                {
                    int m = (int)Math.Ceiling((goal - s) / AvgLeftOver);
                    Note($"Putting your average left-over of {Money.Format(AvgLeftOver)} aside each month, you'd reach it in about {MonthsText(m)}.");
                }
                Note("A common guideline is 3-6 months of essential costs; more if your income is irregular or you're self-employed. Keep it in a savings account you can reach quickly, not in investments.");
            };
        }

        private void BuildStudentLoan()
        {
            var studentDebt = _snap.Debts.Where(d => d.Kind == BalanceKind.StudentLoan).ToList();
            var debt = Field("Student debt (€)", studentDebt.Count > 0 ? studentDebt.Sum(d => d.Amount) : 20000m);
            var rate = Field("Interest per year (%)", studentDebt.FirstOrDefault()?.InterestRate ?? 2.33m,
                "DUO's 2026 rate under the SF35 rules is 2.33% (SF15: 2.29%). The rate is fixed for 5-year periods - see Mijn DUO for yours.", 150);
            var years = Field("Repayment term (years)", 35m, "SF35 rules: 35 years. SF15 (older loans): 15 years.", 170, whole: true);
            var extra = Field("Extra per month (€)", 0m, null, 150);

            _compute = () =>
            {
                decimal d = NumOr(debt, 0), r = NumOr(rate, 0), x = NumOr(extra, 0);
                int months = (int)Math.Clamp(NumOr(years, 35), 1, 40) * 12;
                var normal = Calculators.Annuity(d, r, months);
                Tile("Monthly repayment", Money.Format(normal.FirstPayment), $"Over {months / 12} years", Look.Accent);
                Tile("Total interest", Money.Format(normal.TotalInterest));
                if (x > 0)
                {
                    var faster = Calculators.DebtPayoff(new[] { new DebtInput("DUO", d, r, normal.FirstPayment) }, x, PayoffStrategy.Avalanche);
                    if (faster.PaysOff)
                        Tile("With the extra", MonthsText(faster.Months), $"Saves {Money.Format(normal.TotalInterest - faster.TotalInterest)} interest", Look.Good);
                }
                Note("DUO caps your monthly payment based on your income (draagkracht), and under SF35 any debt left after 35 years is cancelled. So your real payment can be lower than shown here. Mijn DUO shows your actual amount.");
                Note("Paying off student debt early is a personal choice: the interest is low, but mortgage lenders count student debt against how much you can borrow.");
            };
        }

        private void BuildSplit()
        {
            var income = Field("Net income per month (€)", _snap.AvgIncome > 0 ? Math.Round(_snap.AvgIncome) : 2000m, "Pre-filled with your average income");
            _compute = () =>
            {
                decimal inc = NumOr(income, 0);
                var (needs, wants, savings) = Calculators.Split503020(inc);
                Tile("Needs (50%)", Money.Format(needs), "Housing, groceries, insurance, energy, transport");
                Tile("Wants (30%)", Money.Format(wants), "Eating out, hobbies, shopping, holidays");
                Tile("Save / repay (20%)", Money.Format(savings), "Buffer, goals, investing, extra debt payments", Look.Good);
                if (_snap.HasData && _snap.AvgIncome > 0)
                {
                    decimal ai = _snap.AvgIncome;
                    Heading("What you actually do (average per month)");
                    NewTiles();
                    Tile("Needs", $"{Look.Pct(_snap.NeedsShare)}", Money.Format(Math.Round(_snap.NeedsShare * ai)), _snap.NeedsShare > 0.6m ? Look.Warn : null);
                    Tile("Wants", $"{Look.Pct(_snap.WantsShare)}", Money.Format(Math.Round(_snap.WantsShare * ai)), _snap.WantsShare > 0.4m ? Look.Warn : null);
                    Tile("Saved", $"{Look.Pct(_snap.SavingsShare)}", Money.Format(Math.Round(_snap.SavingsShare * ai)), _snap.SavingsShare >= 0.2m ? Look.Good : Look.Warn);
                    Note("Your categories are sorted into needs and wants by name (e.g. Housing and Groceries are needs, Eating out is a want). It's a rough guide, not an exact split.");
                }
                Note("When rent is high (common for students and in big cities), 50% for needs may not be realistic. Then the goal is simply to keep wants in check and save something every month.");
            };
        }

        private void BuildInflation()
        {
            var amount = Field("Amount today (€)", 100m);
            var rate = Field("Inflation per year (%)", 2.5m, "The European Central Bank aims for 2% a year; it has been much higher at times.", 150);
            var years = Field("Years", 10m, whole: true, width: 120);
            _compute = () =>
            {
                decimal a = NumOr(amount, 0), r = NumOr(rate, 0);
                int y = (int)Math.Clamp(NumOr(years, 10), 0, 100);
                Tile($"Costs in {y} years", Money.Format(Calculators.FutureCost(a, r, y)), $"What {Money.Format(a)} of shopping costs then");
                Tile("What your money buys then", Money.Format(Calculators.PurchasingPower(a, r, y)), $"{Money.Format(a)} kept under the mattress, in today's money", Look.Warn);
                Note("That's why money sitting still loses value. Interest on savings only keeps up if it's at least as high as inflation.");
            };
        }

        private void BuildFi()
        {
            var invested = _snap.Assets.Where(a => a.Kind is BalanceKind.Investments or BalanceKind.Pension).Sum(a => a.Amount);
            var spending = Field("Yearly spending (€)", _snap.AvgExpenses > 0 ? Math.Round(_snap.AvgExpenses * 12) : 24000m, "Pre-filled: your average monthly spending × 12");
            var start = Field("Already invested (€)", invested);
            var monthly = Field("Invested per month (€)", AvgLeftOver > 0 ? AvgLeftOver : 300m);
            var ret = Field("Return after inflation (%)", 4m, "Long-term stock-market averages after inflation are often put at 4-5%, but years differ a lot.", 170);
            var swr = Field("Withdrawal rate (%)", 4m, "The '4% rule': withdraw 4% of your investments per year.", 150);
            _compute = () =>
            {
                var fi = Calculators.FinancialIndependence(NumOr(spending, 0), NumOr(start, 0), NumOr(monthly, 0), NumOr(ret, 0), NumOr(swr, 4));
                Tile("Amount needed", Money.Format(fi.TargetAmount), "Investments whose returns cover your yearly spending", Look.Accent);
                Tile("Time to get there", fi.Months is int m ? MonthsText(m) : "100+ years", fi.Months is int m2 ? $"Around {When(m2)}" + (_snap.Age is int age ? $", age {age + m2 / 12}" : "") : null);
                GrowthChart(fi.Years, "Investments", "Deposited");
                Note("A thought experiment, not a plan: returns vary, and the Dutch state pension (AOW) and workplace pensions aren't included. It shows how saving more and spending less both shorten the road.");
            };
        }

        private void BuildCar()
        {
            var price = Field("Purchase price (€)", 15000m);
            var resale = Field("Worth when you sell it (€)", 7000m);
            var years = Field("Years you keep it", 5m, whole: true, width: 150);
            var km = Field("Kilometres per year", 12000m, whole: true, width: 160);
            var fuel = Field("Fuel price (€/litre)", 2.0m, null, 150);
            var usage = Field("Km per litre", 18m, null, 120);
            var insurance = Field("Insurance per month (€)", 60m, null, 170);
            var tax = Field("Road tax per month (€)", 35m, "Motorrijtuigenbelasting - depends on weight, fuel and province", 170);
            var maintenance = Field("Maintenance per year (€)", 600m, null, 180);
            _compute = () =>
            {
                var c = Calculators.CarCost(NumOr(price, 0), NumOr(resale, 0), (int)Math.Max(1, NumOr(years, 5)), NumOr(km, 0),
                    NumOr(fuel, 0), NumOr(usage, 0), NumOr(insurance, 0), NumOr(tax, 0), NumOr(maintenance, 0));
                Tile("Real cost per month", Money.Format(c.MonthlyTotal), null, Look.Accent);
                Tile("Per kilometre", c.PerKm.ToString("€ 0.00", Money.Culture));
                Tile("Value loss", Money.Format(c.Depreciation) + "/month", "Often the biggest cost - and invisible");
                Tile("Fuel", Money.Format(c.Fuel) + "/month");
                Tile("Fixed costs", Money.Format(c.Fixed) + "/month", "Insurance, road tax, maintenance");
                Note("Compare the cost per kilometre with public transport, a shared car or a (e-)bike for your usual trips. Parking, tolls and financing interest aren't included.");
            };
        }
    }
}
