using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.Win32;

namespace BudgetApp.Pages
{
    public partial class DashboardPage : UserControl, IPage
    {
        private bool _saving;

        public DashboardPage()
        {
            InitializeComponent();
            PeriodBar.PeriodChanged += Refresh;
            QuickDate.SelectedDate = DateTime.Today;
        }

        public void OnShown(object? argument)
        {
            PeriodBar.Refresh();
            Refresh();
        }

        private void Refresh()
        {
            var store = AppState.Store;
            var period = PeriodBar.Period;
            var today = DateTime.Today;
            var name = store.GetProfile().Name;
            TitleText.Text = string.IsNullOrWhiteSpace(name) ? "Dashboard" : $"Hi {name.Split(' ')[0]}";

            // New quick-add entries default to a date in the month being viewed.
            if (period.Month is DateTime m && !(m.Year == today.Year && m.Month == today.Month) &&
                QuickDate.SelectedDate is DateTime d && !period.Contains(d))
                QuickDate.SelectedDate = m;

            var s = BudgetCalculator.Summarize(store.Transactions, store.Recurring, period, today);

            IncomeText.Text = Money.Format(s.Income);
            IncomeSub.Text = s.Income == 0 ? "No income in this period"
                : s.RecurringIncome > 0 ? $"{Money.Format(s.OneOffIncome)} received  +  {Money.Format(s.RecurringIncome)} programmed"
                : "From your income entries";
            ExpensesText.Text = Money.Format(s.TotalExpenses);
            ExpensesSub.Text = $"{Money.Format(s.OneOffExpenses)} one-off  +  {Money.Format(s.RecurringExpenses)} programmed";
            NetText.Text = Money.Format(s.Net);
            NetText.Foreground = s.Net >= 0 ? Look.Accent : Look.Bad;
            NetSub.Text = s.SavingsRate is decimal rate
                ? rate >= 0 ? $"You kept {Look.Pct(rate)} of your income" : $"Overspent by {Look.Pct(-rate)} of your income"
                : " ";

            // Pie
            bool hasPie = Look.FillPie(Pie, s, PieLegend);
            ChartEmpty.Visibility = hasPie ? Visibility.Collapsed : Visibility.Visible;
            if (!hasPie) { ChartHeader.Text = "Where the income goes"; ChartSubHeader.Text = " "; }
            else if (s.SlicesAreShareOfIncome) { ChartHeader.Text = "Where the income goes"; ChartSubHeader.Text = $"Share of {Money.Format(s.Income)} income"; }
            else
            {
                ChartHeader.Text = "How the spending splits";
                ChartSubHeader.Text = s.Income > 0 ? $"Spending is {Money.Format(s.TotalExpenses - s.Income)} more than income" : "No income entered for this period";
            }

            // Budgets for the viewed month (or this month when looking at all time)
            var budgetMonth = period.Month ?? new DateTime(today.Year, today.Month, 1);
            BudgetsHeader.Text = period.IsAllTime ? "Budgets this month" : $"Budgets in {budgetMonth.ToString("MMMM", CultureInfo.InvariantCulture)}";
            var budgets = BudgetCalculator.BudgetStatuses(store.Budgets, store.Transactions, store.Recurring, budgetMonth, today)
                .Select(b => new BudgetView(b)).ToList();
            BudgetList.ItemsSource = budgets;
            NoBudgetsText.Visibility = budgets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Latest entries in the period
            var recent = store.Transactions.Where(t => period.Contains(t.Date))
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .Take(30).Select(t => new TxView(t)).ToList();
            RecentGrid.ItemsSource = recent;
            RecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            QuickCategory.ItemsSource = AppState.Categories();
            BuildAttention(s, budgets, budgetMonth);
        }

