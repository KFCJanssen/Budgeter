using System;
using System.Windows;
using System.Windows.Controls;
using BudgetApp.Services;

namespace BudgetApp.Controls
{
    /// <summary>◀ October 2026 ▶ [This month] [All time] - changes the shared AppState.Period.</summary>
    public partial class PeriodBar : UserControl
    {
        public event Action? PeriodChanged;

        private bool _allowAllTime = true;
        public bool AllowAllTime
        {
            get => _allowAllTime;
            set { _allowAllTime = value; AllTimeButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed; Refresh(); }
        }

        public PeriodBar()
        {
            InitializeComponent();
            Refresh();
        }

        /// <summary>The period this bar shows (pages without "All time" always get a month).</summary>
        public BudgetPeriod Period => !AllowAllTime && AppState.Period.IsAllTime ? BudgetPeriod.ForMonth(DateTime.Today) : AppState.Period;

        public void Refresh()
        {
            var p = Period;
            LabelText.Text = p.Label;
            PrevButton.IsEnabled = NextButton.IsEnabled = !p.IsAllTime;
        }

        private void Set(BudgetPeriod p)
        {
            AppState.Period = p;
            Refresh();
            PeriodChanged?.Invoke();
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => Set(Period.Previous());
        private void Next_Click(object sender, RoutedEventArgs e) => Set(Period.Next());
        private void ThisMonth_Click(object sender, RoutedEventArgs e) => Set(BudgetPeriod.ForMonth(DateTime.Today));
        private void AllTime_Click(object sender, RoutedEventArgs e) => Set(BudgetPeriod.AllTime);
    }
}
