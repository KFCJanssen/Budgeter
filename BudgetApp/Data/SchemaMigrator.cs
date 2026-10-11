using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BudgetApp.Data
{
    /// <summary>Thrown when the database was upgraded by a newer Budgeter than the one running.</summary>
    public sealed class NewerDatabaseException : Exception
    {
        public NewerDatabaseException(int found, int supported)
            : base($"This budget file was saved by a newer version of Budgeter (data version {found}; this app understands up to {supported}). " +
                   "Please start the newest BudgetApp.exe - see Help > Updating Budgeter.") { }
    }

    /// <summary>
    /// Creates the database or upgrades an older one, step by step, without losing data.
    /// The data version is kept in SQLite's built-in "user_version" number:
    ///   0 = brand new file, or a file made by Budgeter 1.0/1.1 (which never set it)
    ///   1 = the 1.0 tables (Transactions, RecurringExpenses)
    ///   2 = Budgeter 1.2: payee/import columns, recurring income, budgets, goals, rules, imports, balances, settings
    /// Each upgrade runs inside one SQLite transaction: it either fully happens or not at all.
    /// A copy of the file is made before any upgrade.
    /// </summary>
    public static class SchemaMigrator
    {
        public const int LatestVersion = 2;

        /// <summary>Returns the path of the backup made before upgrading, or null if no upgrade was needed.</summary>
        public static string? Migrate(string dbPath, string backupFolder)
        {
            bool existed = File.Exists(dbPath) && new FileInfo(dbPath).Length > 0;
            string? backup = null;

            using (var probe = Open(dbPath))
            {
                int version = GetVersion(probe);
                if (version > LatestVersion) throw new NewerDatabaseException(version, LatestVersion);
                if (version == LatestVersion) return null;

                if (existed && TableExists(probe, "Transactions"))
                {
                    probe.Close();
                    SqliteConnection.ClearAllPools();
                    backup = Path.Combine(backupFolder, $"budget_before-upgrade-to-v{LatestVersion}_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
                    File.Copy(dbPath, backup, overwrite: false);
                }
            }

            using var conn = Open(dbPath);
            int current = GetVersion(conn);
            while (current < LatestVersion)
            {
                int next = current + 1;
                // Work out the statements first: the checks inside Steps() can't run once a transaction is open.
                var statements = Steps(conn, next).ToList();
                using var tx = conn.BeginTransaction();
                foreach (var sql in statements)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                using (var setVersion = conn.CreateCommand())
                {
                    setVersion.Transaction = tx;
                    setVersion.CommandText = $"PRAGMA user_version = {next};";
                    setVersion.ExecuteNonQuery();
                }
                tx.Commit();
                current = next;
            }
            return backup;
        }

        private static IEnumerable<string> Steps(SqliteConnection conn, int toVersion)
        {
            switch (toVersion)
            {
                case 1:
                    // Exactly the tables Budgeter 1.0 created (via EF Core EnsureCreated).
                    yield return @"CREATE TABLE IF NOT EXISTS ""Transactions"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Transactions"" PRIMARY KEY AUTOINCREMENT,
                        ""Date"" TEXT NOT NULL,
                        ""Type"" INTEGER NOT NULL,
                        ""Category"" TEXT NOT NULL,
                        ""Amount"" TEXT NOT NULL,
                        ""Description"" TEXT NOT NULL);";
                    yield return @"CREATE TABLE IF NOT EXISTS ""RecurringExpenses"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_RecurringExpenses"" PRIMARY KEY AUTOINCREMENT,
                        ""Name"" TEXT NOT NULL,
                        ""Category"" TEXT NOT NULL,
                        ""Amount"" TEXT NOT NULL,
                        ""Frequency"" INTEGER NOT NULL,
                        ""StartDate"" TEXT NOT NULL,
                        ""IsActive"" INTEGER NOT NULL);";
                    break;

                case 2:
                    foreach (var (col, type) in new[] { ("Payee", "TEXT NULL"), ("CounterAccount", "TEXT NULL"), ("ImportKey", "TEXT NULL"),
                                                        ("ImportBatchId", "INTEGER NULL"), ("RecurringId", "INTEGER NULL") })
                        if (!ColumnExists(conn, "Transactions", col))
                            yield return $@"ALTER TABLE ""Transactions"" ADD COLUMN ""{col}"" {type};";

                    // Everything from 1.0 was a cost, so existing rows become Expense (1).
                    if (!ColumnExists(conn, "RecurringExpenses", "Type"))
                        yield return @"ALTER TABLE ""RecurringExpenses"" ADD COLUMN ""Type"" INTEGER NOT NULL DEFAULT 1;";

                    yield return @"CREATE INDEX IF NOT EXISTS ""IX_Transactions_ImportKey"" ON ""Transactions"" (""ImportKey"");";
                    yield return @"CREATE INDEX IF NOT EXISTS ""IX_Transactions_Date"" ON ""Transactions"" (""Date"");";

                    yield return @"CREATE TABLE IF NOT EXISTS ""Budgets"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Budgets"" PRIMARY KEY AUTOINCREMENT,
                        ""Category"" TEXT NOT NULL,
                        ""MonthlyLimit"" TEXT NOT NULL);";
                    yield return @"CREATE TABLE IF NOT EXISTS ""Goals"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Goals"" PRIMARY KEY AUTOINCREMENT,
                        ""Name"" TEXT NOT NULL,
                        ""TargetAmount"" TEXT NOT NULL,
                        ""SavedAmount"" TEXT NOT NULL,
                        ""TargetDate"" TEXT NULL,
                        ""CreatedAt"" TEXT NOT NULL);";
                    yield return @"CREATE TABLE IF NOT EXISTS ""CategoryRules"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CategoryRules"" PRIMARY KEY AUTOINCREMENT,
                        ""Pattern"" TEXT NOT NULL,
                        ""Field"" INTEGER NOT NULL,
                        ""Category"" TEXT NOT NULL,
                        ""SetType"" INTEGER NULL,
                        ""CreatedAt"" TEXT NOT NULL);";
                    yield return @"CREATE TABLE IF NOT EXISTS ""ImportBatches"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ImportBatches"" PRIMARY KEY AUTOINCREMENT,
                        ""ImportedAt"" TEXT NOT NULL,
                        ""FileName"" TEXT NOT NULL,
                        ""Format"" TEXT NOT NULL,
                        ""Count"" INTEGER NOT NULL);";
                    yield return @"CREATE TABLE IF NOT EXISTS ""BalanceItems"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BalanceItems"" PRIMARY KEY AUTOINCREMENT,
                        ""Name"" TEXT NOT NULL,
                        ""Kind"" INTEGER NOT NULL,
                        ""Amount"" TEXT NOT NULL,
                        ""InterestRate"" TEXT NULL,
                        ""MonthlyPayment"" TEXT NULL,
                        ""UpdatedAt"" TEXT NOT NULL);";
                    yield return @"CREATE TABLE IF NOT EXISTS ""Settings"" (
                        ""Key"" TEXT NOT NULL CONSTRAINT ""PK_Settings"" PRIMARY KEY,
                        ""Value"" TEXT NOT NULL);";
                    break;

                default:
                    throw new InvalidOperationException($"No upgrade step to data version {toVersion}.");
            }
        }

        public static int GetVersion(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static bool TableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
            cmd.Parameters.AddWithValue("$name", table);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public static bool ColumnExists(SqliteConnection conn, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<string> Columns(SqliteConnection conn, string table)
        {
            var list = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(reader.GetString(1));
            return list;
        }

        public static SqliteConnection Open(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            return conn;
        }
    }
}
