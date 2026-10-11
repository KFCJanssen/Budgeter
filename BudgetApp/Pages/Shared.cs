using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BudgetApp.Models;
using BudgetApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using LiveChartsCore.Kernel.Sketches;
using SkiaSharp;

namespace BudgetApp.Pages
{
    /// <summary>Colours and small helpers shared by the pages.</summary>
    public static class Look
    {
        public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
        public static Brush Good => Res("GoodBrush");
        public static Brush Bad => Res("BadBrush");
        public static Brush Warn => Res("WarnBrush");
        public static Brush Accent => Res("AccentBrush");
        public static Brush Text => Res("TextBrush");
        public static Brush Faint => Res("FaintBrush");

        public static readonly SKColor[] ChartColors =
        {
            SKColor.Parse("#4361EE"), SKColor.Parse("#FF6B6B"), SKColor.Parse("#FFB84C"), SKColor.Parse("#9B5DE5"),
            SKColor.Parse("#00BBF9"), SKColor.Parse("#F15BB5"), SKColor.Parse("#5C7AEA"), SKColor.Parse("#F9844A"),
            SKColor.Parse("#43AA8B"), SKColor.Parse("#B5838D")
        };
        public static readonly SKColor LeftOver = SKColor.Parse("#2EC4B6");
        public static readonly SKColor IncomeColor = SKColor.Parse("#2EC4B6");
        public static readonly SKColor ExpenseColor = SKColor.Parse("#FF6B6B");
        public static readonly SKColor NetColor = SKColor.Parse("#4361EE");
        public static readonly SKColor AxisText = SKColor.Parse("#767A8A");
        public static readonly SKColor GridLine = SKColor.Parse("#EEF0F5");

        public static string Pct(decimal d) => d.ToString("P0", Money.Culture);

        public static string Signed(Transaction t) => t.Type switch
        {
            TransactionType.Income => "+" + Money.Format(t.Amount),
            TransactionType.Expense => "−" + Money.Format(t.Amount),
            _ => Money.Format(t.Amount)
        };

        public static void Prepare(IChartView chart)
        {
            if (AppState.NoAnimations) chart.AnimationsSpeed = TimeSpan.Zero;
        }

        /// <summary>Builds the "where the income goes" pie. Returns false when there's nothing to show.</summary>
        public static bool FillPie(PieChart pie, BudgetSummary s)
        {
            Prepare(pie);
            decimal whole = s.SlicesAreShareOfIncome ? s.Income : s.TotalExpenses;
            if (s.Slices.Count == 0 || whole <= 0)
            {
                pie.Series = Array.Empty<ISeries>();
                return false;
            }

            // Keep the chart readable: the 7 largest categories, the rest as "Other".
            var slices = s.Slices.Where(x => !x.IsLeftOver).ToList();
            if (slices.Count > 8)
            {
                var top = slices.Take(7).ToList();
                top.Add(new PieSlice("Other categories", slices.Skip(7).Sum(x => x.Amount)));
                slices = top;
            }
            slices.AddRange(s.Slices.Where(x => x.IsLeftOver));

            int colorIndex = 0;
            pie.Series = slices.Select(slice =>
            {
                var color = slice.IsLeftOver ? LeftOver : ChartColors[colorIndex++ % ChartColors.Length];
                var share = slice.Amount / whole;
                return new PieSeries<decimal>
                {
                    Values = new[] { slice.Amount },
                    Name = slice.Label,
                    ToolTipLabelFormatter = _ => $"{Money.Format(slice.Amount)} ({Pct(share)})",
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsSize = 12,
                    DataLabelsFormatter = _ => share >= 0.06m ? Pct(share) : "",
                    Fill = new SolidColorPaint(color)
                };
            }).ToArray<ISeries>();
            return true;
        }

        /// <summary>Lets Enter submit a form, but not inside a multi-line box or an open drop-down.</summary>
        public static bool IsSubmitKey(KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return false;
            if (e.OriginalSource is TextBox tb && tb.AcceptsReturn) return false;
            if (e.OriginalSource is DependencyObject d && FindParent<ComboBox>(d) is ComboBox cb && cb.IsDropDownOpen) return false;
            return true;
        }

        public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match) return match;
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>Reads a euro amount from a text box; shows a warning and focuses the box when invalid.</summary>
        public static bool ReadAmount(TextBox box, out decimal amount, bool allowZero = false)
        {
            if (Money.TryParse(box.Text, out amount) && (allowZero ? amount >= 0 : amount > 0)) return true;
            Ui.Warn(allowZero ? "Please enter a valid amount, e.g. 12,50 or 12.50." : "Please enter a valid positive amount, e.g. 12,50 or 12.50.", "Invalid amount");
            box.Focus();
            box.SelectAll();
            return false;
        }

        /// <summary>Optional percentage like "2,33" or "2.33%".</summary>
        public static decimal? ReadPercent(string text)
        {
            var t = (text ?? "").Replace("%", "").Trim();
            if (t.Length == 0) return null;
            return Money.TryParse(t, out var v) ? v : null;
        }

        public static string AmountForEditing(decimal d) => d.ToString("0.00", Money.Culture);
    }

    /// <summary>A transaction as shown in short lists.</summary>
    public sealed class TxView
    {
        public TxView(Transaction t)
        {
            Source = t;
            Title = !string.IsNullOrWhiteSpace(t.Payee) ? t.Payee!
                  : !string.IsNullOrWhiteSpace(t.Description) ? t.Description
                  : t.Category;
            var extra = t.Type == TransactionType.Transfer ? " · transfer" : t.RecurringId != null ? " · programmed" : "";
            Subtitle = $"{t.Date.ToString("d MMM", CultureInfo.InvariantCulture)} · {t.Category}{extra}";
            AmountText = Look.Signed(t);
            Brush = t.Type == TransactionType.Transfer || t.RecurringId != null ? Look.Faint
                  : t.Type == TransactionType.Income ? Look.Good : Look.Bad;
        }

        public Transaction Source { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string AmountText { get; }
        public Brush Brush { get; }
    }

    /// <summary>A budget line with its progress bar.</summary>
    public sealed class BudgetView
    {
        public BudgetView(BudgetStatus s, Budget? source = null)
        {
            Source = source;
            Status = s;
            Category = s.Category;
            AmountText = $"{Money.Format(s.Spent)} of {Money.Format(s.Limit)}";
            RemainingText = s.IsOver ? $"{Money.Format(-s.Remaining)} over" : $"{Money.Format(s.Remaining)} left";
            Bar = (double)Math.Min(1m, s.Ratio);
            Brush = s.Level switch { 2 => Look.Bad, 1 => Look.Warn, _ => Look.Good };
        }

        public Budget? Source { get; }
        public BudgetStatus Status { get; }
        public string Category { get; }
        public string AmountText { get; }
        public string RemainingText { get; }
        public double Bar { get; }
        public Brush Brush { get; }
    }

    /// <summary>A line in the dashboard's "To do" list.</summary>
    public sealed record AttentionItem(string Text, string ActionText, Action Action);
}
