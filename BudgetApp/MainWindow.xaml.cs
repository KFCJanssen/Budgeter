using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace BudgetApp
{
    public partial class MainWindow : Window
    {
        // EF Core context - one instance lives for the lifetime of the window.
        private readonly BudgetContext _context = new();

        // In-memory collections bound to the two DataGrids. Kept in sync with the database
        // on every add/delete so the UI never has to re-query from scratch.
        public ObservableCollection<Transaction> Transactions { get; } = new();
        public ObservableCollection<RecurringExpense> RecurringExpenses { get; } = new();

        // Suggested categories shown in the editable ComboBoxes. The user can still type
        // a brand new category - this list is just a convenience, not a restriction.
        public string[] CategorySuggestions { get; } =
        {
            "Salary", "Fixed Expenses", "Discretionary", "Savings", "Utilities", "Other"
        };

        // Colors cycled through for each pie-chart slice / category.
        private static readonly SKColor[] SliceColors =
        {
            SKColor.Parse("#FF6B6B"), SKColor.Parse("#4361EE"), SKColor.Parse("#2EC4B6"),
            SKColor.Parse("#FFB84C"), SKColor.Parse("#9B5DE5"), SKColor.Parse("#00BBF9"),
            SKColor.Parse("#F15BB5"), SKColor.Parse("#5C7AEA")
        };

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this; // enables the CategorySuggestions bindings in XAML

            TransactionsGrid.ItemsSource = Transactions;
            RecurringGrid.ItemsSource = RecurringExpenses;
            TransactionDatePicker.SelectedDate = DateTime.Now;

            Loaded += MainWindow_Loaded;
        }

        // ===================== STARTUP: LOAD FROM DATABASE =====================
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Creates the SQLite file + tables on first run; does nothing if they already exist.
            await _context.Database.EnsureCreatedAsync();

            var savedTransactions = await _context.Transactions.OrderByDescending(t => t.Date).ToListAsync();
            foreach (var t in savedTransactions) Transactions.Add(t);

            var savedRecurring = await _context.RecurringExpenses.OrderBy(r => r.Name).ToListAsync();
            foreach (var r in savedRecurring) RecurringExpenses.Add(r);

            RefreshDashboard();
        }

        // ===================== ADD TRANSACTION =====================
        private async void AddTransactionButton_Click(object sender, RoutedEventArgs e)
        {
            var category = (CategoryComboBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(category))
            {
                MessageBox.Show("Please enter a category.", "Missing Category", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(AmountTextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                MessageBox.Show("Please enter a valid positive amount.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var type = TypeComboBox.SelectedIndex == 0 ? TransactionType.Income : TransactionType.Expense;

            var transaction = new Transaction
            {
                Date = TransactionDatePicker.SelectedDate ?? DateTime.Now,
                Type = type,
                Category = category,
                Amount = amount,
                Description = DescriptionTextBox.Text?.Trim() ?? ""
            };

            // Persist first, then update the UI collection - this way Id is populated by SQLite.
            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            Transactions.Insert(0, transaction);
            RefreshDashboard();
            ClearTransactionForm();
        }

        private void ClearTransactionForm()
        {
            AmountTextBox.Clear();
            DescriptionTextBox.Clear();
            CategoryComboBox.Text = "";
            TransactionDatePicker.SelectedDate = DateTime.Now;
        }

        // ===================== ADD PROGRAMMED (RECURRING) EXPENSE =====================
        private async void AddRecurringButton_Click(object sender, RoutedEventArgs e)
        {
            var name = RecurringNameTextBox.Text?.Trim();
            var category = (RecurringCategoryComboBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
            {
                MessageBox.Show("Please enter a name and category.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(RecurringAmountTextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                MessageBox.Show("Please enter a valid positive amount.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var frequency = (RecurringFrequency)FrequencyComboBox.SelectedIndex;

            var recurring = new RecurringExpense
            {
                Name = name,
                Category = category,
                Amount = amount,
                Frequency = frequency,
                StartDate = DateTime.Now,
                IsActive = true
            };

            _context.RecurringExpenses.Add(recurring);
            await _context.SaveChangesAsync();

            RecurringExpenses.Add(recurring);
            RefreshDashboard();

            RecurringNameTextBox.Clear();
            RecurringAmountTextBox.Clear();
            RecurringCategoryComboBox.Text = "";
        }

        // ===================== DELETE HANDLERS =====================
        private Transaction? _selectedTransaction;
        private RecurringExpense? _selectedRecurring;

        private void TransactionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _selectedTransaction = TransactionsGrid.SelectedItem as Transaction;

        private void RecurringGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _selectedRecurring = RecurringGrid.SelectedItem as RecurringExpense;

        private async void DeleteTransactionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTransaction == null)
            {
                MessageBox.Show("Select a transaction in the table first.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _context.Transactions.Remove(_selectedTransaction);
            await _context.SaveChangesAsync();

            Transactions.Remove(_selectedTransaction);
            _selectedTransaction = null;
            RefreshDashboard();
        }

        private async void DeleteRecurringButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecurring == null)
            {
                MessageBox.Show("Select a programmed expense in the table first.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _context.RecurringExpenses.Remove(_selectedRecurring);
            await _context.SaveChangesAsync();

            RecurringExpenses.Remove(_selectedRecurring);
            _selectedRecurring = null;
            RefreshDashboard();
        }

        // ===================== BALANCE SHEET + CHART REFRESH =====================
        private void RefreshDashboard()
        {
            decimal totalIncome = Transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);

            decimal oneOffExpenses = Transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            decimal recurringExpenseTotal = RecurringExpenses.Where(r => r.IsActive).Sum(r => r.MonthlyAmount);
            decimal totalExpenses = oneOffExpenses + recurringExpenseTotal;

            decimal netBalance = totalIncome - totalExpenses;

            TotalIncomeText.Text = totalIncome.ToString("C2");
            TotalExpensesText.Text = totalExpenses.ToString("C2");
            NetBalanceText.Text = netBalance.ToString("C2");
            NetBalanceText.Foreground = new SolidColorBrush(netBalance >= 0
                ? Color.FromRgb(0x43, 0x61, 0xEE)   // blue when positive
                : Color.FromRgb(0xD6, 0x45, 0x45)); // red when overspent

            UpdatePieChart(totalIncome);
        }

        /// <summary>
        /// Groups all expenses (one-off + recurring) by category and shows each as a slice
        /// of the total income - so the chart answers "what % of what I earn goes to X?".
        /// </summary>
        private void UpdatePieChart(decimal totalIncome)
        {
            var byCategory = Transactions
                .Where(t => t.Type == TransactionType.Expense)
                .GroupBy(t => t.Category)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

            foreach (var r in RecurringExpenses.Where(r => r.IsActive))
            {
                byCategory[r.Category] = byCategory.GetValueOrDefault(r.Category) + r.MonthlyAmount;
            }

            if (byCategory.Count == 0 || totalIncome <= 0)
            {
                ExpensePieChart.Series = Array.Empty<ISeries>();
                return;
            }

            var series = byCategory.Select((kv, index) => new PieSeries<decimal>
            {
                Values = new[] { kv.Value },
                Name = kv.Key,
                // Show the amount and its % of total income directly on the slice.
                ToolTipLabelFormatter = point => $"{kv.Key}: {kv.Value:C2} ({kv.Value / totalIncome:P0} of income)",
                DataLabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SKColor.Parse("#22243B")),
                DataLabelsFormatter = point => $"{kv.Value / totalIncome:P0}",
                Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SliceColors[index % SliceColors.Length])
            }).ToArray();

            ExpensePieChart.Series = series;
        }

        // ===================== CSV EXPORT =====================
        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"Budget_Export_{DateTime.Now:yyyy-MM-dd}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            decimal totalIncome = Transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            decimal totalExpenses = Transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                                     + RecurringExpenses.Where(r => r.IsActive).Sum(r => r.MonthlyAmount);
            decimal netBalance = totalIncome - totalExpenses;

            try
            {
                CsvExportService.Export(dialog.FileName, Transactions, RecurringExpenses, totalIncome, totalExpenses, netBalance);
                MessageBox.Show("Export complete.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not export file: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // All changes are already saved on each add/delete, but this is a safety net
            // in case anything is still pending, and it releases the SQLite connection cleanly.
            _context.SaveChanges();
            _context.Dispose();
            base.OnClosed(e);
        }
    }
}
