using System;
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
    /// Excel splits CSV columns using the Windows list separator (';' on Dutch systems, ',' on
    /// US/UK ones) and reads numbers using the Windows decimal separator, so the file is written
    /// with the separators of the given culture - that way it opens as proper columns and numbers.
    /// </summary>
    public static class CsvExportService
    {
        public static string Build(
            IEnumerable<Transaction> transactions,
            IEnumerable<RecurringItem> recurringItems,
            BudgetPeriod period,
            DateTime today,
            CultureInfo culture)
        {
            var sep = culture.TextInfo.ListSeparator;
            if (string.IsNullOrEmpty(sep)) sep = ",";
            var nf = culture.NumberFormat;
            string Num(decimal d) => d.ToString("0.00", nf);
            string Row(params string[] fields) => string.Join(sep, fields.Select(f => Escape(f, sep)));

            var txList = transactions.Where(t => period.Contains(t.Date)).OrderBy(t => t.Date).ToList();
            var recList = recurringItems.ToList();
            var summary = BudgetCalculator.Summarize(txList, recList, period, today);

            var sb = new StringBuilder();

            sb.AppendLine(Row("Budget Summary", period.Label));
            sb.AppendLine(Row("Total Income", Num(summary.Income)));
            sb.AppendLine(Row("Total Expenses", Num(summary.TotalExpenses)));
            sb.AppendLine(Row("Net Balance", Num(summary.Net)));
            sb.AppendLine();

            sb.AppendLine(Row("Date", "Type", "Category", "Amount", "Payee", "Description", "Note"));

            foreach (var t in txList)
            {
                var note = t.Type == TransactionType.Transfer ? "Transfer between own accounts, not counted"
                         : t.RecurringId != null ? "Paid by a programmed item, not counted twice"
                         : "";
                sb.AppendLine(Row(
                    t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    t.Type.ToString(),
                    t.Category,
                    Num(t.Amount),
                    t.Payee ?? "",
                    t.Description,
                    note));
            }

            // Recurring costs are listed with the amount they contribute to this period's totals,
            // so the rows add up to the summary above.
            foreach (var r in recList.OrderBy(r => r.Name))
            {
                var cost = BudgetCalculator.RecurringCost(r, period, today);
                if (cost <= 0) continue;
                int months = BudgetCalculator.MonthsActive(r, period, today);
                var note = months > 1 ? $"{r.Name} - {r.Frequency}, {months} months" : $"{r.Name} - {r.Frequency}";
                sb.AppendLine(Row(
                    r.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.IsIncome ? "Income (Programmed)" : "Expense (Programmed)",
                    r.Category,
                    Num(cost),
                    "",
                    note,
                    ""));
            }

            return sb.ToString();
        }

        public static void Export(string filePath, string csv) =>
            // UTF-8 with BOM so Excel shows characters like € and é correctly.
            File.WriteAllText(filePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Quotes a field if it contains the separator, a quote or a line break.
        internal static string Escape(string? field, string sep)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            if (field.Contains(sep) || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }
    }
}
