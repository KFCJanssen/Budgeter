using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BudgetApp.Data;
using BudgetApp.Import;
using BudgetApp.Models;
using BudgetApp.Pages;
using BudgetApp.Planning;
using BudgetApp.Services;
using Microsoft.Data.Sqlite;

namespace BudgetApp
{
    /// <summary>
    /// "BudgetApp.exe --self-test &lt;folder&gt;": an automatic check of the whole app, run by GitHub Actions on
    /// Windows. It never touches your real data (it uses &lt;folder&gt;\data). It:
    ///  1. creates a database exactly like Budgeter 1.0 made it, and upgrades it (data must survive);
    ///  2. fills in a year of realistic demo data;
    ///  3. imports the ING, Rabobank and ABN AMRO example files, re-imports (no doubles) and undoes an import;
    ///  4. creates the PDF report (and PNG previews of its pages);
    ///  5. opens every page and calculator and saves a screenshot, failing on any error or broken data binding.
    /// Writes selftest.log; exit code 0 = everything passed.
    /// </summary>
    public static class SelfTest
    {
        private static readonly StringBuilder Log = new();
        private static int _failures;
        private static string _out = "";

        private sealed class BindingErrors : TraceListener
        {
            public readonly List<string> Errors = new();
            private readonly StringBuilder _line = new();
            public override void Write(string? message) => _line.Append(message);
            public override void WriteLine(string? message)
            {
                _line.Append(message);
                Errors.Add(_line.ToString());
                _line.Clear();
            }
        }

        public static async Task<int> RunAsync(string output)
        {
            _out = Path.GetFullPath(output);
            Directory.CreateDirectory(_out);
            var dataDir = Path.Combine(_out, "data");
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
            Environment.SetEnvironmentVariable("BUDGETER_DATA_DIR", dataDir);
            AppState.Unattended = true;
            AppState.NoAnimations = true;

            var bindingErrors = new BindingErrors();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            try
            {
                await Step("Upgrade a Budgeter 1.0 database", UpgradeOldDatabase);
                await Step("Fill in demo data", SeedDemoData);
                await Step("Import bank files", ImportFiles);
                await Step("PDF report", PdfReport);
                await Step("Screenshots of every page", Screenshots);
            }
            catch (Exception ex)
            {
                Fail("Unexpected: " + ex);
            }

            // "path error" = a binding to a property that doesn't exist: always a real bug.
            // Other binding messages (e.g. an ancestor not found yet while a table row is being built) can be
            // temporary, so they're listed for review but don't fail the run.
            foreach (var e in bindingErrors.Errors.Distinct())
            {
                if (e.Contains("path error", StringComparison.OrdinalIgnoreCase)) Fail("Binding error: " + e);
                else Line("note  binding message: " + e);
            }
            Line($"\n{(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)")}");
            File.WriteAllText(Path.Combine(_out, "selftest.log"), Log.ToString());
            try { Application.Current.MainWindow?.Close(); } catch { }
            AppState.Store?.Dispose();
            return _failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ helpers

        private static void Line(string text)
        {
            Log.AppendLine(text);
            Console.WriteLine(text);
        }

        private static void Fail(string text)
        {
            _failures++;
            Line("FAIL  " + text);
        }

        private static void Check(bool ok, string what)
        {
            if (ok) Line("ok    " + what);
            else Fail(what);
        }

        private static async Task Step(string name, Func<Task> body)
        {
            Line($"\n== {name}");
            var sw = Stopwatch.StartNew();
            try { await body(); }
            catch (Exception ex) { Fail($"{name}: {ex}"); }
            Line($"   ({sw.ElapsedMilliseconds} ms)");
        }

        private static async Task Idle(int delayMs = 150)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(delayMs);
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        // ------------------------------------------------------------------ 1. upgrade

