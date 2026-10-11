using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BudgetApp.Services;

namespace BudgetApp.Import
{
    /// <summary>One line of a bank file, read but not yet turned into a transaction.</summary>
    public sealed class ImportRow
    {
        /// <summary>Line number in the file (1 = first line), shown when something can't be read.</summary>
        public int RowNumber { get; init; }
        public DateTime Date { get; init; }
        /// <summary>Signed: negative = money out, positive = money in.</summary>
        public decimal Amount { get; init; }
        public string Payee { get; init; } = "";
        public string Description { get; init; } = "";
        public string CounterAccount { get; init; } = "";
        public string OwnAccount { get; init; } = "";
        public string ExternalId { get; init; } = "";
        public string Balance { get; init; } = "";
        /// <summary>Why this line couldn't be read, or null if it's fine.</summary>
        public string? Error { get; init; }
    }

    public static class ImportParser
    {
        private static readonly string[] OutWords = { "af", "d", "debit", "debet", "-", "uit", "out", "dr", "db" };
        private static readonly string[] InWords = { "bij", "c", "credit", "+", "in", "cr" };

        public static List<ImportRow> Parse(CsvTable table, ColumnMapping m)
        {
            var result = new List<ImportRow>();
            if (!m.IsUsable) return result;

            string Get(string[] row, int col) => col >= 0 && col < row.Length ? row[col].Trim() : "";

            for (int i = m.HasHeader ? 1 : 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                int lineNo = i + 1;

                if (row.All(string.IsNullOrWhiteSpace)) continue;

                string? error = null;
                if (!BankFormatDetector.TryParseDate(Get(row, m.Date), m.DateFormat, out var date))
                    error = $"date \"{Get(row, m.Date)}\" not recognised";

                decimal amount = 0;
                var rawAmount = Get(row, m.Amount);
                if (error == null && !Money.TryParseSigned(rawAmount, out amount))
                    error = $"amount \"{rawAmount}\" not recognised";

                if (error == null && m.DebitCredit >= 0)
                {
                    var dc = Get(row, m.DebitCredit).ToLowerInvariant();
                    amount = Math.Abs(amount);
                    if (OutWords.Contains(dc)) amount = -amount;
                    else if (!InWords.Contains(dc)) error = $"in/out value \"{Get(row, m.DebitCredit)}\" not recognised";
                }

                var description = Clean(string.Join(" ", m.Description.Select(c => Get(row, c)).Where(s => s.Length > 0)));
                var payee = Clean(Get(row, m.Payee));
                if (payee.Length == 0 && (m.ExtractPayeeFromDescription || m.Payee < 0))
                    payee = PayeeExtractor.FromDescription(description);

                result.Add(new ImportRow
                {
                    RowNumber = lineNo,
                    Date = date,
                    Amount = amount,
                    Payee = Truncate(payee, 120),
                    Description = Truncate(description, 250),
                    CounterAccount = NormalizeAccount(Get(row, m.CounterAccount)),
                    OwnAccount = NormalizeAccount(Get(row, m.OwnAccount)),
                    ExternalId = Get(row, m.ExternalId),
                    Balance = Get(row, m.Balance),
                    Error = error
                });
            }
            return result;
        }

        public static string NormalizeAccount(string s) => Regex.Replace(s ?? "", @"\s+", "").ToUpperInvariant();

        internal static string Clean(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    }

    /// <summary>
    /// Finds the shop or person in a free-text bank description (ABN AMRO files have no name column).
    /// Handles the SEPA "/NAME/…/" and "Naam: …" styles and card payments ("BEA … Albert Heijn 1234,PAS123").
    /// </summary>
    public static class PayeeExtractor
    {
        public static string FromDescription(string description)
        {
            var d = ImportParser.Clean(description);
            if (d.Length == 0) return "";

            var slash = Regex.Match(d, @"/NAME/([^/]+)/", RegexOptions.IgnoreCase);
            if (slash.Success) return slash.Groups[1].Value.Trim();

            var naam = Regex.Match(d, @"\bNaam:\s*(.+?)(?=\s+(Omschrijving|Kenmerk|IBAN|BIC|Machtiging|Incassant|Betalingskenm|Voor|Referentie)\b:?|$)", RegexOptions.IgnoreCase);
            if (naam.Success) return naam.Groups[1].Value.Trim();

            int pas = d.IndexOf(",PAS", StringComparison.OrdinalIgnoreCase);
            if (pas > 0)
            {
                var before = d[..pas];
                // Drop "BEA", "GEA", "BEA, Apple Pay", "NR:xxxx", dates and times.
                before = Regex.Replace(before, @"^(BEA|GEA|eCom)\b[^A-Za-z0-9]*(Apple Pay|Google Pay|Betaalpas|Debit Mastercard)?", "", RegexOptions.IgnoreCase);
                before = Regex.Replace(before, @"\bNR:\s*\S+", "", RegexOptions.IgnoreCase);
                before = Regex.Replace(before, @"\b\d{2}\.\d{2}\.\d{2,4}(/\d{2}[.:]\d{2})?", "");
                before = Regex.Replace(before, @"\b\d{2}[.:]\d{2}\b", "");
                before = ImportParser.Clean(before.Trim(' ', ',', '-', '/'));
                if (before.Length > 0) return before;
            }

            var text = Regex.Replace(d, @"^(SEPA\s+(Overboeking|iDEAL|Incasso[^:]*|Periodieke overb\.)\s*)", "", RegexOptions.IgnoreCase);
            return text.Length <= 40 ? text : text[..40].TrimEnd();
        }

        /// <summary>
        /// Turns a payee into a rule pattern: "Albert Heijn 1234 MAASTRICHT" -> "Albert Heijn",
        /// so the rule also matches other branches.
        /// </summary>
        public static string ToPattern(string payee)
        {
            var words = ImportParser.Clean(payee).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var kept = new List<string>();
            foreach (var w in words)
            {
                if (kept.Count > 0 && Regex.IsMatch(w, @"\d")) break;
                kept.Add(w);
                if (kept.Count == 4) break;
            }
            var pattern = string.Join(" ", kept).Trim(' ', ',', '.', '*');
            return pattern.Length >= 3 ? pattern : ImportParser.Clean(payee);
        }
    }
}
