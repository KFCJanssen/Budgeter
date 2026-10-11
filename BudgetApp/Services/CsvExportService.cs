using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BudgetApp.Models;

namespace BudgetApp.Services
{
    /// <summary>
    /// Builds the CSV file used by the "Export to CSV" button.
    /// Written by hand with StreamBuilder/File.WriteAllText (no extra NuGet package needed) -
    /// the format is simple enough that a CSV library like CsvHelper isn't required. If your
    /// data grows more complex later, CsvHelper (NuGet) is a drop-in alternative.
    /// </summary>
    public static class CsvExportService
    {
        public static void Export(
            string filePath,
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringExpense> recurringExpenses,
            decimal totalIncome,
            decimal totalExpenses,
            decimal netBalance)
        {
            var sb = new StringBuilder();

            // ---- Summary block ----
            sb.AppendLine("Budget Summary");
            sb.AppendLine($"Total Income,{totalIncome.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Total Expenses,{totalExpenses.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Net Balance,{netBalance.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine();

            // ---- Line-by-line transaction log ----
            sb.AppendLine("Date,Type,Category,Amount,Description");

            foreach (var t in transactions.OrderBy(t => t.Date))
            {
                sb.AppendLine(string.Join(",",
                    t.Date.ToString("yyyy-MM-dd"),
                    t.Type.ToString(),
                    Escape(t.Category),
                    t.Amount.ToString(CultureInfo.InvariantCulture),
                    Escape(t.Description)));
            }

            // Active recurring expenses are appended to the same log (as their monthly-equivalent
            // amount) so the export is a single, complete record of everything factored into totals.
            foreach (var r in recurringExpenses.Where(r => r.IsActive).OrderBy(r => r.Name))
            {
                sb.AppendLine(string.Join(",",
                    r.StartDate.ToString("yyyy-MM-dd"),
                    "Expense (Recurring)",
                    Escape(r.Category),
                    r.MonthlyAmount.ToString(CultureInfo.InvariantCulture),
                    Escape($"{r.Name} - {r.Frequency}")));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        // Wraps a field in quotes and escapes internal quotes if it contains a comma, quote, or newline.
        private static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
    }
}
