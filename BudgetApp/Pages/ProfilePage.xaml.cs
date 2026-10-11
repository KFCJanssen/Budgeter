using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BudgetApp.Models;
using BudgetApp.Planning;
using BudgetApp.Services;
using Microsoft.Win32;

namespace BudgetApp.Pages
{
    public partial class ProfilePage : UserControl, IPage
    {
        private BalanceItem? _editing;
        private bool _loading;

        public static readonly (BalanceKind Kind, string Label)[] Kinds =
        {
            (BalanceKind.Checking, "Current account"),
            (BalanceKind.Savings, "Savings account"),
            (BalanceKind.Investments, "Investments"),
            (BalanceKind.Pension, "Pension (own savings)"),
            (BalanceKind.Property, "House / property"),
            (BalanceKind.OtherAsset, "Other (owned)"),
            (BalanceKind.StudentLoan, "Student loan (DUO)"),
            (BalanceKind.Mortgage, "Mortgage"),
            (BalanceKind.CreditCard, "Credit card"),
            (BalanceKind.PersonalLoan, "Personal loan"),
            (BalanceKind.OtherDebt, "Other debt"),
        };

        public static string KindLabel(BalanceKind k) => Kinds.FirstOrDefault(x => x.Kind == k).Label ?? k.ToString();

        private sealed record KindChoice(BalanceKind Kind, string Label);

        private sealed class BalanceRow
        {
            public BalanceRow(BalanceItem b)
            {
                Source = b;
                Name = b.Name;
                KindText = KindLabel(b.Kind);
                AmountText = (b.IsDebt ? "−" : "") + Money.Format(b.Amount);
                Brush = b.IsDebt ? Look.Bad : Look.Good;
                RateText = b.InterestRate is decimal r ? r.ToString("0.##", Money.Culture) + "%" : "";
            }
            public BalanceItem Source { get; }
            public string Name { get; }
            public string KindText { get; }
            public string AmountText { get; }
            public Brush Brush { get; }
            public string RateText { get; }
        }

        private sealed class AdviceView
        {
            public AdviceView(Advice a)
            {
                Area = a.Area.ToUpperInvariant();
                Title = a.Title;
                Finding = a.Finding;
                Suggestion = a.Suggestion;
                (LevelText, Brush, SoftBrush) = a.Level switch
                {
                    AdviceLevel.Action => ("Act on this", Look.Bad, Look.Res("BadSoftBrush")),
                    AdviceLevel.Attention => ("Worth a look", Look.Warn, Look.Res("WarnSoftBrush")),
                    AdviceLevel.Good => ("Good", Look.Good, Look.Res("GoodSoftBrush")),
                    _ => ("Info", Look.Res("AccentDarkBrush"), Look.Res("AccentSoftBrush"))
                };
            }
            public string Area { get; }
            public string Title { get; }
            public string Finding { get; }
            public string Suggestion { get; }
            public string LevelText { get; }
            public Brush Brush { get; }
            public Brush SoftBrush { get; }
        }

        public ProfilePage()
        {
            InitializeComponent();
            HouseholdBox.ItemsSource = new[] { "Single", "Living with partner", "Partner and children", "Single parent", "Shared house / student house", "Living with parents" };
            HousingBox.ItemsSource = new[] { "Renting", "Renting (student room)", "Own home with mortgage", "Own home, no mortgage", "Living with parents" };
            WorkBox.ItemsSource = new[] { "Student", "Student with a side job", "Employed", "Self-employed", "Job seeker", "Retired" };
            KindBox.ItemsSource = Kinds.Select(k => new KindChoice(k.Kind, k.Label)).ToList();
            KindBox.SelectedIndex = 1;
        }

        public void OnShown(object? argument)
        {
            _loading = true;
            var p = AppState.Store.GetProfile();
            NameBox.Text = p.Name;
            BirthYearBox.Text = p.BirthYear?.ToString(CultureInfo.InvariantCulture) ?? "";
            HouseholdBox.Text = p.Household;
            HousingBox.Text = p.Housing;
            WorkBox.Text = p.Work;
            GoalsBox.Text = p.Goals;
            MonthsBox.SelectedIndex = p.ReportMonths <= 3 ? 0 : p.ReportMonths >= 12 ? 2 : 1;
            _loading = false;
            Refresh();
        }

        private int ReportMonths => MonthsBox.SelectedIndex switch { 0 => 3, 2 => 12, _ => 6 };