        private static async Task UpgradeOldDatabase()
        {
            var db = AppPaths.DatabasePath;
            // Exactly what Budgeter 1.0 created (EF Core EnsureCreated), with some data in it.
            using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE ""RecurringExpenses"" (""Id"" INTEGER NOT NULL CONSTRAINT ""PK_RecurringExpenses"" PRIMARY KEY AUTOINCREMENT,
                        ""Name"" TEXT NOT NULL, ""Category"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""Frequency"" INTEGER NOT NULL,
                        ""StartDate"" TEXT NOT NULL, ""IsActive"" INTEGER NOT NULL);
                    CREATE TABLE ""Transactions"" (""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Transactions"" PRIMARY KEY AUTOINCREMENT,
                        ""Date"" TEXT NOT NULL, ""Type"" INTEGER NOT NULL, ""Category"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""Description"" TEXT NOT NULL);
                    INSERT INTO ""Transactions"" (""Date"",""Type"",""Category"",""Amount"",""Description"") VALUES
                        ('2025-09-03 14:22:10.1234567', 1, 'Groceries', '23.45', 'old 1.0 entry'),
                        ('2025-09-25 00:00:00', 0, 'Salary', '850.00', 'bijbaan');
                    INSERT INTO ""RecurringExpenses"" (""Name"",""Category"",""Amount"",""Frequency"",""StartDate"",""IsActive"") VALUES
                        ('Netflix', 'Subscriptions', '13.99', 1, '2025-09-01 10:00:00', 1);";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var auto = BackupService.AutoBackupIfDue(db, AppPaths.BackupFolder, DateTime.Now);
            Check(auto != null && File.Exists(auto), "daily backup made before opening");
            Check(BackupService.AutoBackupIfDue(db, AppPaths.BackupFolder, DateTime.Now) == null, "only one automatic backup per day");

            var store = BudgetStore.Open(db, AppPaths.BackupFolder);
            AppState.Store = store;
            Check(Directory.GetFiles(AppPaths.BackupFolder, "budget_before-upgrade*.db").Length == 1, "backup made before upgrading");
            Check(store.Transactions.Count == 2, $"old transactions kept ({store.Transactions.Count})");
            var groceries = store.Transactions.FirstOrDefault(t => t.Category == "Groceries");
            Check(groceries?.Amount == 23.45m && groceries.Type == TransactionType.Expense && groceries.ImportKey == null, "old amounts and types intact");
            Check(store.Recurring.Count == 1 && store.Recurring[0].Type == TransactionType.Expense && store.Recurring[0].Amount == 13.99m,
                "old programmed costs became 'Expense'");

            using (var conn = SchemaMigrator.Open(db))
            {
                Check(SchemaMigrator.GetVersion(conn) == SchemaMigrator.LatestVersion, "data version is now " + SchemaMigrator.LatestVersion);
                var cols = SchemaMigrator.Columns(conn, "Transactions");
                Check(new[] { "Payee", "CounterAccount", "ImportKey", "ImportBatchId", "RecurringId" }.All(cols.Contains), "new columns added");
                Check(new[] { "Budgets", "Goals", "CategoryRules", "ImportBatches", "BalanceItems", "Settings" }.All(t => SchemaMigrator.TableExists(conn, t)), "new tables added");
            }

            // Upgrading again must do nothing.
            Check(SchemaMigrator.Migrate(db, AppPaths.BackupFolder) == null, "second upgrade is a no-op");

            // A newer database must be refused, not damaged.
            var newer = Path.Combine(_out, "newer.db");
            File.Copy(db, newer, true);
            using (var conn = new SqliteConnection($"Data Source={newer};Pooling=False")) { conn.Open(); using var c = conn.CreateCommand(); c.CommandText = "PRAGMA user_version = 99;"; c.ExecuteNonQuery(); }
            try { SchemaMigrator.Migrate(newer, AppPaths.BackupFolder); Fail("newer database should be refused"); }
            catch (NewerDatabaseException) { Line("ok    database from a newer version is refused"); }

            // A fresh install must work too.
            var fresh = Path.Combine(_out, "fresh.db");
            if (File.Exists(fresh)) File.Delete(fresh);
            using (var s = BudgetStore.Open(fresh, AppPaths.BackupFolder))
            {
                await s.AddTransactionAsync(new Transaction { Date = DateTime.Today, Category = "Test", Amount = 1.5m, Type = TransactionType.Expense });
                Check(s.Transactions.Count == 1, "fresh database works");
            }

            // Window right after the upgrade (shows what an existing 1.0 user sees).
            await ShowWindowAsync();
            await Shot("00-dashboard-after-upgrade", PageKey.Dashboard);
            await Task.CompletedTask;
        }

        // ------------------------------------------------------------------ 2. demo data

        private static async Task SeedDemoData()
        {
            var store = AppState.Store;
            var rnd = new Random(42);
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1).AddMonths(-12);

            // The 1.0 Netflix item stays; add the rest.
            foreach (var r in new[]
            {
                new RecurringItem { Name = "Bijbaan Jumbo", Category = "Salary", Amount = 960m, Type = TransactionType.Income, StartDate = first },
                new RecurringItem { Name = "DUO", Category = "Student finance", Amount = 420m, Type = TransactionType.Income, StartDate = first },
                new RecurringItem { Name = "Huur", Category = "Housing", Amount = 545m, StartDate = first },
                new RecurringItem { Name = "Zilveren Kruis", Category = "Insurance", Amount = 152.50m, StartDate = first },
                new RecurringItem { Name = "Odido", Category = "Utilities", Amount = 17.50m, StartDate = first },
                new RecurringItem { Name = "Spotify", Category = "Subscriptions", Amount = 11.99m, StartDate = first },
                new RecurringItem { Name = "Basic-Fit", Category = "Sport", Amount = 29.99m, StartDate = first.AddMonths(3) },
                new RecurringItem { Name = "Liability insurance", Category = "Insurance", Amount = 59m, Frequency = RecurringFrequency.Yearly, StartDate = first },
                new RecurringItem { Name = "Disney+", Category = "Subscriptions", Amount = 9.99m, StartDate = first, IsActive = false },
            })
                await store.AddRecurringAsync(r);

            var shops = new (string Category, string[] Payees, decimal Min, decimal Max, int PerMonth)[]
            {
                ("Groceries", new[] { "Albert Heijn 1403", "Jumbo Maastricht", "Lidl Wyck", "Aldi" }, 6, 42, 7),
                ("Eating out", new[] { "Thuisbezorgd.nl", "Cafe Zondag", "Burger King", "Starbucks" }, 4, 26, 3),
                ("Transport", new[] { "NS Groep", "Arriva", "OV-chipkaart" }, 3, 28, 4),
                ("Shopping", new[] { "Bol.com", "Hema", "Action", "Coolblue" }, 5, 60, 2),
                ("Going out", new[] { "Cafe de Pieter", "Pathe", "Muziekgieterij" }, 8, 35, 2),
                ("Education", new[] { "Studystore", "Universiteit Maastricht" }, 15, 70, 0),
            };

            var month = first;
            while (month <= today)
            {
                int days = DateTime.DaysInMonth(month.Year, month.Month);
                foreach (var (category, payees, min, max, perMonth) in shops)
                {
                    int count = perMonth == 0 ? (rnd.Next(4) == 0 ? 1 : 0) : perMonth + rnd.Next(-1, 3) + (month.Month == 12 && category == "Shopping" ? 4 : 0);
                    for (int i = 0; i < count; i++)
                    {
                        var date = month.AddDays(rnd.Next(days));
                        if (date > today) continue;
                        await store.AddTransactionAsync(new Transaction
                        {
                            Date = date, Type = TransactionType.Expense, Category = category,
                            Payee = payees[rnd.Next(payees.Length)],
                            Amount = Math.Round(min + (decimal)rnd.NextDouble() * (max - min), 2)
                        });
                    }
                }
                if (rnd.Next(3) == 0)
                {
                    var date = month.AddDays(rnd.Next(days));
                    if (date <= today)
                        await store.AddTransactionAsync(new Transaction { Date = date, Type = TransactionType.Income, Category = "Gifts", Payee = "Tikkie", Amount = 15m * (1 + rnd.Next(3)), Description = "Terugbetaling" });
                }
                month = month.AddMonths(1);
            }
            await store.AddTransactionAsync(new Transaction { Date = today.AddDays(-2), Type = TransactionType.Expense, Category = BudgetCalculator.Uncategorized, Payee = "Unknown shop", Amount = 18.40m });

            foreach (var (cat, limit) in new[] { ("Groceries", 250m), ("Eating out", 70m), ("Going out", 60m), ("Shopping", 80m), ("Transport", 50m) })
                await store.AddAsync(new Budget { Category = cat, MonthlyLimit = limit }, store.Budgets);

            await store.AddAsync(new Goal { Name = "New laptop", TargetAmount = 1200m, SavedAmount = 450m, TargetDate = today.AddMonths(8) }, store.Goals);
            await store.AddAsync(new Goal { Name = "Emergency buffer", TargetAmount = 3000m, SavedAmount = 1100m }, store.Goals);

            foreach (var b in new[]
            {
                new BalanceItem { Name = "ING current account", Kind = BalanceKind.Checking, Amount = 640m },
                new BalanceItem { Name = "Oranje Spaarrekening", Kind = BalanceKind.Savings, Amount = 1850m },
                new BalanceItem { Name = "DUO student loan", Kind = BalanceKind.StudentLoan, Amount = 14500m, InterestRate = 2.33m, MonthlyPayment = 0m },
                new BalanceItem { Name = "Credit card", Kind = BalanceKind.CreditCard, Amount = 320m, InterestRate = 13.9m, MonthlyPayment = 50m },
            })
                await store.AddAsync(b, store.Balances);

            await store.SaveProfileAsync(new ProfileInfo
            {
                Name = "Sam", BirthYear = 2004, Household = "Shared house / student house", Housing = "Renting (student room)",
                Work = "Student with a side job", Goals = "Build a buffer of three months and buy a laptop before my thesis.", ReportMonths = 6
            });

            Check(store.Transactions.Count > 200, $"{store.Transactions.Count} transactions seeded");
            var s = BudgetCalculator.Summarize(store.Transactions, store.Recurring, BudgetPeriod.ForMonth(today.AddMonths(-1)), today);
            Check(s.Income > 1000m && s.TotalExpenses > 500m, $"last month: income {s.Income}, spending {s.TotalExpenses}");
        }

