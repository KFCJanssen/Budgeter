using System;
using System.IO;

namespace BudgetApp.Data
{
    /// <summary>
    /// Where Budgeter keeps its files. Everything lives in %APPDATA%\BudgetApp, so it doesn't matter
    /// which copy of the app you start - they all use the same data.
    /// The self-test sets BUDGETER_DATA_DIR to use a throw-away folder instead.
    /// </summary>
    public static class AppPaths
    {
        public static string DataFolder
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("BUDGETER_DATA_DIR");
                var folder = !string.IsNullOrWhiteSpace(overridden)
                    ? overridden
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BudgetApp");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public static string DatabasePath => Path.Combine(DataFolder, "budget.db");

        public static string BackupFolder
        {
            get
            {
                var folder = Path.Combine(DataFolder, "Backups");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>The running .exe (for single-file builds this is the real file, not a temp copy).</summary>
        public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BudgetApp.exe");

        public static string ExeFolder => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;

        /// <summary>
        /// True when running from a Visual Studio / dotnet build folder (bin\Debug or bin\Release) instead of
        /// the single BudgetApp.exe from a release. Such a copy never updates itself.
        /// </summary>
        public static bool IsDevelopmentBuild =>
            File.Exists(Path.Combine(ExeFolder, "BudgetApp.dll")) ||
            ExeFolder.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