        private void BuildAttention(BudgetSummary s, List<BudgetView> budgets, DateTime budgetMonth)
        {
            var store = AppState.Store;
            var items = new List<AttentionItem>();

            int uncategorized = store.Transactions.Count(t =>
                t.Type != TransactionType.Transfer && string.Equals(t.Category, BudgetCalculator.Uncategorized, StringComparison.OrdinalIgnoreCase));
            if (uncategorized > 0)
                items.Add(new AttentionItem($"{uncategorized} transaction{(uncategorized == 1 ? " has" : "s have")} no category yet.",
                    "Sort them", () => AppState.Navigate(PageKey.Transactions, TransactionsPage.ShowUncategorized)));

            foreach (var b in budgets.Where(b => b.Status.IsOver).Take(2))
                items.Add(new AttentionItem($"{b.Category} is {b.RemainingText} budget this month.", "See budgets",
                    () => AppState.Navigate(PageKey.Budgets)));

            if (store.Transactions.Count == 0)
                items.Add(new AttentionItem("Save typing: import the transactions from your bank's website.", "Import a bank file",
                    () => AppState.Navigate(PageKey.Import)));
            else if (s.Income == 0 && !PeriodBar.Period.IsAllTime)
                items.Add(new AttentionItem("No income recorded in this period. Add your salary or allowance, or make it a programmed income.",
                    "Add programmed income", () => AppState.Navigate(PageKey.Programmed)));

            var lastImport = store.Imports.FirstOrDefault();
            if (lastImport != null && (DateTime.Now - lastImport.ImportedAt).TotalDays > 31)
                items.Add(new AttentionItem($"Your last bank import was on {lastImport.ImportedAt:d MMMM}.", "Import new transactions",
                    () => AppState.Navigate(PageKey.Import)));

            if (store.Balances.Count == 0 && store.Transactions.Count > 20)
                items.Add(new AttentionItem("Fill in your savings and debts to get a full financial health check.", "Open profile",
                    () => AppState.Navigate(PageKey.Profile)));

            AttentionList.ItemsSource = items;
            AllGoodText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Attention_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is AttentionItem item) item.Action();
        }

        // ------------------------------------------------------------------ quick add

        private void QuickAdd_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; QuickAdd_Click(sender, e); }
        }

        private async void QuickAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_saving) return;
            if (!Look.ReadAmount(QuickAmount, out var amount)) return;
            var category = (QuickCategory.Text ?? "").Trim();
            if (category.Length == 0)
            {
                Ui.Warn("Please choose or type a category.", "Missing category");
                QuickCategory.Focus();
                return;
            }

            var t = new Transaction
            {
                Date = (QuickDate.SelectedDate ?? DateTime.Today).Date,
                Type = QuickIn.IsChecked == true ? TransactionType.Income : TransactionType.Expense,
                Category = category,
                Amount = amount,
                Description = QuickDescription.Text?.Trim() ?? ""
            };

            _saving = true;
            try
            {
                await AppState.Store.AddTransactionAsync(t);
            }
            catch (Exception ex)
            {
                Ui.Error("The entry couldn't be saved.", ex);
                return;
            }
            finally { _saving = false; }

            QuickAmount.Clear();
            QuickDescription.Clear();
            QuickCategory.Text = "";
            if (!PeriodBar.Period.Contains(t.Date))
            {
                AppState.Period = BudgetPeriod.ForMonth(t.Date);
                PeriodBar.Refresh();
            }
            Refresh();
            QuickAmount.Focus();
            AppState.Toast($"Added {Look.Signed(t)} · {t.Category}", "Undo", async () =>
            {
                try { await AppState.Store.DeleteTransactionsAsync(new[] { t }); }
                catch (Exception ex) { Ui.Error("Couldn't undo.", ex); }
            });
        }

        // ------------------------------------------------------------------ navigation & export

        private void ManageBudgets_Click(object sender, RoutedEventArgs e) => AppState.Navigate(PageKey.Budgets);
        private void AllTransactions_Click(object sender, RoutedEventArgs e) => AppState.Navigate(PageKey.Transactions);

        private void RecentGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecentGrid.SelectedItem is TxView v) AppState.Navigate(PageKey.Transactions, v.Source);
        }

        private void Export_Click(object sender, RoutedEventArgs e) => ExportCsv(PeriodBar.Period);

        public static void ExportCsv(BudgetPeriod period)
        {
            var namePart = period.Month is DateTime m ? m.ToString("yyyy-MM") : "All";
            var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"Budget_{namePart}_{DateTime.Now:yyyy-MM-dd}.csv"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var csv = CsvExportService.Build(AppState.Store.Transactions, AppState.Store.Recurring, period, DateTime.Today, CultureInfo.CurrentCulture);
                CsvExportService.Export(dialog.FileName, csv);
                AppState.Toast($"Exported {period.Label.ToLower(CultureInfo.InvariantCulture)} to {System.IO.Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                Ui.Error("The file couldn't be exported.", ex);
            }
        }
    }
}
