using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using SkiaSharp;

namespace BudgetApp
{
    public partial class MainWindow : Window
    {
        // EF Core context - one instance lives for the lifetime of the window.
        private readonly BudgetContext _context = new();

        // Everything in the database, kept in memory (personal budgets are small).
        private readonly List<Transaction> _allTransactions = new();

        // What the grids show. Transactions are filtered to the selected period.
        public ObservableCollection<Transaction> VisibleTransactions { get; } = new();
        public ObservableCollection<RecurringExpense> RecurringExpenses { get; } = new();

        // Suggested categories for the editable ComboBoxes; previously used categories are added on top.
        private static readonly string[] DefaultCategories =
        {
            "Salary", "Housing", "Groceries", "Transport", "Utilities", "Insurance",
            "Subscriptions", "Discretionary", "Savings", "Other"
        };

        private static readonly SKColor[] SliceColors =
        {
            SKColor.Parse("#FF6B6B"), SKColor.Parse("#4361EE"), SKColor.Parse("#FFB84C"),
            SKColor.Parse("#9B5DE5"), SKColor.Parse("#00BBF9"), SKColor.Parse("#F15BB5"),
            SKColor.Parse("#5C7AEA"), SKColor.Parse("#F9844A")
        };
        private static readonly SKColor LeftOverColor = SKColor.Parse("#2EC4B6");

        private BudgetPeriod _period = BudgetPeriod.ForMonth(DateTime.Today);
        private Transaction? _editing;   // non-null while the form edits an existing entry

        public MainWindow()
        {
            InitializeComponent();

            TransactionsGrid.ItemsSource = VisibleTransactions;
            RecurringGrid.ItemsSource = RecurringExpenses;
            TransactionDatePicker.SelectedDate = DateTime.Today;
            RecurringStartDatePicker.SelectedDate = DateTime.Today;

            Loaded += MainWindow_Loaded;
        }

        // ===================== STARTUP: LOAD FROM DATABASE =====================
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Creates the SQLite file + tables on first run; does nothing if they already exist.
                await _context.Database.EnsureCreatedAsync();

                _allTransactions.AddRange(await _context.Transactions.ToListAsync());
                foreach (var r in (await _context.RecurringExpenses.ToListAsync()).OrderBy(r => r.Name))
                    RecurringExpenses.Add(r);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open your budget database:\n{ex.Message}", "Budgeter",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshCategorySuggestions();
            RefreshDashboard();
            AmountTextBox.Focus();
        }

        // ===================== PERIOD NAVIGATION =====================
        private void PrevPeriodButton_Click(object sender, RoutedEventArgs e) => SetPeriod(_period.Previous());
        private void NextPeriodButton_Click(object sender, RoutedEventArgs e) => SetPeriod(_period.Next());
        private void ThisMonthButton_Click(object sender, RoutedEventArgs e) => SetPeriod(BudgetPeriod.ForMonth(DateTime.Today));
        private void AllTimeButton_Click(object sender, RoutedEventArgs e) => SetPeriod(BudgetPeriod.AllTime);

        private void SetPeriod(BudgetPeriod period)
        {
            _period = period;

            // New entries default to a date inside the month being viewed.
            if (_editing == null && period.Month is DateTime m)
            {
                bool viewingThisMonth = m.Year == DateTime.Today.Year && m.Month == DateTime.Today.Month;
                TransactionDatePicker.SelectedDate = viewingThisMonth ? DateTime.Today : m;
            }
            RefreshDashboard();
        }

        // ===================== ADD / EDIT TRANSACTION =====================
        private async void SaveTransactionButton_Click(object sender, RoutedEventArgs e) => await SaveTransactionAsync();