        // ------------------------------------------------------------------ 3. imports

        private static async Task ImportFiles()
        {
            var store = AppState.Store;
            var window = (MainWindow)Application.Current.MainWindow;
            var page = (ImportPage)window.GetPage(PageKey.Import);
            window.Navigate(PageKey.Import, null);
            await Shot("07-import-step1", null);

            // Other bank (column check step)
            var bunq = "\"Date\";\"Interest Date\";\"Amount\";\"Account\";\"Counterparty\";\"Name\";\"Description\"\n" +
                       "\"2026-10-01\";\"2026-10-01\";\"-12,50\";\"NL01BUNQ0000000001\";\"NL02INGB0000000002\";\"Jumbo\";\"Boodschappen\"\n" +
                       "\"2026-10-02\";\"2026-10-02\";\"1.200,00\";\"NL01BUNQ0000000001\";\"NL03RABO0000000003\";\"Werkgever\";\"Salaris\"\n" +
                       "\"2026-10-03\";\"2026-10-03\";\"-4,95\";\"NL01BUNQ0000000001\";\"\";\"Spotify\";\"Premium\"\n";
            page.LoadBytes(Encoding.UTF8.GetBytes(bunq), "bunq-export.csv");
            Check(page.CurrentStep == 2, "unknown bank goes to the column check");
            await Shot("08-import-columns", null);

            int before = store.Transactions.Count;
            foreach (var (name, text, expected) in new[]
            {
                ("ING.csv", ImportSamples.Ing, BankFormat.Ing),
                ("CSV_A_NL00RABO1234567890_EUR.csv", ImportSamples.Rabobank, BankFormat.Rabobank),
                ("TXT261014120000.TAB", ImportSamples.AbnAmro, BankFormat.AbnAmro),
            })
            {
                page.LoadBytes(Encoding.UTF8.GetBytes(text), name);
                Check(page.CurrentStep == 3, $"{expected}: recognised, straight to review");
                var candidates = page.Candidates;
                Check(candidates.Count > 0 && candidates.All(c => c.Status != CandidateStatus.Unreadable), $"{expected}: {candidates.Count} rows read");
                if (expected == BankFormat.Ing)
                {
                    Check(candidates.Any(c => c.RecurringId != null && c.Payee.Contains("Netflix")), "ING: Netflix linked to the programmed item");
                    Check(candidates.Any(c => c.Type == TransactionType.Transfer), "ING: savings transfer recognised");
                    // The user fixes the salary's category: a rule should be learned.
                    var salary = candidates.First(c => c.Payee == "Werkgever BV");
                    salary.Category = "Salary";
                    await Idle();
                    await Shot("09-import-review", null);
                }
                var rows = ImportPlanner.ToTransactions(candidates, null);
                var rules = ImportPlanner.NewRules(candidates, store.Rules);
                await store.SaveImportAsync(new ImportBatch { FileName = name, Format = expected.ToString() }, rows, rules);
            }
            Check(store.Rules.Any(r => r.Pattern == "Werkgever BV" && r.Category == "Salary"), "rule learned from a changed category");
            int imported = store.Transactions.Count - before;
            Check(imported == 6 + 3 + 4, $"all sample rows imported ({imported})");

            // Same ING file again: nothing new.
            page.LoadBytes(Encoding.UTF8.GetBytes(ImportSamples.Ing), "ING.csv");
            Check(page.Candidates.All(c => c.Status == CandidateStatus.AlreadyImported && !c.Include), "re-import: everything already imported");

            // Undo the ABN AMRO import.
            var abn = store.Imports.First(b => b.Format == nameof(BankFormat.AbnAmro));
            int removed = await store.UndoImportAsync(abn);
            Check(removed == 4 && store.Transactions.Count == before + 9, "undo removes exactly that import");

            // Done screen (re-import ABN through the page's own button path is async; show the screen with a fresh import instead).
            page.LoadBytes(Encoding.UTF8.GetBytes(ImportSamples.AbnAmro), "TXT261014120000.TAB");
            page.SimulateImportClick();
            for (int i = 0; i < 40 && page.CurrentStep != 4; i++) await Task.Delay(50);
            Check(page.CurrentStep == 4, "import button saves and shows the done step");
            await Shot("10-import-done", null);
        }

