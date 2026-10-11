using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BudgetApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace BudgetApp.Pages
{
    public partial class TrendsPage : UserControl, IPage
    {
        private List<MonthTotals> _months = new();
        private bool _loading;

        public TrendsPage()
        {
            InitializeComponent();
            Look.Prepare(MonthChart);
            Look.Prepare(CategoryChart);
        }

        public void OnShown(object? argument) => Refresh();

        private int MonthCount => Range6.IsChecked == true ? 6 : Range24.IsChecked == true ? 24 : 12;

        private void Range_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) Refresh();
        }

        private static Axis MoneyAxis() => new()
        {
            Labeler = v => Money.Format((decimal)Math.Round(v)).Replace(",00", ""),
            LabelsPaint = new SolidColorPaint(Look.AxisText),
            SeparatorsPaint = new SolidColorPaint(Look.GridLine),
            TextSize = 12
        };

        private Axis MonthAxis() => new()
        {
            Labels = _months.Select(m => MonthCount > 12 ? m.Month.ToString("MMM yy", CultureInfo.InvariantCulture) : m.ShortLabel).ToArray(),
            LabelsPaint = new SolidColorPaint(Look.AxisText),
            TextSize = 12
        };

        private void Refresh()
        {
            var store = AppState.Store;
            var today = DateTime.Today;
            _months = BudgetCalculator.MonthlyTotals(store.Transactions, store.Recurring, today, MonthCount, today);

            // Averages over complete months that have data (not the current month, not empty months before you started).
            var complete = BudgetCalculator.RecentMonthsWithData(store.Transactions, store.Recurring, today, MonthCount);
            bool hasData = _months.Any(m => m.Income > 0 || m.Expenses > 0);
            ChartEmpty.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;

            if (complete.Count > 0)
            {
                decimal inc = complete.Average(m => m.Income), exp = complete.Average(m => m.Expenses);
                AvgIncome.Text = Money.Format(Math.Round(inc, 2));
                AvgSpending.Text = Money.Format(Math.Round(exp, 2));
                AvgNote.Text = $"Over {complete.Count} complete month{(complete.Count == 1 ? "" : "s")}";
                AvgRate.Text = inc > 0 ? Look.Pct((inc - exp) / inc) : "–";
                AvgRate.Foreground = inc - exp >= 0 ? Look.Accent : Look.Bad;
                AvgSaved.Text = $"{Money.Format(Math.Round(inc - exp, 2))} left over a month";
                var best = complete.OrderByDescending(m => m.Net).First();
                BestMonth.Text = best.Month.ToString("MMMM", CultureInfo.InvariantCulture);
                BestNote.Text = $"{Money.Format(best.Net)} left over";
                if (complete.Count >= 6)
                {
                    var recent = complete.TakeLast(3).Average(m => m.Expenses);
                    var before = complete.Skip(complete.Count - 6).Take(3).Average(m => m.Expenses);
                    var change = before > 0 ? (recent - before) / before : 0;
                    SpendTrend.Text = change >= 0
                        ? $"Last 3 months {Look.Pct(change)} higher than the 3 before"
                        : $"Last 3 months {Look.Pct(-change)} lower than the 3 before";
                }
                else SpendTrend.Text = "Trend shows after 6 months";
            }
            else
            {
                AvgIncome.Text = AvgSpending.Text = AvgRate.Text = BestMonth.Text = "–";
                AvgNote.Text = "Needs one complete month";
                SpendTrend.Text = AvgSaved.Text = BestNote.Text = "";
            }

            MonthChart.Series = new ISeries[]
            {
                new ColumnSeries<decimal>
                {
                    Name = "Income", Values = _months.Select(m => m.Income).ToArray(),
                    Fill = new SolidColorPaint(Look.IncomeColor), MaxBarWidth = 22,
                    ToolTipLabelFormatter = p => Money.Format((decimal)p.Coordinate.PrimaryValue)
                },
                new ColumnSeries<decimal>
                {
                    Name = "Spending", Values = _months.Select(m => m.Expenses).ToArray(),
                    Fill = new SolidColorPaint(Look.ExpenseColor), MaxBarWidth = 22,
                    ToolTipLabelFormatter = p => Money.Format((decimal)p.Coordinate.PrimaryValue)
                },
                new LineSeries<decimal>
                {
                    Name = "Left over", Values = _months.Select(m => m.Net).ToArray(),
                    Stroke = new SolidColorPaint(Look.NetColor) { StrokeThickness = 3 }, Fill = null, GeometrySize = 8,
                    GeometryStroke = new SolidColorPaint(Look.NetColor) { StrokeThickness = 3 },
                    ToolTipLabelFormatter = p => Money.Format((decimal)p.Coordinate.PrimaryValue)
                }
            };
            MonthChart.XAxes = new[] { MonthAxis() };
            MonthChart.YAxes = new[] { MoneyAxis() };

            MonthGrid.ItemsSource = _months.AsEnumerable().Reverse().ToList();

            _loading = true;
            var current = CategoryPicker.SelectedItem as string;
            var categories = BudgetCalculator.SpendingByCategory(store.Transactions, store.Recurring, BudgetPeriod.AllTime, today)
                .Select(kv => kv.Key).ToList();
            CategoryPicker.ItemsSource = categories;
            CategoryPicker.SelectedItem = current != null && categories.Contains(current) ? current : categories.FirstOrDefault();
            _loading = false;
            DrawCategory();
        }

        private void CategoryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) DrawCategory();
        }

        private void DrawCategory()
        {
            var store = AppState.Store;
            if (CategoryPicker.SelectedItem is not string category)
            {
                CategoryChart.Series = Array.Empty<ISeries>();
                CategoryNote.Text = "Pick a category to see how it changes.";
                return;
            }
            var values = _months.Select(m => BudgetCalculator.SpendingByCategory(store.Transactions, store.Recurring, BudgetPeriod.ForMonth(m.Month), DateTime.Today)
                .Where(kv => string.Equals(kv.Key, category, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value)).ToArray();
            var budget = store.Budgets.FirstOrDefault(b => string.Equals(b.Category.Trim(), category, StringComparison.OrdinalIgnoreCase));

            var series = new List<ISeries>
            {
                new LineSeries<decimal>
                {
                    Name = category, Values = values,
                    Stroke = new SolidColorPaint(Look.ChartColors[1]) { StrokeThickness = 3 },
                    Fill = new SolidColorPaint(Look.ChartColors[1].WithAlpha(40)),
                    GeometrySize = 7, GeometryStroke = new SolidColorPaint(Look.ChartColors[1]) { StrokeThickness = 3 },
                    ToolTipLabelFormatter = p => Money.Format((decimal)p.Coordinate.PrimaryValue)
                }
            };
            if (budget != null)
                series.Add(new LineSeries<decimal>
                {
                    Name = "Budget", Values = values.Select(_ => budget.MonthlyLimit).ToArray(),
                    Stroke = new SolidColorPaint(Look.AxisText) { StrokeThickness = 2 }, Fill = null, GeometrySize = 0
                });
            CategoryChart.Series = series;
            CategoryChart.XAxes = new[] { MonthAxis() };
            CategoryChart.YAxes = new[] { MoneyAxis() };

            var nonZero = values.Where(v => v > 0).ToList();
            CategoryNote.Text = nonZero.Count == 0 ? "No spending in this category in this range."
                : $"Average {Money.Format(Math.Round(nonZero.Average(), 2))} in months with spending" +
                  (budget != null ? $" · budget {Money.Format(budget.MonthlyLimit)}" : "");
        }
    }
}
