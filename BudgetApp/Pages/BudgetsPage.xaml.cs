using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BudgetApp.Controls;
using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Pages
{
    public partial class BudgetsPage : UserControl, IPage
    {
        private Budget? _editingBudget;
        private Goal? _editingGoal;

        /// <summary>A goal card.</summary>
        private sealed class GoalView
        {
            public GoalView(Goal g, decimal avgLeftOver)
            {
                Source = g;
                Name = g.Name;
                AmountText = $"{Money.Format(g.SavedAmount)} of {Money.Format(g.TargetAmount)}";
                var ratio = g.TargetAmount > 0 ? Math.Min(1m, g.SavedAmount / g.TargetAmount) : 0m;
                Bar = (double)ratio;
                Brush = ratio >= 1 ? Look.Good : Look.Accent;
                var remaining = Math.Max(0, g.TargetAmount - g.SavedAmount);
                if (remaining == 0) PlanText = "Reached - well done!";
                else if (g.TargetDate is DateTime d)
                {
                    int months = Math.Max(1, (d.Year - DateTime.Today.Year) * 12 + d.Month - DateTime.Today.Month);
                    var perMonth = Math.Ceiling(remaining / months);
                    PlanText = d < DateTime.Today
                        ? $"Target date passed. {Money.Format(remaining)} to go."
                        : $"{Money.Format(remaining)} to go: save {Money.Format(perMonth)} a month to make it by {d.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}.";
                }
                else if (avgLeftOver > 0)
                {
                    int months = (int)Math.Ceiling(remaining / avgLeftOver);
                    PlanText = $"{Money.Format(remaining)} to go. At your average left-over of {Money.Format(avgLeftOver)}/month: about {months} month{(months == 1 ? "" : "s")}.";
                }
                else PlanText = $"{Money.Format(remaining)} to go. Add a target date to see how much to save per month.";
            }

            public Goal Source { get; }
            public string Name { get; }
            public string AmountText { get; }
            public string PlanText { get; }
            public double Bar { get; }
            public Brush Brush { get; }
        }

        public BudgetsPage()
        {
            InitializeComponent();
            PeriodBar.AllowAllTime = false;
            PeriodBar.PeriodChanged += Refresh;
        }

        public void OnShown(object? argument)
        {
            PeriodBar.Refresh();
            Refresh();
        }

        private void Refresh()
        {
            var store = AppState.Store;
            var month = PeriodBar.Period.Month ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var statuses = BudgetCalculator.BudgetStatuses(store.Budgets, store.Transactions, store.Recurring, month, DateTime.Today);
            var views = statuses.Select(s => new BudgetView(s, store.Budgets.FirstOrDefault(b => string.Equals(b.Category.Trim(), s.Category, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            BudgetList.ItemsSource = views;
            NoBudgets.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BudgetsHeader.Text = $"Budgets in {month.ToString("MMMM", CultureInfo.InvariantCulture)}";

            var summary = BudgetCalculator.Summarize(store.Transactions, store.Recurring, Services.BudgetPeriod.ForMonth(month), DateTime.Today);
            decimal budgeted = store.Budgets.Sum(b => b.MonthlyLimit);
            BudgetSummary.Text = views.Count == 0 ? "" :
                $"{Money.Format(budgeted)} budgeted in total · {Money.Format(statuses.Sum(s => s.Spent))} spent in those categories" +
                (summary.Income > 0 ? $" · income this month {Money.Format(summary.Income)}" : "");

            var typed = BudgetCategory.Text;
            BudgetCategory.ItemsSource = AppState.Categories().Where(c => !string.Equals(c, BudgetCalculator.Uncategorized, StringComparison.OrdinalIgnoreCase)).ToList();
            BudgetCategory.Text = typed;

            var recent = BudgetCalculator.RecentMonthsWithData(store.Transactions, store.Recurring, DateTime.Today, 3);
            decimal avgLeft = recent.Count > 0 ? Math.Max(0, recent.Average(m => m.Net)) : 0;
            var goals = store.Goals.OrderBy(g => g.SavedAmount >= g.TargetAmount).ThenBy(g => g.TargetDate ?? DateTime.MaxValue)
                .Select(g => new GoalView(g, avgLeft)).ToList();
            GoalList.ItemsSource = goals;
            NoGoals.Visibility = goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ budgets

        private void BudgetForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; BudgetSave_Click(sender, e); }
        }

        private async void BudgetSave_Click(object sender, RoutedEventArgs e)
        {
            var category = (BudgetCategory.Text ?? "").Trim();
            if (category.Length == 0) { Ui.Warn("Choose the category this budget is for.", "Missing category"); return; }
            if (!Look.ReadAmount(BudgetLimit, out var limit)) return;

            var store = AppState.Store;
            var existing = _editingBudget ?? store.Budgets.FirstOrDefault(b => string.Equals(b.Category.Trim(), category, StringComparison.OrdinalIgnoreCase));
            try
            {
                if (existing != null)
                {
                    existing.Category = category;
                    existing.MonthlyLimit = limit;
                    await store.SaveEditsAsync();
                }
                else
                {
                    await store.AddAsync(new Budget { Category = category, MonthlyLimit = limit }, store.Budgets);
                }
            }
            catch (Exception ex) { Ui.Error("Couldn't save the budget.", ex); return; }

            ResetBudgetForm();
            Refresh();
            AppState.Toast($"Budget for {category}: {Money.Format(limit)} a month");
        }

        private void ResetBudgetForm()
        {
            _editingBudget = null;
            BudgetCategory.Text = "";
            BudgetLimit.Clear();
            BudgetSave.Content = "Add budget";
            BudgetCancel.Visibility = Visibility.Collapsed;
            BudgetCategory.IsEnabled = true;
        }

        private void BudgetCancel_Click(object sender, RoutedEventArgs e) => ResetBudgetForm();

        private void BudgetEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not BudgetView v || v.Source == null) return;
            _editingBudget = v.Source;
            BudgetCategory.Text = v.Source.Category;
            BudgetLimit.Text = Look.AmountForEditing(v.Source.MonthlyLimit);
            BudgetSave.Content = "Save";
            BudgetCancel.Visibility = Visibility.Visible;
            BudgetLimit.Focus();
            BudgetLimit.SelectAll();
        }

        private async void BudgetDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not BudgetView v || v.Source == null) return;
            if (!Ui.Confirm($"Remove the budget for {v.Category}?", "Remove budget")) return;
            try { await AppState.Store.DeleteAsync(v.Source, AppState.Store.Budgets); }
            catch (Exception ex) { Ui.Error("Couldn't remove the budget.", ex); return; }
            if (_editingBudget == v.Source) ResetBudgetForm();
            Refresh();
        }

        private async void Suggest_Click(object sender, RoutedEventArgs e)
        {
            var store = AppState.Store;
            var suggestions = BudgetCalculator.SuggestBudgets(store.Transactions, store.Recurring, DateTime.Today)
                .Where(kv => !string.Equals(kv.Key, BudgetCalculator.Uncategorized, StringComparison.OrdinalIgnoreCase))
                .Where(kv => !store.Budgets.Any(b => string.Equals(b.Category.Trim(), kv.Key, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(kv => kv.Value)
                .ToList();
            if (suggestions.Count == 0)
            {
                Ui.Info(store.Budgets.Count > 0
                    ? "Every category you spend on already has a budget."
                    : "There isn't a complete month of spending yet. Suggestions use your last 3 full months.", "Suggest budgets");
                return;
            }

            var list = string.Join("\n", suggestions.Select(kv => $"  {kv.Key}: {Money.Format(kv.Value)}"));
            if (!Ui.Confirm($"Based on your average spending of the last months (rounded up):\n\n{list}\n\nAdd these budgets? You can change each one afterwards.",
                    "Suggest budgets")) return;
            try
            {
                foreach (var kv in suggestions)
                    await store.AddAsync(new Budget { Category = kv.Key, MonthlyLimit = kv.Value }, store.Budgets);
            }
            catch (Exception ex) { Ui.Error("Couldn't add all budgets.", ex); }
            Refresh();
        }

        // ------------------------------------------------------------------ goals

        private void GoalForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; GoalSave_Click(sender, e); }
        }

        private async void GoalSave_Click(object sender, RoutedEventArgs e)
        {
            var name = GoalName.Text?.Trim() ?? "";
            if (name.Length == 0) { Ui.Warn("Give the goal a name, e.g. \"Laptop\" or \"Emergency buffer\".", "Missing name"); return; }
            if (!Look.ReadAmount(GoalTarget, out var target)) return;
            if (!Look.ReadAmount(GoalSaved, out var saved, allowZero: true)) return;

            var store = AppState.Store;
            try
            {
                if (_editingGoal != null)
                {
                    _editingGoal.Name = name;
                    _editingGoal.TargetAmount = target;
                    _editingGoal.SavedAmount = saved;
                    _editingGoal.TargetDate = GoalDate.SelectedDate?.Date;
                    await store.SaveEditsAsync();
                }
                else
                {
                    await store.AddAsync(new Goal { Name = name, TargetAmount = target, SavedAmount = saved, TargetDate = GoalDate.SelectedDate?.Date }, store.Goals);
                }
            }
            catch (Exception ex) { Ui.Error("Couldn't save the goal.", ex); return; }
            ResetGoalForm();
            Refresh();
        }

        private void ResetGoalForm()
        {
            _editingGoal = null;
            GoalName.Clear();
            GoalTarget.Clear();
            GoalSaved.Text = "0";
            GoalDate.SelectedDate = null;
            GoalFormHeader.Text = "New goal";
            GoalSave.Content = "Add goal";
            GoalCancel.Visibility = Visibility.Collapsed;
        }

        private void GoalCancel_Click(object sender, RoutedEventArgs e) => ResetGoalForm();

        private void GoalEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not GoalView v) return;
            _editingGoal = v.Source;
            GoalName.Text = v.Source.Name;
            GoalTarget.Text = Look.AmountForEditing(v.Source.TargetAmount);
            GoalSaved.Text = Look.AmountForEditing(v.Source.SavedAmount);
            GoalDate.SelectedDate = v.Source.TargetDate;
            GoalFormHeader.Text = $"Edit \"{v.Source.Name}\"";
            GoalSave.Content = "Save";
            GoalCancel.Visibility = Visibility.Visible;
            GoalName.Focus();
        }

        private async void GoalAddMoney_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not GoalView v) return;
            var text = InputDialog.Ask("Add money", $"How much did you put aside for \"{v.Name}\"? (Use a minus to take money out.)", "");
            if (text == null) return;
            if (!Money.TryParseSigned(text, out var amount) || amount == 0) { Ui.Warn("That's not an amount.", "Add money"); return; }
            var before = v.Source.SavedAmount;
            v.Source.SavedAmount = Math.Max(0, v.Source.SavedAmount + amount);
            try { await AppState.Store.SaveEditsAsync(); }
            catch (Exception ex) { v.Source.SavedAmount = before; Ui.Error("Couldn't save.", ex); return; }
            Refresh();
            if (v.Source.SavedAmount >= v.Source.TargetAmount && before < v.Source.TargetAmount)
                AppState.Toast($"Goal reached: {v.Name}!");
        }

        private async void GoalDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not GoalView v) return;
            if (!Ui.Confirm($"Delete the goal \"{v.Name}\"?", "Delete goal")) return;
            try { await AppState.Store.DeleteAsync(v.Source, AppState.Store.Goals); }
            catch (Exception ex) { Ui.Error("Couldn't delete.", ex); return; }
            if (_editingGoal == v.Source) ResetGoalForm();
            Refresh();
        }
    }
}