        // ------------------------------------------------------------------ 4. PDF

        private static async Task PdfReport()
        {
            var store = AppState.Store;
            var snap = FinancialSnapshot.Build(store.Transactions, store.Recurring, store.Budgets, store.Goals, store.Balances, store.GetProfile(), DateTime.Today);
            var advice = AdviceEngine.Evaluate(snap);
            Check(snap.HasData && snap.Months.Count == 6, $"snapshot over {snap.Months.Count} months");
            Check(advice.Count >= 4, $"{advice.Count} health-check items");
            foreach (var a in advice) Line($"      [{a.Level}] {a.Title}");

            var pdf = Path.Combine(_out, "financial-profile.pdf");
            await Task.Run(() => ReportService.Create(snap, advice, pdf, store.Recurring));
            var size = new FileInfo(pdf).Length;
            Check(size > 15_000, $"PDF created ({size / 1024} KB)");
            var pages = await Task.Run(() => ReportService.CreatePreviewImages(snap, advice, store.Recurring));
            for (int i = 0; i < pages.Count; i++) File.WriteAllBytes(Path.Combine(_out, $"report-page{i + 1}.png"), pages[i]);
            Check(pages.Count >= 2, $"{pages.Count} PDF pages");
        }

        // ------------------------------------------------------------------ 5. screenshots

