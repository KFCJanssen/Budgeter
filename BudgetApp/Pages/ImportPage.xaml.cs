using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using BudgetApp.Help;
using BudgetApp.Import;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.Win32;

namespace BudgetApp.Pages
{
    /// <summary>A value + label pair for combo boxes in the review grid.</summary>
    public sealed class Choice
    {
        public Choice(object? value, string label) { Value = value; Label = label; }
        public object? Value { get; }
        public string Label { get; }
    }

    /// <summary>
    /// The bank import wizard: 1 choose file (with bank guides), 2 check columns (other banks),
    /// 3 review and categorise, 4 done (with undo).
    /// </summary>
    public partial class ImportPage : UserControl, IPage
    {
        // Bound from the review grid's combo boxes.
        public List<string> Categories { get; private set; } = new();
        public List<Choice> TypeChoices { get; } = new()
        {
            new Choice(TransactionType.Expense, "Out"),
            new Choice(TransactionType.Income, "In"),
            new Choice(TransactionType.Transfer, "Transfer"),
        };
        public List<Choice> RecurringChoices { get; private set; } = new();

        private string _fileName = "";
        private CsvTable? _table;
        private DetectedFormat? _detected;
        private ColumnMapping _mapping = new();
        private List<ImportCandidate> _candidates = new();
        private ImportBatch? _lastBatch;
        private bool _loadingMapping;
        private bool _propagating;
        private bool _importing;

        private sealed record ColumnChoice(int Index, string Label);
        private sealed record HistoryRow(ImportBatch Batch, string Title, string Subtitle);
        private sealed record RuleRow(CategoryRule Rule, string Text);
        private sealed record PreviewRow(int RowNumber, string DateText, string AmountText, Brush Brush, string Payee, string Description, string? Error);

        public ImportPage()
        {
            InitializeComponent();
            ShowBankGuide();
            ShowStep(1);
        }

        public void OnShown(object? argument)
        {
            if (argument is string path && File.Exists(path)) LoadFile(path);
            RefreshSidePanels();
        }

        // ------------------------------------------------------------------ steps

        private int _step = 1;

