using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BudgetApp.Models;
using BudgetApp.Planning;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data
{
    /// <summary>
    /// All of the user's data, loaded into memory (a personal budget is small) and saved to SQLite on
    /// every change. Pages read the lists and call the methods here; <see cref="Changed"/> tells the
    /// other pages to refresh.
    /// </summary>
    public sealed class BudgetStore : IDisposable
    {
        private readonly BudgetContext _db;

        public string DatabasePath { get; }
        public List<Transaction> Transactions { get; } = new();
        public List<RecurringItem> Recurring { get; } = new();
        public List<Budget> Budgets { get; } = new();
        public List<Goal> Goals { get; } = new();
        public List<CategoryRule> Rules { get; } = new();
        public List<ImportBatch> Imports { get; } = new();
        public List<BalanceItem> Balances { get; } = new();
        private readonly Dictionary<string, Setting> _settings = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised after any successful change.</summary>
        public event Action? Changed;

        private BudgetStore(string dbPath)
        {
            DatabasePath = dbPath;
            _db = new BudgetContext(dbPath);
        }

        /// <summary>Upgrades the database if needed and loads everything.</summary>
        public static BudgetStore Open(string dbPath, string backupFolder)
        {
            SchemaMigrator.Migrate(dbPath, backupFolder);
            var store = new BudgetStore(dbPath);
            store.Load();
            return store;
        }

        private void Load()
        {
            Transactions.AddRange(_db.Transactions.ToList());
            Recurring.AddRange(_db.RecurringItems.ToList().OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));
            Budgets.AddRange(_db.Budgets.ToList().OrderBy(b => b.Category, StringComparer.CurrentCultureIgnoreCase));
            Goals.AddRange(_db.Goals.ToList());
            Rules.AddRange(_db.CategoryRules.ToList().OrderBy(r => r.Pattern, StringComparer.CurrentCultureIgnoreCase));
            Imports.AddRange(_db.ImportBatches.ToList().OrderByDescending(i => i.ImportedAt));
            Balances.AddRange(_db.BalanceItems.ToList());
            foreach (var s in _db.Settings.ToList()) _settings[s.Key] = s;
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Saves pending changes. On failure, undoes them in memory and in the change tracker and rethrows,
        /// so the screen and the database never disagree.
        /// </summary>
        private async Task SaveAsync(Action? undoInMemory = null)
        {
            try
            {
                await _db.SaveChangesAsync();
            }
            catch
            {
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                {
                    switch (entry.State)
                    {
                        case EntityState.Added: entry.State = EntityState.Detached; break;
                        case EntityState.Modified:
                        case EntityState.Deleted:
                            entry.State = EntityState.Unchanged;
                            entry.Reload();
                            break;
                    }
                }
                undoInMemory?.Invoke();
                throw;
            }
            Changed?.Invoke();
        }

        public void NotifyChanged() => Changed?.Invoke();

        // ------------------------------------------------------------------ transactions

        public async Task AddTransactionAsync(Transaction t)
        {
            _db.Transactions.Add(t);
            Transactions.Add(t);
            await SaveAsync(() => Transactions.Remove(t));
        }

        /// <summary>Saves edits already made to a tracked transaction (or several).</summary>
        public Task SaveEditsAsync() => SaveAsync();

        public async Task DeleteTransactionsAsync(IReadOnlyCollection<Transaction> items)
        {
            _db.Transactions.RemoveRange(items);
            var removed = items.ToList();
            foreach (var t in removed) Transactions.Remove(t);
            await SaveAsync(() => Transactions.AddRange(removed));
        }

        // ------------------------------------------------------------------ programmed items

        public async Task AddRecurringAsync(RecurringItem r)
        {
            _db.RecurringItems.Add(r);
            Recurring.Add(r);
            Recurring.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            await SaveAsync(() => Recurring.Remove(r));
        }

        public async Task DeleteRecurringAsync(RecurringItem r)
        {
            // Bank rows linked to it become normal rows again, so they count in the totals.
            var linked = Transactions.Where(t => t.RecurringId == r.Id).ToList();
            foreach (var t in linked) t.RecurringId = null;
            _db.RecurringItems.Remove(r);
            Recurring.Remove(r);
            await SaveAsync(() =>
            {
                Recurring.Add(r);
                foreach (var t in linked) t.RecurringId = r.Id;
            });
        }

        // ------------------------------------------------------------------ budgets, goals, balances, rules

        public async Task AddAsync<T>(T item, List<T> list) where T : class
        {
            _db.Add(item);
            list.Add(item);
            await SaveAsync(() => list.Remove(item));
        }

        public async Task DeleteAsync<T>(T item, List<T> list) where T : class
        {
            _db.Remove(item);
            int index = list.IndexOf(item);
            list.Remove(item);
            await SaveAsync(() => list.Insert(Math.Max(0, Math.Min(index, list.Count)), item));
        }

        // ------------------------------------------------------------------ imports

        /// <summary>Saves an import in one go: the batch record, its transactions and any new rules.</summary>
        public async Task<ImportBatch> SaveImportAsync(ImportBatch batch, List<Transaction> rows, List<CategoryRule> newRules)
        {
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                batch.Count = rows.Count;
                _db.ImportBatches.Add(batch);
                await _db.SaveChangesAsync();
                foreach (var r in rows) r.ImportBatchId = batch.Id;
                _db.Transactions.AddRange(rows);
                _db.CategoryRules.AddRange(newRules);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                    entry.State = EntityState.Detached;
                _db.Entry(batch).State = EntityState.Detached;
                foreach (var r in rows) { _db.Entry(r).State = EntityState.Detached; r.Id = 0; r.ImportBatchId = null; }
                foreach (var r in newRules) { _db.Entry(r).State = EntityState.Detached; r.Id = 0; }
                batch.Id = 0;
                throw;
            }

            Imports.Insert(0, batch);
            Transactions.AddRange(rows);
            Rules.AddRange(newRules);
            Rules.Sort((a, b) => string.Compare(a.Pattern, b.Pattern, StringComparison.CurrentCultureIgnoreCase));
            Changed?.Invoke();
            return batch;
        }

        /// <summary>Removes every transaction from one import. Rules made during it are kept.</summary>
        public async Task<int> UndoImportAsync(ImportBatch batch)
        {
            var rows = Transactions.Where(t => t.ImportBatchId == batch.Id).ToList();
            _db.Transactions.RemoveRange(rows);
            _db.ImportBatches.Remove(batch);
            foreach (var t in rows) Transactions.Remove(t);
            Imports.Remove(batch);
            await SaveAsync(() =>
            {
                Transactions.AddRange(rows);
                Imports.Insert(0, batch);
            });
            return rows.Count;
        }

        // ------------------------------------------------------------------ settings & profile

        public string GetSetting(string key, string fallback = "") =>
            _settings.TryGetValue(key, out var s) ? s.Value : fallback;

        public bool GetBool(string key, bool fallback) =>
            _settings.TryGetValue(key, out var s) ? s.Value == "1" : fallback;

        public async Task SetSettingAsync(string key, string value)
        {
            if (_settings.TryGetValue(key, out var s))
            {
                if (s.Value == value) return;
                s.Value = value;
            }
            else
            {
                s = new Setting { Key = key, Value = value };
                _settings[key] = s;
                _db.Settings.Add(s);
            }
            await _db.SaveChangesAsync();
        }

        public Task SetBoolAsync(string key, bool value) => SetSettingAsync(key, value ? "1" : "0");

        public ProfileInfo GetProfile() => new()
        {
            Name = GetSetting("profile.name"),
            BirthYear = int.TryParse(GetSetting("profile.birthYear"), out var y) ? y : null,
            Household = GetSetting("profile.household"),
            Housing = GetSetting("profile.housing"),
            Work = GetSetting("profile.work"),
            Goals = GetSetting("profile.goals"),
            ReportMonths = int.TryParse(GetSetting("profile.reportMonths"), out var m) ? m : 6
        };

        public async Task SaveProfileAsync(ProfileInfo p)
        {
            await SetSettingAsync("profile.name", p.Name.Trim());
            await SetSettingAsync("profile.birthYear", p.BirthYear?.ToString(CultureInfo.InvariantCulture) ?? "");
            await SetSettingAsync("profile.household", p.Household);
            await SetSettingAsync("profile.housing", p.Housing);
            await SetSettingAsync("profile.work", p.Work);
            await SetSettingAsync("profile.goals", p.Goals.Trim());
            await SetSettingAsync("profile.reportMonths", p.ReportMonths.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>IBANs the user marked as their own, for spotting transfers.</summary>
        public List<string> MyAccounts =>
            GetSetting("import.myAccounts").Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        public bool UseBuiltInRules => GetBool("import.builtInRules", true);

        public void Dispose() => _db.Dispose();
    }
}