        private static async Task ShowWindowAsync()
        {
            var window = new MainWindow { Width = 1280, Height = 800, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, ShowActivated = false };
            Application.Current.MainWindow = window;
            window.Show();
            await Idle(300);
        }

        /// <summary>Isolated pie-chart experiments, to see which setup draws the slices.</summary>
        private static async Task PieDiagnostics()
        {
            async Task Try(string name, bool setBeforeShow, TimeSpan? speed, int wait)
            {
                var pie = new LiveChartsCore.SkiaSharpView.WPF.PieChart { Width = 400, Height = 300, LegendPosition = LiveChartsCore.Measure.LegendPosition.Right };
                if (speed is TimeSpan sp) pie.AnimationsSpeed = sp;
                LiveChartsCore.ISeries[] series =
                {
                    new LiveChartsCore.SkiaSharpView.PieSeries<decimal> { Values = new[] { 300m }, Name = "A" },
                    new LiveChartsCore.SkiaSharpView.PieSeries<decimal> { Values = new[] { 200m }, Name = "B" },
                };
                if (setBeforeShow) pie.Series = series;
                var w = new Window { Content = pie, SizeToContent = SizeToContent.WidthAndHeight, Left = 0, Top = 0, ShowActivated = false, Background = Brushes.White };
                w.Show();
                if (!setBeforeShow) { await Idle(100); pie.Series = series; }
                await Idle(wait);
                SaveVisual(pie, (int)pie.ActualWidth, (int)pie.ActualHeight, Brushes.White, Path.Combine(_out, "screens", $"diag-{name}.png"));
                w.Close();
                Line($"ok    pie diagnostic {name}");
            }
            await Try("a-before-default", true, null, 1500);
            await Try("b-before-1ms", true, TimeSpan.FromMilliseconds(1), 600);
            await Try("c-after-default", false, null, 1500);
            await Try("d-after-1ms", false, TimeSpan.FromMilliseconds(1), 600);
            await Try("e-before-zero", true, TimeSpan.Zero, 600);

            // Our own pie-building code, standalone.
            var summary = BudgetCalculator.Summarize(AppState.Store.Transactions, AppState.Store.Recurring, BudgetPeriod.ForMonth(DateTime.Today.AddMonths(-2)), DateTime.Today);
            Line($"      pie slices: {string.Join(", ", summary.Slices.Select(x => $"{x.Label}={x.Amount}"))}");
            async Task Ours(string name, bool labels, bool tooltip)
            {
                var pie = new LiveChartsCore.SkiaSharpView.WPF.PieChart { Width = 460, Height = 300, LegendPosition = LiveChartsCore.Measure.LegendPosition.Right };
                var w = new Window { Content = pie, SizeToContent = SizeToContent.WidthAndHeight, Left = 0, Top = 0, ShowActivated = false, Background = Brushes.White };
                w.Show();
                await Idle(100);
                Look.FillPie(pie, summary, labels, tooltip);
                await Idle(800);
                SaveVisual(pie, (int)pie.ActualWidth, (int)pie.ActualHeight, Brushes.White, Path.Combine(_out, "screens", $"diag-{name}.png"));
                w.Close();
            }
            await Ours("f-ours", true, true);
            await Ours("g-ours-nolabels", false, true);
            await Ours("h-ours-notooltip", true, false);
            await Ours("i-ours-bare", false, false);

            var dash = (DashboardPage)((MainWindow)Application.Current.MainWindow).GetPage(PageKey.Dashboard);
            var dp = (LiveChartsCore.SkiaSharpView.WPF.PieChart)dash.FindName("Pie");
            Line($"      dashboard pie: {dp.ActualWidth}x{dp.ActualHeight}, loaded {dp.IsLoaded}, visible {dp.IsVisible}, series {dp.Series?.Count()}");
        }