        private void TransactionForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = SaveTransactionAsync(); }
            else if (e.Key == Key.Escape && _editing != null) { e.Handled = true; ExitEditMode(); }
        }

        private bool _saving;   // guards against double submits (e.g. holding Enter)

        private async System.Threading.Tasks.Task SaveTransactionAsync()
        {
            if (_saving) return;
            _saving = true;
            try { await SaveTransactionCoreAsync(); }
            finally { _saving = false; }
        }

        private async System.Threading.Tasks.Task SaveTransactionCoreAsync()
        {
            var category = (CategoryComboBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(category))
            {
                Warn("Please enter a category.", "Missing Category");
                CategoryComboBox.Focus();
                return;
            }

            if (!Money.TryParse(AmountTextBox.Text, out var amount) || amount <= 0)
            {
                Warn("Please enter a valid positive amount, e.g. 12,50 or 12.50.", "Invalid Amount");
                AmountTextBox.Focus();
                AmountTextBox.SelectAll();
                return;
            }

            var target = _editing ?? new Transaction();
            target.Date = (TransactionDatePicker.SelectedDate ?? DateTime.Today).Date;
            target.Type = TypeComboBox.SelectedIndex == 0 ? TransactionType.Income : TransactionType.Expense;
            target.Category = category;
            target.Amount = amount;
            target.Description = DescriptionTextBox.Text?.Trim() ?? "";

            if (_editing == null) _context.Transactions.Add(target);

            if (!await TrySaveAsync()) return;

            // Jump to the month of the entry so it is visible straight away.
            if (!_period.Contains(target.Date)) _period = BudgetPeriod.ForMonth(target.Date);

            if (_editing == null)
            {
                _allTransactions.Add(target);
                ClearTransactionForm(keepType: true);
            }
            else
            {
                ExitEditMode();
            }

            RefreshCategorySuggestions();
            RefreshDashboard();
            AmountTextBox.Focus();
        }

        private void ClearTransactionForm(bool keepType)
        {
            AmountTextBox.Clear();
            DescriptionTextBox.Clear();
            CategoryComboBox.Text = "";
            if (!keepType) TypeComboBox.SelectedIndex = 1;
            // Keep the date as is: entering several receipts from the same day is common.
        }

        private void EnterEditMode(Transaction t)
        {
            _editing = t;
            TypeComboBox.SelectedIndex = t.Type == TransactionType.Income ? 0 : 1;
            CategoryComboBox.Text = t.Category;
            AmountTextBox.Text = t.Amount.ToString("0.00", Money.Culture);   // 12,50
            TransactionDatePicker.SelectedDate = t.Date;
            DescriptionTextBox.Text = t.Description;

            TransactionFormHeader.Text = "Edit Entry";
            SaveTransactionButton.Content = "Save Changes";
            CancelEditButton.Visibility = Visibility.Visible;
            AmountTextBox.Focus();
            AmountTextBox.SelectAll();
        }

        private void ExitEditMode()
        {
            _editing = null;
            TransactionFormHeader.Text = "Add Income / Expense";
            SaveTransactionButton.Content = "Add Entry";
            CancelEditButton.Visibility = Visibility.Collapsed;
            ClearTransactionForm(keepType: false);
            TransactionDatePicker.SelectedDate = DateTime.Today;
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e) => ExitEditMode();

        private void EditTransactionButton_Click(object sender, RoutedEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is Transaction t) EnterEditMode(t);
            else Info("Select a transaction in the table first.");
        }

        private void TransactionsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only react to double-clicks on a row, not on the header or empty space.
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(TransactionsGrid, d) is DataGridRow row
                && row.Item is Transaction t)
                EnterEditMode(t);
        }

        // ===================== ADD PROGRAMMED (RECURRING) EXPENSE =====================
        private async void AddRecurringButton_Click(object sender, RoutedEventArgs e) => await AddRecurringAsync();

        private void RecurringForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = AddRecurringAsync(); }
        }

        private async System.Threading.Tasks.Task AddRecurringAsync()
        {
            if (_saving) return;
            _saving = true;
            try { await AddRecurringCoreAsync(); }
            finally { _saving = false; }
        }

        private async System.Threading.Tasks.Task AddRecurringCoreAsync()
        {
            var name = RecurringNameTextBox.Text?.Trim() ?? "";
            var category = (RecurringCategoryComboBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
            {
                Warn("Please enter a name and category.", "Missing Information");
                return;
            }

            if (!Money.TryParse(RecurringAmountTextBox.Text, out var amount) || amount <= 0)
            {
                Warn("Please enter a valid positive amount, e.g. 12,50 or 12.50.", "Invalid Amount");
                RecurringAmountTextBox.Focus();
                return;
            }

            var recurring = new RecurringExpense
            {
                Name = name,
                Category = category,
                Amount = amount,
                Frequency = (RecurringFrequency)Math.Max(0, FrequencyComboBox.SelectedIndex),
                StartDate = (RecurringStartDatePicker.SelectedDate ?? DateTime.Today).Date,
                IsActive = true
            };

            _context.RecurringExpenses.Add(recurring);
            if (!await TrySaveAsync()) return;

            InsertSorted(recurring);
            RefreshCategorySuggestions();
            RefreshDashboard();

            RecurringNameTextBox.Clear();
            RecurringAmountTextBox.Clear();
            RecurringCategoryComboBox.Text = "";
            RecurringStartDatePicker.SelectedDate = DateTime.Today;
            RecurringNameTextBox.Focus();
        }

        private void InsertSorted(RecurringExpense r)
        {
            int i = 0;
            while (i < RecurringExpenses.Count &&
                   string.Compare(RecurringExpenses[i].Name, r.Name, StringComparison.CurrentCultureIgnoreCase) <= 0) i++;
            RecurringExpenses.Insert(i, r);
        }

        private async void ToggleRecurringButton_Click(object sender, RoutedEventArgs e)
        {
            if (RecurringGrid.SelectedItem is not RecurringExpense r)
            {
                Info("Select a programmed expense in the table first.");
                return;
            }

            r.IsActive = !r.IsActive;
            if (!await TrySaveAsync()) { r.IsActive = !r.IsActive; return; }

            RecurringGrid.Items.Refresh();
            RecurringGrid.SelectedItem = r;
            UpdateToggleButton();
            RefreshDashboard();
        }

        private void RecurringGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateToggleButton();

        private void UpdateToggleButton() =>
            ToggleRecurringButton.Content = RecurringGrid.SelectedItem is RecurringExpense { IsActive: false } ? "Resume" : "Pause";

        // ===================== DELETE =====================
        private async void DeleteTransactionButton_Click(object sender, RoutedEventArgs e) => await DeleteSelectedTransactionAsync();

        private async void TransactionsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { e.Handled = true; await DeleteSelectedTransactionAsync(); }
            else if (e.Key == Key.Enter && TransactionsGrid.SelectedItem is Transaction t) { e.Handled = true; EnterEditMode(t); }
        }

        private async System.Threading.Tasks.Task DeleteSelectedTransactionAsync()
        {
            if (TransactionsGrid.SelectedItem is not Transaction t)
            {
                Info("Select a transaction in the table first.");
                return;
            }

            var answer = MessageBox.Show(
                $"Delete this entry?\n\n{t.Date:dd-MM-yyyy}  ·  {t.Category}  ·  {Money.Format(t.Amount)}",
                "Delete Entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _context.Transactions.Remove(t);
            if (!await TrySaveAsync()) return;

            if (_editing == t) ExitEditMode();
            _allTransactions.Remove(t);
            RefreshCategorySuggestions();
            RefreshDashboard();
        }

        private async void RecurringGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { e.Handled = true; await DeleteSelectedRecurringAsync(); }
        }

        private async void DeleteRecurringButton_Click(object sender, RoutedEventArgs e) => await DeleteSelectedRecurringAsync();

        private async System.Threading.Tasks.Task DeleteSelectedRecurringAsync()
        {
            if (RecurringGrid.SelectedItem is not RecurringExpense r)
            {
                Info("Select a programmed expense in the table first.");
                return;
            }

            var answer = MessageBox.Show(
                $"Remove \"{r.Name}\" permanently?\n\nIt will disappear from every month, including past ones. " +
                "To stop it from now on but keep it in past months' totals, use Pause instead.",
                "Remove Programmed Expense", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _context.RecurringExpenses.Remove(r);
            if (!await TrySaveAsync()) return;

            RecurringExpenses.Remove(r);
            RefreshCategorySuggestions();
            RefreshDashboard();
        }

        // ===================== BALANCE SHEET + CHART REFRESH =====================
        private void RefreshDashboard()
        {
            PeriodLabel.Text = _period.Label;
            PrevPeriodButton.IsEnabled = NextPeriodButton.IsEnabled = !_period.IsAllTime;

            // Grid: entries in the period, newest first.
            var selected = TransactionsGrid.SelectedItem;
            VisibleTransactions.Clear();
            foreach (var t in _allTransactions.Where(t => _period.Contains(t.Date))
                         .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id))
                VisibleTransactions.Add(t);
            if (selected != null && VisibleTransactions.Contains(selected)) TransactionsGrid.SelectedItem = selected;

            HistoryEmptyText.Visibility = VisibleTransactions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryCountText.Text = VisibleTransactions.Count == 1 ? "1 entry" : $"{VisibleTransactions.Count} entries";

            var s = BudgetCalculator.Summarize(_allTransactions, RecurringExpenses, _period, DateTime.Today);

            TotalIncomeText.Text = Money.Format(s.Income);
            TotalExpensesText.Text = Money.Format(s.TotalExpenses);
            NetBalanceText.Text = Money.Format(s.Net);
            NetBalanceText.Foreground = new SolidColorBrush(s.Net >= 0
                ? Color.FromRgb(0x43, 0x61, 0xEE)   // blue when positive
                : Color.FromRgb(0xD6, 0x45, 0x45)); // red when overspent

            int incomeCount = _allTransactions.Count(t => _period.Contains(t.Date) && t.Type == TransactionType.Income);
            IncomeSubText.Text = incomeCount == 0 ? "No income entered for this period" : $"{incomeCount} income entr{(incomeCount == 1 ? "y" : "ies")}";
            ExpensesSubText.Text = $"{Money.Format(s.OneOffExpenses)} one-off  +  {Money.Format(s.RecurringExpenses)} programmed";
            NetSubText.Text = s.SavingsRate is decimal rate
                ? (rate >= 0 ? $"You kept {rate.ToString("P0", Money.Culture)} of your income"
                             : $"Overspent by {(-rate).ToString("P0", Money.Culture)} of your income")
                : " ";

            var activeMonthly = RecurringExpenses.Where(r => r.IsActive).Sum(r => r.MonthlyAmount);
            RecurringTotalText.Text = $"{Money.Format(activeMonthly)} per month active";

            UpdatePieChart(s);
        }

        /// <summary>
        /// Normally each slice is a category's share of income, plus a "Left over" slice, so the
        /// whole pie is the income. When spending exceeds income (or there is none), the pie shows
        /// how the spending splits instead, and the header says so.
        /// </summary>
        private void UpdatePieChart(BudgetSummary s)
        {
            decimal whole = s.SlicesAreShareOfIncome ? s.Income : s.TotalExpenses;

            if (s.Slices.Count == 0 || whole <= 0)
            {
                ExpensePieChart.Series = Array.Empty<ISeries>();
                ChartEmptyText.Visibility = Visibility.Visible;
                ChartHeader.Text = "Where the income goes";
                ChartSubHeader.Text = " ";
                return;
            }

            ChartEmptyText.Visibility = Visibility.Collapsed;
            if (s.SlicesAreShareOfIncome)
            {
                ChartHeader.Text = "Where the income goes";
                ChartSubHeader.Text = $"Share of {Money.Format(s.Income)} income";
            }
            else
            {
                ChartHeader.Text = "How the spending splits";
                ChartSubHeader.Text = s.Income > 0
                    ? $"Spending is {Money.Format(s.TotalExpenses - s.Income)} more than income"
                    : "No income entered for this period";
            }

            int colorIndex = 0;
            var series = s.Slices.Select(slice =>
            {
                var color = slice.IsLeftOver ? LeftOverColor : SliceColors[colorIndex++ % SliceColors.Length];
                var share = slice.Amount / whole;
                return new PieSeries<decimal>
                {
                    Values = new[] { slice.Amount },
                    Name = slice.Label,
                    ToolTipLabelFormatter = _ => $"{Money.Format(slice.Amount)} ({share.ToString("P0", Money.Culture)})",
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsSize = 12,
                    // Tiny slices get no label so labels don't pile on top of each other.
                    DataLabelsFormatter = _ => share >= 0.05m ? share.ToString("P0", Money.Culture) : "",
                    Fill = new SolidColorPaint(color)
                };
            }).ToArray<ISeries>();

            ExpensePieChart.Series = series;
        }

        private void RefreshCategorySuggestions()
        {
            var list = BudgetCalculator.CategorySuggestions(DefaultCategories, _allTransactions, RecurringExpenses);
            // Setting ItemsSource on an editable ComboBox keeps whatever text the user has typed.
            var typed1 = CategoryComboBox.Text;
            var typed2 = RecurringCategoryComboBox.Text;
            CategoryComboBox.ItemsSource = list;
            RecurringCategoryComboBox.ItemsSource = list;
            CategoryComboBox.Text = typed1;
            RecurringCategoryComboBox.Text = typed2;
        }

        // ===================== CSV EXPORT =====================
        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var namePart = _period.Month is DateTime m ? m.ToString("yyyy-MM") : "All";
            var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"Budget_{namePart}_{DateTime.Now:yyyy-MM-dd}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var csv = CsvExportService.Build(_allTransactions, RecurringExpenses, _period, DateTime.Today, CultureInfo.CurrentCulture);
                CsvExportService.Export(dialog.FileName, csv);
                MessageBox.Show($"Exported {_period.Label.ToLower(Money.Culture)} to\n{dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not export file: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===================== HELPERS =====================
        /// <summary>Saves pending changes; on failure shows the error, undoes pending changes and returns false.</summary>
        private async System.Threading.Tasks.Task<bool> TrySaveAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                {
                    switch (entry.State)
                    {
                        case EntityState.Added: entry.State = EntityState.Detached; break;
                        case EntityState.Modified:
                        case EntityState.Deleted: entry.Reload(); break;
                    }
                }
                MessageBox.Show($"Could not save your change:\n{ex.GetBaseException().Message}", "Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static void Warn(string text, string title) =>
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        private static void Info(string text) =>
            MessageBox.Show(text, "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);

        protected override void OnClosed(EventArgs e)
        {
            // Everything is saved on each change; this just releases the SQLite connection cleanly.
            _context.Dispose();
            base.OnClosed(e);
        }
    }
}