        private void ShowStep(int step)
        {
            _step = step;
            Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
            var chips = new[] { (Chip1, Chip1Text), (Chip2, Chip2Text), (Chip3, Chip3Text), (Chip4, Chip4Text) };
            for (int i = 0; i < chips.Length; i++)
            {
                bool current = i + 1 == step, done = i + 1 < step;
                chips[i].Item1.Background = current ? Look.Accent : done ? Look.Res("AccentSoftBrush") : Look.Res("CardBrush");
                chips[i].Item1.BorderBrush = current || done ? Look.Accent : Look.Res("BorderBrush");
                chips[i].Item2.Foreground = current ? Brushes.White : done ? Look.Res("AccentDarkBrush") : Look.Res("MutedBrush");
                chips[i].Item2.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private void Back1_Click(object sender, RoutedEventArgs e)
        {
            if (_step == 3 && _detected is { IsKnownBank: false }) ShowStep(2);
            else ShowStep(1);
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            _candidates = new();
            ReviewGrid.ItemsSource = null;
            ShowStep(1);
            RefreshSidePanels();
        }

        private void FullGuide_Click(object sender, RoutedEventArgs e) => AppState.Navigate(PageKey.Help, SelectedGuideId);
        private void ReviewHelp_Click(object sender, RoutedEventArgs e) => AppState.Navigate(PageKey.Help, "import-review");

        // ------------------------------------------------------------------ step 1

        private string SelectedGuideId =>
            BankRabo.IsChecked == true ? "bank-rabobank" :
            BankAbn.IsChecked == true ? "bank-abnamro" :
            BankOther.IsChecked == true ? "bank-other" : "bank-ing";

        private void Bank_Checked(object sender, RoutedEventArgs e) => ShowBankGuide();

        private void ShowBankGuide()
        {
            if (BankGuide == null) return;
            var guide = Guides.Find(SelectedGuideId);
            if (guide != null) BankGuide.Show(guide);
        }

        private void RefreshSidePanels()
        {
            var store = AppState.Store;
            var history = store.Imports.Select(b => new HistoryRow(b,
                string.IsNullOrWhiteSpace(b.FileName) ? b.Format : b.FileName,
                $"{b.ImportedAt.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)} · {b.Format} · {b.Count} rows")).ToList();
            HistoryList.ItemsSource = history;
            NoHistory.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var rules = store.Rules.Select(r => new RuleRow(r,
                $"\"{r.Pattern}\" → {r.Category}{(r.SetType == TransactionType.Transfer ? " (transfer)" : "")}")).ToList();
            RuleList.ItemsSource = rules;
            NoRules.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var typed = RuleCategory.Text;
            RuleCategory.ItemsSource = AppState.Categories();
            RuleCategory.Text = typed;
        }

        private void ChooseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose the file you downloaded from your bank",
                Filter = "Bank files (*.csv;*.txt;*.tab)|*.csv;*.txt;*.tab|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is string home && Directory.Exists(Path.Combine(home, "Downloads"))
                    ? Path.Combine(home, "Downloads") : ""
            };
            if (dialog.ShowDialog() == true) LoadFile(dialog.FileName);
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DropZone.BorderBrush = Look.Accent;
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e) => DropZone.BorderBrush = Look.Res("InputBorderBrush");

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = Look.Res("InputBorderBrush");
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) LoadFile(files[0]);
        }

        /// <summary>Reads a bank file and moves to step 2 (unknown layout) or 3 (known bank).</summary>
        public void LoadFile(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                Ui.Warn($"The file couldn't be opened. If it's open in Excel, close it there first.\n\n{ex.Message}", "Can't open file");
                return;
            }
            catch (Exception ex)
            {
                Ui.Error("The file couldn't be opened.", ex);
                return;
            }
            LoadBytes(bytes, Path.GetFileName(path));
        }

        public void LoadBytes(byte[] bytes, string fileName)
        {
            _fileName = fileName;
            _table = CsvTable.FromBytes(bytes);
            if (_table.Rows.Count == 0)
            {
                Ui.Warn("This file is empty or isn't a text/CSV file.", "Nothing to import");
                return;
            }
            if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                Ui.Warn("This looks like an Excel (.xlsx) or zip file. Download the CSV/TXT version from your bank instead.", "Not a CSV file");
                return;
            }

            _detected = BankFormatDetector.Detect(_table);
            _mapping = _detected.Mapping.Clone();

            // Reflect the detected bank in the bank buttons (shows the matching guide too).
            switch (_detected.Format)
            {
                case BankFormat.Ing: BankIng.IsChecked = true; break;
                case BankFormat.Rabobank: BankRabo.IsChecked = true; break;
                case BankFormat.AbnAmro: BankAbn.IsChecked = true; break;
                default: BankOther.IsChecked = true; break;
            }

            if (_detected.IsKnownBank)
            {
                PlanAndReview();
            }
            else
            {
                FillMappingControls();
                ShowStep(2);
            }
        }

        // ------------------------------------------------------------------ rules (step 1 side panel)

        private void RuleForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (Look.IsSubmitKey(e)) { e.Handled = true; AddRule_Click(sender, e); }
        }

        private async void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var pattern = RulePattern.Text?.Trim() ?? "";
            var category = (RuleCategory.Text ?? "").Trim();
            if (pattern.Length < 2 || category.Length == 0)
            {
                Ui.Warn("Type the text to look for (e.g. \"Albert Heijn\") and the category.", "Add rule");
                return;
            }
            var store = AppState.Store;
            if (store.Rules.Any(r => string.Equals(r.Pattern.Trim(), pattern, StringComparison.OrdinalIgnoreCase)))
            {
                Ui.Warn($"There's already a rule for \"{pattern}\". Remove it first to change it.", "Add rule");
                return;
            }
            try
            {
                await store.AddAsync(new CategoryRule
                {
                    Pattern = pattern,
                    Category = category,
                    SetType = RuleTransfer.IsChecked == true ? TransactionType.Transfer : null
                }, store.Rules);
            }
            catch (Exception ex) { Ui.Error("Couldn't add the rule.", ex); return; }
            RulePattern.Clear();
            RuleCategory.Text = "";
            RuleTransfer.IsChecked = false;
            RefreshSidePanels();

            // Offer to apply it to existing transactions too.
            var matching = store.Transactions.Where(t =>
                RuleEngine.BestMatch(new[] { store.Rules.Last() }, t.Payee, t.Description) != null &&
                !string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count > 0 && Ui.Confirm($"{matching.Count} existing transaction(s) contain \"{pattern}\". Give them the category {category} too?", "Apply rule"))
            {
                foreach (var t in matching)
                {
                    t.Category = category;
                    if (RuleTransfer.IsChecked == true) t.Type = TransactionType.Transfer;
                }
                try { await store.SaveEditsAsync(); } catch (Exception ex) { Ui.Error("Couldn't update the transactions.", ex); }
            }
        }

        private async void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not CategoryRule rule) return;
            try { await AppState.Store.DeleteAsync(rule, AppState.Store.Rules); }
            catch (Exception ex) { Ui.Error("Couldn't remove the rule.", ex); return; }
            RefreshSidePanels();
            AppState.Toast($"Rule \"{rule.Pattern}\" removed");
        }

        private async void UndoHistory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ImportBatch batch) return;
            await UndoAsync(batch);
            RefreshSidePanels();
        }

        private async System.Threading.Tasks.Task UndoAsync(ImportBatch batch)
        {
            int count = AppState.Store.Transactions.Count(t => t.ImportBatchId == batch.Id);
            if (!Ui.Confirm($"Remove the {count} transaction(s) imported from \"{batch.FileName}\" on {batch.ImportedAt:d MMM yyyy}?\n\nRules made during the import are kept.", "Undo import"))
                return;
            try
            {
                int removed = await AppState.Store.UndoImportAsync(batch);
                AppState.Toast($"Import undone: {removed} transaction(s) removed");
            }
            catch (Exception ex) { Ui.Error("Couldn't undo the import.", ex); }
        }

        // ------------------------------------------------------------------ step 2: columns

        private void FillMappingControls()
        {
            if (_table == null || _detected == null) return;
            _loadingMapping = true;

            MappingIntro.Text = _detected.IsKnownBank
                ? $"Recognised as a {_detected.BankName} file, so the columns below were filled in automatically. You normally don't need to change anything."
                : "Budgeter guessed the columns from the file. Check them using the preview on the right: dates and amounts should look right, money going out in red.";
            HasHeaderBox.IsChecked = _mapping.HasHeader;

            var names = BankFormatDetector.ColumnNames(_table, _mapping.HasHeader);
            var sampleRow = _table.Rows.Skip(_mapping.HasHeader ? 1 : 0).FirstOrDefault() ?? Array.Empty<string>();
            var columns = names.Select((n, i) =>
            {
                var sample = i < sampleRow.Length ? sampleRow[i] : "";
                if (sample.Length > 22) sample = sample[..22] + "…";
                return new ColumnChoice(i, sample.Length > 0 ? $"{n}   (e.g. {sample})" : n);
            }).ToList();
            var withNone = new[] { new ColumnChoice(-1, "(none)") }.Concat(columns).ToList();

            void Fill(ComboBox box, List<ColumnChoice> items, int selected)
            {
                box.ItemsSource = items;
                box.SelectedItem = items.FirstOrDefault(c => c.Index == selected) ?? items[0];
            }
            Fill(MapDate, withNone, _mapping.Date);
            Fill(MapAmount, withNone, _mapping.Amount);
            Fill(MapDebitCredit, withNone, _mapping.DebitCredit);
            Fill(MapPayee, withNone, _mapping.Payee);
            Fill(MapDescription, withNone, _mapping.Description.Count > 0 ? _mapping.Description[0] : -1);
            Fill(MapDescription2, withNone, _mapping.Description.Count > 1 ? _mapping.Description[1] : -1);
            Fill(MapCounter, withNone, _mapping.CounterAccount);
            Fill(MapOwn, withNone, _mapping.OwnAccount);

            var formats = new List<string> { "Detect automatically", "yyyyMMdd", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "dd.MM.yyyy", "MM/dd/yyyy", "dd-MM-yy" };
            if (_mapping.DateFormat != null && !formats.Contains(_mapping.DateFormat)) formats.Add(_mapping.DateFormat);
            MapDateFormat.ItemsSource = formats;
            MapDateFormat.SelectedItem = _mapping.DateFormat ?? formats[0];

            _loadingMapping = false;
            UpdatePreview();
        }

        private void Mapping_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingMapping || _table == null) return;
            int Sel(ComboBox b) => (b.SelectedItem as ColumnChoice)?.Index ?? -1;

            bool headerChanged = (HasHeaderBox.IsChecked == true) != _mapping.HasHeader;
            _mapping.HasHeader = HasHeaderBox.IsChecked == true;
            _mapping.Date = Sel(MapDate);
            _mapping.Amount = Sel(MapAmount);
            _mapping.DebitCredit = Sel(MapDebitCredit);
            _mapping.Payee = Sel(MapPayee);
            _mapping.Description = new[] { Sel(MapDescription), Sel(MapDescription2) }.Where(i => i >= 0).Distinct().ToList();
            _mapping.CounterAccount = Sel(MapCounter);
            _mapping.OwnAccount = Sel(MapOwn);
            var fmt = MapDateFormat.SelectedItem as string;
            _mapping.DateFormat = fmt == null || fmt.StartsWith("Detect") ? null : fmt;
            if (_mapping.DateFormat == null && _mapping.Date >= 0)
                _mapping.DateFormat = BankFormatDetector.DetectDateFormat(_table.Rows.Skip(_mapping.HasHeader ? 1 : 0).Select(r => _mapping.Date < r.Length ? r[_mapping.Date] : ""));
            _mapping.ExtractPayeeFromDescription = _mapping.Payee < 0;

            if (headerChanged) FillMappingControls();   // column names change
            else UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_table == null) return;
            if (!_mapping.IsUsable)
            {
                PreviewGrid.ItemsSource = null;
                PreviewStatus.Text = "Choose at least the Date and Amount columns.";
                PreviewStatus.Foreground = Look.Bad;
                return;
            }
            var rows = ImportParser.Parse(_table, _mapping);
            int errors = rows.Count(r => r.Error != null);
            PreviewGrid.ItemsSource = rows.Take(200).Select(r => new PreviewRow(
                r.RowNumber,
                r.Error == null ? r.Date.ToString("dd-MM-yyyy") : "",
                r.Error == null ? (r.Amount < 0 ? "−" : "+") + Money.Format(Math.Abs(r.Amount)) : "",
                r.Amount < 0 ? Look.Bad : Look.Good,
                r.Payee, r.Description, r.Error)).ToList();
            PreviewStatus.Text = errors == 0
                ? $"{rows.Count} rows read without problems. In: {rows.Count(r => r.Amount > 0)}, out: {rows.Count(r => r.Amount < 0)}."
                : $"{rows.Count} rows, {errors} can't be read with these settings - see the Problem column.";
            PreviewStatus.Foreground = errors == 0 ? Look.Good : Look.Warn;
        }

        private void CheckColumns_Click(object sender, RoutedEventArgs e)
        {
            FillMappingControls();
            ShowStep(2);
        }

        private void ToReview_Click(object sender, RoutedEventArgs e)
        {
            if (!_mapping.IsUsable) { Ui.Warn("Choose at least the Date and Amount columns.", "Columns"); return; }
            PlanAndReview();
        }

        // ------------------------------------------------------------------ step 3: review

        private void PlanAndReview()
        {
            if (_table == null || _detected == null) return;
            var store = AppState.Store;
            var rows = ImportParser.Parse(_table, _mapping);
            if (rows.Count == 0)
            {
                Ui.Warn("No transactions were found in this file.", "Nothing to import");
                return;
            }

            foreach (var c in _candidates) c.PropertyChanged -= Candidate_PropertyChanged;
            _candidates = ImportPlanner.Plan(rows, _detected.Format, store.Transactions, store.Recurring, store.Rules,
                new ImportOptions(store.UseBuiltInRules, store.MyAccounts));
            foreach (var c in _candidates) c.PropertyChanged += Candidate_PropertyChanged;

            Categories = AppState.Categories(new[] { BudgetCalculator.Uncategorized, "Transfer", "Savings transfer" }
                .Concat(_candidates.Select(c => c.Category)));
            RecurringChoices = new[] { new Choice(null, "—") }
                .Concat(store.Recurring.Where(r => r.IsActive).Select(r => new Choice(r.Id, r.Name))).ToList();

            ReviewTitle.Text = $"Review {_candidates.Count} rows from {_fileName}" + (_detected.IsKnownBank ? $" ({_detected.BankName})" : "");
            AttentionOnly.IsChecked = false;
            ReviewGrid.ItemsSource = null;
            ReviewGrid.ItemsSource = new ListCollectionView(_candidates);
            UpdateSummary();
            ShowStep(3);
        }

        private void Candidate_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ImportCandidate c) return;
            if (e.PropertyName == nameof(ImportCandidate.RecurringId) && !_propagating)
            {
                var r = AppState.Store.Recurring.FirstOrDefault(x => x.Id == c.RecurringId);
                c.RecurringName = r?.Name;
                if (r != null) { c.Category = r.Category; c.Type = r.Type; }
            }
            if ((e.PropertyName == nameof(ImportCandidate.Category) || e.PropertyName == nameof(ImportCandidate.Type)) && !_propagating)
            {
                _propagating = true;
                try
                {
                    int changed = ImportPlanner.PropagateCategory(_candidates, c);
                    if (changed > 0) AppState.Toast($"Also applied to {changed} other row{(changed == 1 ? "" : "s")} from \"{c.Payee}\"");
                }
                finally { _propagating = false; }
            }
            if (e.PropertyName is nameof(ImportCandidate.Include) or nameof(ImportCandidate.Category) or nameof(ImportCandidate.Type))
                UpdateSummary();
        }

        private void UpdateSummary()
        {
            int toImport = _candidates.Count(c => c.Include && c.CanInclude);
            int already = _candidates.Count(c => c.Status == CandidateStatus.AlreadyImported);
            int dupes = _candidates.Count(c => c.Status == CandidateStatus.PossibleDuplicate);
            int unread = _candidates.Count(c => c.Status == CandidateStatus.Unreadable);
            int uncategorized = _candidates.Count(c => c.Include && c.IsUncategorized && c.Type != TransactionType.Transfer);
            int linked = _candidates.Count(c => c.Include && c.RecurringId != null);
            int transfers = _candidates.Count(c => c.Include && c.Type == TransactionType.Transfer);

            SummaryChips.Children.Clear();
            void Chip(string text, string bg, string fg)
            {
                SummaryChips.Children.Add(new Border
                {
                    Style = (Style)FindResource("Chip"),
                    Padding = new Thickness(12, 5, 12, 5),
                    Background = Look.Res(bg),
                    Child = new TextBlock { Text = text, Foreground = Look.Res(fg), FontWeight = FontWeights.SemiBold }
                });
            }
            Chip($"{toImport} to import", "AccentSoftBrush", "AccentDarkBrush");
            if (uncategorized > 0) Chip($"{uncategorized} need a category", "WarnSoftBrush", "WarnBrush");
            if (linked > 0) Chip($"{linked} paid by programmed items", "PageBrush", "MutedBrush");
            if (transfers > 0) Chip($"{transfers} transfers", "PageBrush", "MutedBrush");
            if (already > 0) Chip($"{already} already imported", "PageBrush", "MutedBrush");
            if (dupes > 0) Chip($"{dupes} possible duplicates (not ticked)", "WarnSoftBrush", "WarnBrush");
            if (unread > 0) Chip($"{unread} couldn't be read", "BadSoftBrush", "BadBrush");

            ImportButton.Content = toImport == 0 ? "Nothing to import" : $"Import {toImport} transaction{(toImport == 1 ? "" : "s")}";
            ImportButton.IsEnabled = toImport > 0 && !_importing;
        }

        private void AttentionOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (ReviewGrid.ItemsSource is ListCollectionView view)
            {
                view.Filter = AttentionOnly.IsChecked == true ? o => o is ImportCandidate c && c.NeedsAttention : null;
                view.Refresh();
            }
        }

        private void TickAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _candidates.Where(c => c.Status == CandidateStatus.New)) c.Include = true;
        }

        private void UntickAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _candidates) c.Include = false;
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_importing) return;
            // Commit a category the user is still typing in.
            ReviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
            Keyboard.ClearFocus();

            var store = AppState.Store;
            var rows = ImportPlanner.ToTransactions(_candidates, null);
            if (rows.Count == 0) return;
            var rules = ImportPlanner.NewRules(_candidates, store.Rules);

            _importing = true;
            ImportButton.IsEnabled = false;
            try
            {
                _lastBatch = await store.SaveImportAsync(new ImportBatch
                {
                    FileName = _fileName,
                    Format = _detected?.BankName ?? "CSV",
                    ImportedAt = DateTime.Now
                }, rows, rules);
            }
            catch (Exception ex)
            {
                Ui.Error("The import couldn't be saved. Nothing was imported.", ex);
                return;
            }
            finally
            {
                _importing = false;
                UpdateSummary();
            }

            int skipped = _candidates.Count - rows.Count;
            DoneTitle.Text = $"Imported {rows.Count} transaction{(rows.Count == 1 ? "" : "s")}";
            var details = new List<string>();
            var first = rows.Min(r => r.Date);
            var last = rows.Max(r => r.Date);
            details.Add($"From {_fileName}, {first:d MMM yyyy} to {last:d MMM yyyy}.");
            if (rules.Count > 0) details.Add($"Budgeter learned {rules.Count} new rule{(rules.Count == 1 ? "" : "s")} for next time.");
            int stillUncategorized = rows.Count(r => r.Type != TransactionType.Transfer && string.Equals(r.Category, BudgetCalculator.Uncategorized, StringComparison.OrdinalIgnoreCase));
            if (stillUncategorized > 0) details.Add($"{stillUncategorized} still have no category - the Dashboard's To do list takes you there.");
            if (skipped > 0) details.Add($"{skipped} row{(skipped == 1 ? " was" : "s were")} skipped (already imported, unticked or unreadable).");
            DoneDetails.Text = string.Join("\n", details);
            ShowStep(4);
            RefreshSidePanels();
        }

        // ------------------------------------------------------------------ step 4

        private void SeeImported_Click(object sender, RoutedEventArgs e)
        {
            var newest = AppState.Store.Transactions.Where(t => t.ImportBatchId == _lastBatch?.Id).OrderByDescending(t => t.Date).FirstOrDefault();
            AppState.Period = newest != null ? BudgetPeriod.ForMonth(newest.Date) : BudgetPeriod.ForMonth(DateTime.Today);
            AppState.Navigate(PageKey.Transactions);
        }

        private async void UndoLast_Click(object sender, RoutedEventArgs e)
        {
            if (_lastBatch == null || !AppState.Store.Imports.Contains(_lastBatch)) { ShowStep(1); return; }
            await UndoAsync(_lastBatch);
            if (!AppState.Store.Imports.Contains(_lastBatch))
            {
                _lastBatch = null;
                PlanAndReview();   // back to the review, ready to fix and import again
            }
        }

        // ------------------------------------------------------------------ for the self-test

        internal IReadOnlyList<ImportCandidate> Candidates => _candidates;
        internal int CurrentStep => _step;
        internal void SimulateImportClick() => Import_Click(this, new RoutedEventArgs());
    }
}