        private ProfileInfo ReadProfile() => new()
        {
            Name = NameBox.Text?.Trim() ?? "",
            BirthYear = int.TryParse(BirthYearBox.Text, out var y) && y > 1900 && y <= DateTime.Today.Year ? y : null,
            Household = HouseholdBox.Text?.Trim() ?? "",
            Housing = HousingBox.Text?.Trim() ?? "",
            Work = WorkBox.Text?.Trim() ?? "",
            Goals = GoalsBox.Text?.Trim() ?? "",
            ReportMonths = ReportMonths
        };

        internal FinancialSnapshot BuildSnapshot()
        {
            var store = AppState.Store;
            return FinancialSnapshot.Build(store.Transactions, store.Recurring, store.Budgets, store.Goals, store.Balances, ReadProfile(), DateTime.Today);
        }

        private void Refresh()
        {
            var store = AppState.Store;
            var rows = store.Balances.OrderBy(b => b.IsDebt).ThenByDescending(b => b.Amount).Select(b => new BalanceRow(b)).ToList();
            BalanceGrid.ItemsSource = rows;

            var snap = BuildSnapshot();
            AssetsText.Text = Money.Format(snap.TotalAssets);
            DebtsText.Text = Money.Format(snap.TotalDebts);
            NetWorthText.Text = Money.Format(snap.NetWorth);
            NetWorthText.Foreground = snap.NetWorth >= 0 ? Look.Accent : Look.Bad;

            CheckIntro.Text = snap.HasData
                ? $"Based on {snap.Months.Count} complete month{(snap.Months.Count == 1 ? "" : "s")} ({snap.Months.First().Label} - {snap.Months.Last().Label}), your programmed items and the balances on the left."
                : "Add or import some transactions to get a full health check.";

            KpiPanel.Children.Clear();
            void Kpi(string label, string value, Brush? brush = null)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("StatLabel") });
                sp.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = brush ?? Look.Text });
                KpiPanel.Children.Add(new Border { Background = Look.Res("PageBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 10, 10), Child = sp });
            }
            Kpi("AVG INCOME", Money.Format(snap.AvgIncome));
            Kpi("AVG SPENDING", Money.Format(snap.AvgExpenses));
            Kpi("SAVINGS RATE", snap.SavingsRate is decimal r ? Look.Pct(r) : "–", snap.SavingsRate is decimal r2 ? (r2 < 0 ? Look.Bad : r2 < 0.1m ? Look.Warn : Look.Good) : null);
            Kpi("BUFFER", snap.Assets.Count > 0 && snap.EmergencyMonths is decimal em ? $"{em:0.0} months" : "–",
                snap.EmergencyMonths is decimal em2 && snap.Assets.Count > 0 ? (em2 < 1 ? Look.Bad : em2 < 3 ? Look.Warn : Look.Good) : null);

            AdviceList.ItemsSource = AdviceEngine.Evaluate(snap).Select(a => new AdviceView(a)).ToList();
        }

        private async void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            try { await AppState.Store.SaveProfileAsync(ReadProfile()); }
            catch (Exception ex) { Ui.Error("Couldn't save your profile.", ex); return; }
            AppState.Toast("Profile saved");
            Refresh();
        }

        private async void MonthsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !IsLoaded) return;
            try { await AppState.Store.SetSettingAsync("profile.reportMonths", ReportMonths.ToString(CultureInfo.InvariantCulture)); } catch { /* not important */ }
            Refresh();
        }

        // ------------------------------------------------------------------ balances

        private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var kind = (KindBox.SelectedItem as KindChoice)?.Kind;
            bool debt = kind is BalanceKind k && (int)k >= 10;
            PaymentPanel.Visibility = debt ? Visibility.Visible : Visibility.Hidden;
            // DUO's 2026 rate (SF35) as a starting point; the user can change it.
            if (_editing == null && kind == BalanceKind.StudentLoan && string.IsNullOrWhiteSpace(BalanceRate.Text))
                BalanceRate.Text = "2,33";
        }

        private void BalanceForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; SaveBalance_Click(sender, e); }
        }

        private async void SaveBalance_Click(object sender, RoutedEventArgs e)
        {
            if (KindBox.SelectedItem is not KindChoice kind) return;
            if (!Look.ReadAmount(BalanceAmount, out var amount, allowZero: true)) return;
            var name = BalanceName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) name = kind.Label;
            decimal? rate = Look.ReadPercent(BalanceRate.Text);
            decimal? payment = Money.TryParse(BalancePayment.Text, out var p) ? p : null;
            if (kind.Kind < BalanceKind.StudentLoan) payment = null;

            var store = AppState.Store;
            try
            {
                if (_editing != null)
                {
                    _editing.Name = name;
                    _editing.Kind = kind.Kind;
                    _editing.Amount = amount;
                    _editing.InterestRate = rate;
                    _editing.MonthlyPayment = payment;
                    _editing.UpdatedAt = DateTime.Today;
                    await store.SaveEditsAsync();
                }
                else
                {
                    await store.AddAsync(new BalanceItem { Name = name, Kind = kind.Kind, Amount = amount, InterestRate = rate, MonthlyPayment = payment, UpdatedAt = DateTime.Today }, store.Balances);
                }
            }
            catch (Exception ex) { Ui.Error("Couldn't save.", ex); return; }
            ResetBalanceForm();
            Refresh();
        }

        private void ResetBalanceForm()
        {
            _editing = null;
            BalanceName.Clear();
            BalanceAmount.Clear();
            BalanceRate.Clear();
            BalancePayment.Clear();
            BalanceFormHeader.Text = "Add";
            BalanceSave.Content = "Add";
            BalanceCancel.Visibility = Visibility.Collapsed;
        }

        private void CancelBalance_Click(object sender, RoutedEventArgs e) => ResetBalanceForm();

        private void EditBalance(BalanceItem b)
        {
            _editing = b;
            KindBox.SelectedItem = (KindBox.ItemsSource as List<KindChoice>)?.FirstOrDefault(k => k.Kind == b.Kind);
            BalanceName.Text = b.Name;
            BalanceAmount.Text = Look.AmountForEditing(b.Amount);
            BalanceRate.Text = b.InterestRate?.ToString("0.##", Money.Culture) ?? "";
            BalancePayment.Text = b.MonthlyPayment is decimal mp ? Look.AmountForEditing(mp) : "";
            BalanceFormHeader.Text = $"Update \"{b.Name}\"";
            BalanceSave.Content = "Save";
            BalanceCancel.Visibility = Visibility.Visible;
            BalanceAmount.Focus();
            BalanceAmount.SelectAll();
        }

        private void EditBalance_Click(object sender, RoutedEventArgs e)
        {
            if (BalanceGrid.SelectedItem is BalanceRow r) EditBalance(r.Source);
            else Ui.Info("Select a line in the table first.", "Nothing selected");
        }

        private void BalanceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BalanceGrid.SelectedItem is BalanceRow r) EditBalance(r.Source);
        }

        private async void DeleteBalance_Click(object sender, RoutedEventArgs e)
        {
            if (BalanceGrid.SelectedItem is not BalanceRow r) { Ui.Info("Select a line in the table first.", "Nothing selected"); return; }
            if (!Ui.Confirm($"Remove \"{r.Name}\"?", "Remove")) return;
            try { await AppState.Store.DeleteAsync(r.Source, AppState.Store.Balances); }
            catch (Exception ex) { Ui.Error("Couldn't remove.", ex); return; }
            if (_editing == r.Source) ResetBalanceForm();
            Refresh();
        }

        // ------------------------------------------------------------------ PDF

        private async void Pdf_Click(object sender, RoutedEventArgs e)
        {
            try { await AppState.Store.SaveProfileAsync(ReadProfile()); } catch { /* still make the report */ }
            var snap = BuildSnapshot();
            var dialog = new SaveFileDialog
            {
                Filter = "PDF file (*.pdf)|*.pdf",
                FileName = $"Financial profile {DateTime.Today:yyyy-MM-dd}.pdf"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await System.Threading.Tasks.Task.Run(() => ReportService.Create(snap, AdviceEngine.Evaluate(snap), dialog.FileName));
            }
            catch (Exception ex)
            {
                Ui.Error("The PDF couldn't be created. If a file with that name is open in a PDF reader, close it and try again.", ex);
                return;
            }
            finally { Mouse.OverrideCursor = null; }

            AppState.Toast("PDF report saved", "Open", () => OpenFile(dialog.FileName));
            OpenFile(dialog.FileName);
        }

        private static void OpenFile(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* no PDF reader */ }
        }
    }
}
