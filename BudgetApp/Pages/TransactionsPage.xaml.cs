using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Pages
{
    public partial class TransactionsPage : UserControl, IPage
    {
        /// <summary>Navigation argument: show everything that still has no category.</summary>
        public const string ShowUncategorized = "uncategorized";

        private const string AllCategories = "All categories";
        private Transaction? _editing;
        private bool _saving;
        private bool _loading;

        private sealed record RecurringChoice(int? Id, string Name);

        public TransactionsPage()
        {
            InitializeComponent();
            PeriodBar.PeriodChanged += Refresh;
            DateBox.SelectedDate = DateTime.Today;
        }

        public void OnShown(object? argument)
        {
            if (argument is Transaction t)
            {
                AppState.Period = BudgetPeriod.ForMonth(t.Date);
                ClearFilters();
                PeriodBar.Refresh();
                Refresh();
                TxGrid.SelectedItem = t;
                TxGrid.ScrollIntoView(t);
                EnterEditMode(t);
                return;
            }
            if (argument as string == ShowUncategorized)
            {
                AppState.Period = BudgetPeriod.AllTime;
                ClearFilters();
                UncategorizedOnly.IsChecked = true;
            }
            PeriodBar.Refresh();
            Refresh();
        }

        private void ClearFilters()
        {
            _loading = true;
            SearchBox.Text = "";
            TypeFilter.SelectedIndex = 0;
            CategoryFilter.SelectedIndex = 0;
            UncategorizedOnly.IsChecked = false;
            _loading = false;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (!_loading && IsLoaded) Refresh();
        }

        private void Refresh()
        {
            var store = AppState.Store;
            var categories = AppState.Categories();

            _loading = true;
            var currentFilter = CategoryFilter.SelectedItem as string;
            CategoryFilter.ItemsSource = new[] { AllCategories }.Concat(categories).ToList();
            CategoryFilter.SelectedItem = currentFilter != null && categories.Contains(currentFilter) ? currentFilter : AllCategories;
            var typedCategory = CategoryBox.Text;
            CategoryBox.ItemsSource = categories;
            CategoryBox.Text = typedCategory;
            var typedBulk = BulkCategory.Text;
            BulkCategory.ItemsSource = categories;
            BulkCategory.Text = typedBulk;
            _loading = false;
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            var period = PeriodBar.Period;
            IEnumerable<Transaction> rows = store.Transactions.Where(t => period.Contains(t.Date));

            var search = (SearchBox.Text ?? "").Trim();
            if (search.Length > 0)
            {
                bool isAmount = Money.TryParse(search, out var amount);
                rows = rows.Where(t =>
                    (t.Payee ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    t.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    t.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (isAmount && t.Amount == amount));
            }
            if (CategoryFilter.SelectedItem is string cat && cat != AllCategories)
                rows = rows.Where(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase));
            rows = TypeFilter.SelectedIndex switch
            {
                1 => rows.Where(t => t.Type == TransactionType.Income),
                2 => rows.Where(t => t.Type == TransactionType.Expense),
                3 => rows.Where(t => t.Type == TransactionType.Transfer),
                _ => rows
            };
            if (UncategorizedOnly.IsChecked == true)
                rows = rows.Where(t => t.Type != TransactionType.Transfer &&
                                       (string.Equals(t.Category, BudgetCalculator.Uncategorized, StringComparison.OrdinalIgnoreCase) || t.Category.Trim().Length == 0));

            var list = rows.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToList();
            var selected = TxGrid.SelectedItems.Cast<Transaction>().ToList();
            TxGrid.ItemsSource = list;
            foreach (var s in selected.Where(list.Contains)) TxGrid.SelectedItems.Add(s);

            EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = store.Transactions.Count == 0
                ? "No transactions yet. Add one on the right, or import a file from your bank (Import from bank)."
                : "Nothing matches these filters in this period. Try All time or clear the search.";

            var counted = list.Where(t => t.CountsInTotals).ToList();
            TotalsText.Text = $"{list.Count} {(list.Count == 1 ? "entry" : "entries")}  ·  in {Money.Format(counted.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount))}" +
                              $"  ·  out {Money.Format(counted.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount))}";

            RefreshRecurringChoices();
        }

        private void RefreshRecurringChoices()
        {
            var type = SelectedType;
            var keep = (RecurringBox.SelectedItem as RecurringChoice)?.Id;
            var choices = new List<RecurringChoice> { new(null, "No, count it normally") };
            if (type != TransactionType.Transfer)
                choices.AddRange(AppState.Store.Recurring.Where(r => r.Type == type).Select(r => new RecurringChoice(r.Id, $"{r.Name} ({Money.Format(r.Amount)})")));
            RecurringBox.ItemsSource = choices;
            RecurringBox.SelectedItem = choices.FirstOrDefault(c => c.Id == keep) ?? choices[0];
        }

        private TransactionType SelectedType =>
            TypeIncome.IsChecked == true ? TransactionType.Income :
            TypeTransfer.IsChecked == true ? TransactionType.Transfer : TransactionType.Expense;

        private void Type_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) RefreshRecurringChoices();
        }

        // ------------------------------------------------------------------ add / edit

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; Save_Click(sender, e); }
            else if (e.Key == Key.Escape && _editing != null) { e.Handled = true; ExitEditMode(); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_saving) return;
            if (!Look.ReadAmount(AmountBox, out var amount)) return;
            var type = SelectedType;
            var category = (CategoryBox.Text ?? "").Trim();
            if (category.Length == 0)
            {
                if (type == TransactionType.Transfer) category = "Transfer";
                else
                {
                    Ui.Warn("Please choose or type a category.", "Missing category");
                    CategoryBox.Focus();
                    return;
                }
            }

            var target = _editing ?? new Transaction();
            var before = (target.Date, target.Type, target.Category, target.Amount, target.Payee, target.Description, target.RecurringId);
            target.Date = (DateBox.SelectedDate ?? DateTime.Today).Date;
            target.Type = type;
            target.Category = category;
            target.Amount = amount;
            target.Payee = string.IsNullOrWhiteSpace(PayeeBox.Text) ? null : PayeeBox.Text.Trim();
            target.Description = DescriptionBox.Text?.Trim() ?? "";
            target.RecurringId = type == TransactionType.Transfer ? null : (RecurringBox.SelectedItem as RecurringChoice)?.Id;

            _saving = true;
            try
            {
                if (_editing == null) await AppState.Store.AddTransactionAsync(target);
                else await AppState.Store.SaveEditsAsync();
            }
            catch (Exception ex)
            {
                if (_editing != null)
                    (target.Date, target.Type, target.Category, target.Amount, target.Payee, target.Description, target.RecurringId) = before;
                Ui.Error("The entry couldn't be saved.", ex);
                return;
            }
            finally { _saving = false; }

            bool wasNew = _editing == null;
            if (!PeriodBar.Period.Contains(target.Date))
            {
                AppState.Period = BudgetPeriod.ForMonth(target.Date);
                PeriodBar.Refresh();
            }
            if (wasNew)
            {
                AmountBox.Clear();
                DescriptionBox.Clear();
                PayeeBox.Clear();
                CategoryBox.Text = "";
                AppState.Toast($"Added {Look.Signed(target)} · {target.Category}");
            }
            else
            {
                ExitEditMode();
                AppState.Toast("Changes saved");
            }
            Refresh();
            TxGrid.SelectedItem = target;
            TxGrid.ScrollIntoView(target);
            AmountBox.Focus();
        }

        private void EnterEditMode(Transaction t)
        {
            _editing = t;
            TypeIncome.IsChecked = t.Type == TransactionType.Income;
            TypeExpense.IsChecked = t.Type == TransactionType.Expense;
            TypeTransfer.IsChecked = t.Type == TransactionType.Transfer;
            AmountBox.Text = Look.AmountForEditing(t.Amount);
            DateBox.SelectedDate = t.Date;
            CategoryBox.Text = t.Category;
            PayeeBox.Text = t.Payee ?? "";
            DescriptionBox.Text = t.Description;
            RefreshRecurringChoices();
            RecurringBox.SelectedItem = (RecurringBox.ItemsSource as List<RecurringChoice>)?.FirstOrDefault(c => c.Id == t.RecurringId);

            FormHeader.Text = "Edit entry";
            SaveButton.Content = "Save changes";
            CancelButton.Visibility = Visibility.Visible;
            ImportedNote.Visibility = t.IsImported ? Visibility.Visible : Visibility.Collapsed;
            ImportedNote.Text = "This entry came from a bank import. Changing it here won't change your bank, and it won't be imported again.";
            AmountBox.Focus();
            AmountBox.SelectAll();
        }

        private void ExitEditMode()
        {
            _editing = null;
            FormHeader.Text = "Add an entry";
            SaveButton.Content = "Add entry";
            CancelButton.Visibility = Visibility.Collapsed;
            ImportedNote.Visibility = Visibility.Collapsed;
            AmountBox.Clear();
            DescriptionBox.Clear();
            PayeeBox.Clear();
            CategoryBox.Text = "";
            TypeExpense.IsChecked = true;
            DateBox.SelectedDate = DateTime.Today;
            RefreshRecurringChoices();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => ExitEditMode();

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (TxGrid.SelectedItem is Transaction t) EnterEditMode(t);
            else Ui.Info("Select a transaction in the table first.", "Nothing selected");
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(TxGrid, d) is DataGridRow row && row.Item is Transaction t)
                EnterEditMode(t);
        }

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private async void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { e.Handled = true; await DeleteSelectedAsync(); }
            else if (e.Key == Key.Enter && TxGrid.SelectedItem is Transaction t) { e.Handled = true; EnterEditMode(t); }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

        private async System.Threading.Tasks.Task DeleteSelectedAsync()
        {
            var selected = TxGrid.SelectedItems.Cast<Transaction>().ToList();
            if (selected.Count == 0)
            {
                Ui.Info("Select one or more transactions first (Ctrl+click or Shift+click to select several).", "Nothing selected");
                return;
            }
            var text = selected.Count == 1
                ? $"Delete this entry?\n\n{selected[0].Date:dd-MM-yyyy}  ·  {selected[0].Category}  ·  {Money.Format(selected[0].Amount)}"
                : $"Delete these {selected.Count} entries?";
            if (selected.Any(t => t.IsImported))
                text += "\n\nImported entries won't come back if you import the same bank file again. To bring them back, undo the whole import on the Import page instead.";
            if (!Ui.Confirm(text, "Delete")) return;

            try
            {
                await AppState.Store.DeleteTransactionsAsync(selected);
            }
            catch (Exception ex)
            {
                Ui.Error("Couldn't delete.", ex);
                return;
            }
            if (_editing != null && selected.Contains(_editing)) ExitEditMode();
            Refresh();
            AppState.Toast(selected.Count == 1 ? "Entry deleted" : $"{selected.Count} entries deleted");
        }

        private async void BulkApply_Click(object sender, RoutedEventArgs e)
        {
            var selected = TxGrid.SelectedItems.Cast<Transaction>().ToList();
            var category = (BulkCategory.Text ?? "").Trim();
            if (selected.Count == 0) { Ui.Info("Select the transactions to change first (Ctrl+click or Shift+click).", "Nothing selected"); return; }
            if (category.Length == 0) { Ui.Warn("Choose or type the new category.", "Missing category"); return; }

            var before = selected.ToDictionary(t => t, t => t.Category);
            foreach (var t in selected) t.Category = category;
            try
            {
                await AppState.Store.SaveEditsAsync();
            }
            catch (Exception ex)
            {
                foreach (var kv in before) kv.Key.Category = kv.Value;
                Ui.Error("Couldn't change the category.", ex);
                return;
            }
            Refresh();
            AppState.Toast($"{selected.Count} {(selected.Count == 1 ? "entry" : "entries")} moved to {category}", "Undo", async () =>
            {
                foreach (var kv in before) kv.Key.Category = kv.Value;
                try { await AppState.Store.SaveEditsAsync(); } catch (Exception ex) { Ui.Error("Couldn't undo.", ex); }
                Refresh();
            });
        }

        private void Export_Click(object sender, RoutedEventArgs e) => DashboardPage.ExportCsv(PeriodBar.Period);
    }
}
