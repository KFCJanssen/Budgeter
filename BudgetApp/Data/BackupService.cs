using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BudgetApp.Data
{
    /// <summary>
    /// Keeps copies of budget.db in %APPDATA%\BudgetApp\Backups:
    /// - automatically once a day when the app starts (the newest 20 are kept),
    /// - before every database upgrade (kept forever),
    /// - whenever you click "Back up now" (kept forever).
    /// </summary>
    public static class BackupService
    {
        private const string AutoPrefix = "budget_auto_";
        private const int AutoKeep = 20;

        /// <summary>Copies the database if there's no automatic backup from today yet. Call before opening it.</summary>
        public static string? AutoBackupIfDue(string dbPath, string backupFolder, DateTime now)
        {
            if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0) return null;
            var today = now.ToString("yyyy-MM-dd");
            if (Directory.GetFiles(backupFolder, $"{AutoPrefix}{today}*.db").Length > 0) return null;

            var target = Path.Combine(backupFolder, $"{AutoPrefix}{now:yyyy-MM-dd_HHmmss}.db");
            File.Copy(dbPath, target, overwrite: false);

            foreach (var old in Directory.GetFiles(backupFolder, $"{AutoPrefix}*.db")
                         .OrderByDescending(f => f, StringComparer.Ordinal)
                         .Skip(AutoKeep))
            {
                try { File.Delete(old); } catch { /* not important */ }
            }
            return target;
        }

        /// <summary>A consistent copy while the app has the database open (SQLite's online backup).</summary>
        public static string BackupNow(string dbPath, string backupFolder, string label = "manual")
        {
            var target = Path.Combine(backupFolder, $"budget_{label}_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
            using var source = new SqliteConnection($"Data Source={dbPath}");
            using var dest = new SqliteConnection($"Data Source={target};Pooling=False");
            source.Open();
            dest.Open();
            source.BackupDatabase(dest);
            return target;
        }

        /// <summary>
        /// Replaces the database with a backup. The current database is backed up first.
        /// The app must have closed its database connection; it should restart afterwards.
        /// </summary>
        public static void Restore(string backupFile, string dbPath, string backupFolder)
        {
            if (!IsBudgetDatabase(backupFile))
                throw new InvalidOperationException("That file isn't a Budgeter backup.");
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Copy(dbPath, Path.Combine(backupFolder, $"budget_before-restore_{DateTime.Now:yyyy-MM-dd_HHmmss}.db"));
            File.Copy(backupFile, dbPath, overwrite: true);
        }

        public static bool IsBudgetDatabase(string file)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
                conn.Open();
                return SchemaMigrator.TableExists(conn, "Transactions");
            }
            catch
            {
                return false;
            }
        }
    }
}