        private static void SaveVisual(Visual root, int w, int h, Brush background, string file)
        {
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawRectangle(background, null, new Rect(0, 0, w, h));
                ctx.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var stream = File.Create(file);
            encoder.Save(stream);
        }

        private static async Task Screenshots()
        {
            await PieDiagnostics();
            var window = (MainWindow)Application.Current.MainWindow;
            var store = AppState.Store;
            AppState.Period = BudgetPeriod.ForMonth(DateTime.Today.AddMonths(-2));

            await Shot("01-dashboard", PageKey.Dashboard);
            await Shot("02-transactions", PageKey.Transactions);
            var someTx = store.Transactions.Where(t => t.Payee != null && t.Date.Month == DateTime.Today.AddMonths(-2).Month).OrderByDescending(t => t.Date).First();
            await Shot("03-transactions-edit", PageKey.Transactions, someTx);
            await Shot("04-programmed", PageKey.Programmed);
            await Shot("05-budgets", PageKey.Budgets);
            await Shot("06-trends", PageKey.Trends);

            var tools = (ToolsPage)window.GetPage(PageKey.Tools);
            window.Navigate(PageKey.Tools, null);
            int n = 0;
            foreach (var id in tools.CalculatorIds.ToList())
            {
                tools.ShowCalculator(id);
                await Shot($"11-tools-{++n:00}-{id}", null);
            }

            await Shot("12-profile", PageKey.Profile);
            await Shot("13-help-shortcut", PageKey.Help, "shortcut");
            await Shot("14-help-ing", PageKey.Help, "bank-ing");
            await Shot("15-settings", PageKey.Settings);

            // Uncategorized shortcut from the dashboard's to-do list
            await Shot("16-transactions-uncategorized", PageKey.Transactions, TransactionsPage.ShowUncategorized);
            AppState.Toast("Example toast message", "Undo", () => { });
            await Shot("17-toast", PageKey.Dashboard);
        }

        private static async Task Shot(string name, PageKey? page, object? argument = null)
        {
            var window = (MainWindow)Application.Current.MainWindow;
            try
            {
                if (page is PageKey p) window.Navigate(p, argument);
                await Idle(450);
                var root = (FrameworkElement)window.Content;
                root.UpdateLayout();
                int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
                if (w < 100 || h < 100) { w = 1384; h = 821; root.Measure(new Size(w, h)); root.Arrange(new Rect(0, 0, w, h)); root.UpdateLayout(); }

                var visual = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    ctx.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
                    ctx.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
                }
                var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.Combine(_out, "screens"));
                using var file = File.Create(Path.Combine(_out, "screens", name + ".png"));
                encoder.Save(file);
                Line($"ok    screenshot {name}");
            }
            catch (Exception ex)
            {
                Fail($"screenshot {name}: {ex}");
            }
        }
    }
}
