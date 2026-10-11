using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Pages
{
    public partial class ProgrammedPage : UserControl, IPage
    {
        private RecurringItem? _editing;
        private bool _saving;

        /// <summary>A programmed item as shown in the table.</summary>
        private sealed class Row
        {
            public Row(RecurringItem r)
            {
                Source = r;
                Name = r.Name;
                Category = r.Category;
                TypeText = r.IsIncome ? "Income" : "Cost";
                AmountText = $"{Money.Format(r.Amount)} / {r.Frequency switch { RecurringFrequency.Weekly => "week", RecurringFrequency.Yearly => "year", _ => "month" }}";
                MonthlyText = (r.IsIncome ? "+" : "−") + Money.Format(Math.Round(r.MonthlyAmount, 2));
                Since = r.StartDate.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                StatusText = r.IsActive ? "Active" : "Paused";
            }
            public RecurringItem Source { get; }
            public string Name { get; }
            public string Category { get; }
            public string TypeText { get; }
            public string AmountText { get; }
            public string MonthlyText { get; }
            public string Since { get; }
            public string StatusText { get; }
        }

        public ProgrammedPage()
        {
            InitializeComponent();
            StartBox.SelectedDate = DateTime.Today;
        }

        public void OnShown(object? argument) => Refresh();

        private void Refresh()
        {
            var store = AppState.Store;
            var active = store.Recurring.Where(r => r.IsActive).ToList();
            decimal income = active.Where(r => r.IsIncome).Sum(r => r.MonthlyAmount);
            decimal costs = active.Where(r => !r.IsIncome).Sum(r => r.MonthlyAmount);
            IncomeText.Text = Money.Format(income);
            CostsText.Text = Money.Format(costs);
            LeftText.Text = Money.Format(income - costs);
            LeftText.Foreground = income - costs >= 0 ? Look.Accent : Look.Bad;
            LeftSub.Text = income > 0
                ? $"Fixed costs take {Look.Pct(costs / income)} of your programmed income"
                : "Add your income to see what's left for everything else";

            var selected = (ItemsGrid.SelectedItem as Row)?.Source;
            var rows = store.Recurring
                .OrderBy(r => r.IsIncome ? 0 : 1).ThenBy(r => !r.IsActive).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new Row(r)).ToList();
            ItemsGrid.ItemsSource = rows;
            ItemsGrid.SelectedItem = rows.FirstOrDefault(r => r.Source == selected);
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var typed = CategoryBox.Text;
            CategoryBox.ItemsSource = AppState.Categories();
            CategoryBox.Text = typed;
            UpdateToggle();
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; Save_Click(sender, e); }
            else if (e.Key == Key.Escape && _editing != null) { e.Handled = true; ExitEditMode(); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_saving) return;
            var name = NameBox.Text?.Trim() ?? "";
            var category = (CategoryBox.Text ?? "").Trim();
            if (name.Length == 0 || category.Length == 0)
            {
                Ui.Warn("Please enter a name and a category.", "Missing information");
                (name.Length == 0 ? (Control)NameBox : CategoryBox).Focus();
                return;
            }
            if (!Look.ReadAmount(AmountBox, out var amount)) return;

            var target = _editing ?? new RecurringItem { IsActive = true };
            var before = (target.Name, target.Category, target.Amount, target.Frequency, target.Type, target.StartDate);
            target.Name = name;
            target.Category = category;
            target.Amount = amount;
            target.Frequency = (RecurringFrequency)Math.Max(0, FrequencyBox.SelectedIndex);
            target.Type = TypeIncome.IsChecked == true ? TransactionType.Income : TransactionType.Expense;
            target.StartDate = (StartBox.SelectedDate ?? DateTime.Today).Date;

            _saving = true;
            try
            {
                if (_editing == null) await AppState.Store.AddRecurringAsync(target);
                else await AppState.Store.SaveEditsAsync();
            }
            catch (Exception ex)
            {
                if (_editing != null) (target.Name, target.Category, target.Amount, target.Frequency, target.Type, target.StartDate) = before;
                Ui.Error("Couldn't save.", ex);
                return;
            }
            finally { _saving = false; }

            AppState.Toast(_editing == null ? $"Added {target.Name}" : "Changes saved");
            ExitEditMode();
            Refresh();
            NameBox.Focus();
        }

        private void EnterEditMode(RecurringItem r)
        {
            _editing = r;
            TypeIncome.IsChecked = r.IsIncome;
            TypeExpense.IsChecked = !r.IsIncome;
            NameBox.Text = r.Name;
            CategoryBox.Text = r.Category;
            AmountBox.Text = Look.AmountForEditing(r.Amount);
            FrequencyBox.SelectedIndex = (int)r.Frequency;
            StartBox.SelectedDate = r.StartDate;
            FormHeader.Text = "Edit programmed item";
            SaveButton.Content = "Save changes";
            CancelButton.Visibility = Visibility.Visible;
            NameBox.Focus();
        }

        private void ExitEditMode()
        {
            _editing = null;
            NameBox.Clear();
            AmountBox.Clear();
            CategoryBox.Text = "";
            FrequencyBox.SelectedIndex = 1;
            StartBox.SelectedDate = DateTime.Today;
            TypeExpense.IsChecked = true;
            FormHeader.Text = "Add programmed item";
            SaveButton.Content = "Add";
            CancelButton.Visibility = Visibility.Collapsed;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => ExitEditMode();

        private RecurringItem? Selected => (ItemsGrid.SelectedItem as Row)?.Source;

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is RecurringItem r) EnterEditMode(r);
            else Ui.Info("Select an item in the table first.", "Nothing selected");
        }

        private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(ItemsGrid, d) is DataGridRow row && row.Item is Row r)
                EnterEditMode(r.Source);
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateToggle();

        private void UpdateToggle() => ToggleButton.Content = Selected is { IsActive: false } ? "Resume" : "Pause";

        private async void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not RecurringItem r) { Ui.Info("Select an item in the table first.", "Nothing selected"); return; }
            r.IsActive = !r.IsActive;
            try { await AppState.Store.SaveEditsAsync(); }
            catch (Exception ex) { r.IsActive = !r.IsActive; Ui.Error("Couldn't save.", ex); return; }
            Refresh();
            AppState.Toast(r.IsActive ? $"{r.Name} resumed" : $"{r.Name} paused");
        }

        private async void ItemsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { e.Handled = true; await RemoveAsync(); }
        }

        private async void Remove_Click(object sender, RoutedEventArgs e) => await RemoveAsync();

        private async System.Threading.Tasks.Task RemoveAsync()
        {
            if (Selected is not RecurringItem r) { Ui.Info("Select an item in the table first.", "Nothing selected"); return; }
            int linked = AppState.Store.Transactions.Count(t => t.RecurringId == r.Id);
            var text = $"Remove \"{r.Name}\" permanently?\n\nIt disappears from every month, including past ones. " +
                       "To stop it from now on but keep past months correct, use Pause instead.";
            if (linked > 0) text += $"\n\n{linked} imported payment(s) linked to it will count as normal entries again.";
            if (!Ui.Confirm(text, "Remove programmed item")) return;
            try { await AppState.Store.DeleteRecurringAsync(r); }
            catch (Exception ex) { Ui.Error("Couldn't remove.", ex); return; }
            if (_editing == r) ExitEditMode();
            Refresh();
        }
    }
}
