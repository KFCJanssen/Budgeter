using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BudgetApp.Services;

namespace BudgetApp.Import
{
    public enum BankFormat
    {
        Ing,
        Rabobank,
        AbnAmro,
        Generic
    }

    /// <summary>
    /// Which column holds what. Column numbers start at 0; -1 means "not in this file".
    /// </summary>
    public sealed class ColumnMapping
    {
        public bool HasHeader { get; set; }
        public int Date { get; set; } = -1;
        /// <summary>.NET date format, e.g. "yyyyMMdd". Null means: detect from the values.</summary>
        public string? DateFormat { get; set; }
        public int Amount { get; set; } = -1;
        /// <summary>Column saying in/out (Af/Bij, D/C, Debit/Credit). -1 when the amount has a sign.</summary>
        public int DebitCredit { get; set; } = -1;
        public int Payee { get; set; } = -1;
        public List<int> Description { get; set; } = new();
        public int CounterAccount { get; set; } = -1;
        public int OwnAccount { get; set; } = -1;
        /// <summary>A unique transaction number from the bank, if the file has one (Rabobank does).</summary>
        public int ExternalId { get; set; } = -1;
        public int Balance { get; set; } = -1;
        /// <summary>Read the shop/person name out of the description (ABN AMRO files have no name column).</summary>
        public bool ExtractPayeeFromDescription { get; set; }

        public bool IsUsable => Date >= 0 && Amount >= 0;

        public ColumnMapping Clone() => new()
        {
            HasHeader = HasHeader, Date = Date, DateFormat = DateFormat, Amount = Amount, DebitCredit = DebitCredit,
            Payee = Payee, Description = new List<int>(Description), CounterAccount = CounterAccount,
            OwnAccount = OwnAccount, ExternalId = ExternalId, Balance = Balance,
            ExtractPayeeFromDescription = ExtractPayeeFromDescription
        };
    }

    public sealed record DetectedFormat(BankFormat Format, string BankName, ColumnMapping Mapping, IReadOnlyList<string> ColumnNames)
    {
        /// <summary>True for known banks: the columns are certain, so the mapping step can be skipped.</summary>
        public bool IsKnownBank => Format != BankFormat.Generic;
    }

    /// <summary>
    /// Recognises the export formats of Dutch banks and guesses the columns of any other bank's CSV.
    ///
    /// Known formats:
    /// - ING: "Datum","Naam / Omschrijving","Rekening","Tegenrekening","Code","Af Bij","Bedrag (EUR)",
    ///   "Mutatiesoort","Mededelingen"[,"Saldo na mutatie","Tag"] (or the English equivalents);
    ///   dates yyyyMMdd, amounts "12,50" with a separate Af/Bij column; comma or semicolon separated.
    /// - Rabobank: "IBAN/BBAN","Munt","BIC","Volgnr","Datum",...,"Bedrag","Saldo na trn",
    ///   "Tegenrekening IBAN/BBAN","Naam tegenpartij",...,"Omschrijving-1..3" (or English);
    ///   dates yyyy-MM-dd, amounts "+442,98" / "-30,00".
    /// - ABN AMRO "TXT" download: tab separated, no header: account, currency, date (yyyyMMdd),
    ///   start balance, end balance, value date, amount ("-25,00"), description.
    /// </summary>
    public static class BankFormatDetector
    {
        public static DetectedFormat Detect(CsvTable table)
        {
            if (table.Rows.Count == 0)
                return new DetectedFormat(BankFormat.Generic, "Unknown", new ColumnMapping(), Array.Empty<string>());

            var first = table.Rows[0];
            var header = first.Select(Norm).ToList();
            int Col(params string[] names)
            {
                foreach (var n in names)
                {
                    int i = header.IndexOf(n);
                    if (i >= 0) return i;
                }
                return -1;
            }

            // ---------- ING ----------
            if (Col("af bij", "debit/credit") >= 0 && Col("bedrag (eur)", "amount (eur)") >= 0)
            {
                var m = new ColumnMapping
                {
                    HasHeader = true,
                    Date = Col("datum", "date"),
                    DateFormat = "yyyyMMdd",
                    Payee = Col("naam / omschrijving", "name / description"),
                    OwnAccount = Col("rekening", "account"),
                    CounterAccount = Col("tegenrekening", "counterparty"),
                    DebitCredit = Col("af bij", "debit/credit"),
                    Amount = Col("bedrag (eur)", "amount (eur)"),
                    Balance = Col("saldo na mutatie", "saldo na muatatie", "resulting balance"),
                };
                int notes = Col("mededelingen", "notifications");
                if (notes >= 0) m.Description.Add(notes);
                if (!LooksLikeDates(table, m.Date, "yyyyMMdd")) m.DateFormat = null;
                return new DetectedFormat(BankFormat.Ing, "ING", m, first);
            }

            // ---------- Rabobank ----------
            if (Col("iban/bban") >= 0 && Col("volgnr", "seq no") >= 0)
            {
                var m = new ColumnMapping
                {
                    HasHeader = true,
                    Date = Col("datum", "date"),
                    DateFormat = "yyyy-MM-dd",
                    Amount = Col("bedrag", "amount"),
                    Payee = Col("naam tegenpartij", "name counterpty"),
                    CounterAccount = Col("tegenrekening iban/bban", "counterpty iban/bban"),
                    OwnAccount = Col("iban/bban"),
                    ExternalId = Col("volgnr", "seq no"),
                    Balance = Col("saldo na trn", "bal after bkng"),
                };
                foreach (var d in new[] { ("omschrijving-1", "description-1"), ("omschrijving-2", "description-2"), ("omschrijving-3", "description-3") })
                {
                    int i = Col(d.Item1, d.Item2);
                    if (i >= 0) m.Description.Add(i);
                }
                if (!LooksLikeDates(table, m.Date, "yyyy-MM-dd")) m.DateFormat = null;
                return new DetectedFormat(BankFormat.Rabobank, "Rabobank", m, first);
            }

            // ---------- ABN AMRO (TXT/TAB, no header) ----------
            if (first.Length >= 8 && Regex.IsMatch(first[1], "^[A-Z]{3}$") && Regex.IsMatch(first[2], @"^\d{8}$")
                && Regex.IsMatch(first[5], @"^\d{8}$") && Money.TryParseSigned(first[6], out _))
            {
                var m = new ColumnMapping
                {
                    HasHeader = false,
                    OwnAccount = 0,
                    Date = 2,
                    DateFormat = "yyyyMMdd",
                    Balance = 4,
                    Amount = 6,
                    Description = new List<int> { 7 },
                    ExtractPayeeFromDescription = true,
                };
                var names = new List<string> { "Account", "Currency", "Date", "Start balance", "End balance", "Value date", "Amount", "Description" };
                while (names.Count < first.Length) names.Add($"Column {names.Count + 1}");
                return new DetectedFormat(BankFormat.AbnAmro, "ABN AMRO", m, names);
            }

            // ---------- Anything else: best guess, the user checks it ----------
            return new DetectedFormat(BankFormat.Generic, "Other bank", GuessMapping(table), ColumnNames(table, LooksLikeHeader(first)));
        }

        public static IReadOnlyList<string> ColumnNames(CsvTable table, bool hasHeader)
        {
            int n = table.ColumnCount;
            var names = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var h = hasHeader && table.Rows.Count > 0 && i < table.Rows[0].Length ? table.Rows[0][i].Trim() : "";
                names.Add(string.IsNullOrEmpty(h) ? $"Column {i + 1}" : h);
            }
            return names;
        }

        /// <summary>Guesses columns from header names, or from the values when there is no header.</summary>
        public static ColumnMapping GuessMapping(CsvTable table)
        {
            var m = new ColumnMapping();
            if (table.Rows.Count == 0) return m;
            m.HasHeader = LooksLikeHeader(table.Rows[0]);
            int cols = table.ColumnCount;

            if (m.HasHeader)
            {
                var header = table.Rows[0].Select(Norm).ToList();
                int Find(params string[] words)
                {
                    foreach (var w in words)
                    {
                        int exact = header.IndexOf(w);
                        if (exact >= 0) return exact;
                    }
                    foreach (var w in words)
                    {
                        int i = header.FindIndex(h => h.Contains(w));
                        if (i >= 0) return i;
                    }
                    return -1;
                }

                m.Date = Find("datum", "date", "boekdatum", "transactiedatum", "transaction date", "booking date", "boekingsdatum");
                m.Amount = Find("bedrag", "amount", "bedrag (eur)", "amount (eur)", "transactiebedrag", "value");
                m.DebitCredit = Find("af bij", "af/bij", "debet/credit", "debit/credit", "credit/debit", "d/c", "bij/af");
                m.Payee = Find("naam tegenpartij", "naam", "name", "tegenpartij", "counterparty name", "counterparty", "begunstigde", "omschrijving tegenpartij", "payee");
                m.CounterAccount = Find("tegenrekening", "counterparty iban", "tegenrekeningnummer", "iban tegenpartij", "counter account");
                if (m.CounterAccount == m.Payee) m.CounterAccount = -1;
                int desc = Find("omschrijving", "description", "mededelingen", "notes", "memo", "betalingskenmerk", "details");
                if (desc >= 0 && desc != m.Payee) m.Description.Add(desc);
                m.Balance = Find("saldo na", "balance", "saldo");
                if (m.Balance == m.Amount) m.Balance = -1;
            }

            // Fill gaps from the data itself.
            int start = m.HasHeader ? 1 : 0;
            var dataRows = table.Rows.Skip(start).Take(50).ToList();
            if (m.Date < 0)
            {
                for (int c = 0; c < cols && m.Date < 0; c++)
                    if (DetectDateFormat(dataRows.Select(r => c < r.Length ? r[c] : "")) != null) m.Date = c;
            }
            if (m.Amount < 0)
            {
                for (int c = 0; c < cols && m.Amount < 0; c++)
                {
                    if (c == m.Date) continue;
                    var vals = dataRows.Select(r => c < r.Length ? r[c] : "").Where(v => v.Length > 0).ToList();
                    if (vals.Count > 0 && vals.All(v => Regex.IsMatch(v, @"^[+\-−]?\s*€?\s*[\d.,]+[,.]\d{2}-?$")))
                        m.Amount = c;
                }
            }
            if (m.Payee < 0 && m.Description.Count == 0)
            {
                // The column with the longest text is probably the description.
                int best = -1; double bestLen = 0;
                for (int c = 0; c < cols; c++)
                {
                    if (c == m.Date || c == m.Amount) continue;
                    double avg = dataRows.Select(r => c < r.Length ? r[c].Length : 0).DefaultIfEmpty(0).Average();
                    bool numeric = dataRows.All(r => c >= r.Length || r[c].Length == 0 || Regex.IsMatch(r[c], @"^[\d.,+\- ]+$"));
                    if (!numeric && avg > bestLen) { bestLen = avg; best = c; }
                }
                if (best >= 0) m.Description.Add(best);
            }
            if (m.Date >= 0)
                m.DateFormat = DetectDateFormat(dataRows.Select(r => m.Date < r.Length ? r[m.Date] : ""));
            return m;
        }

        /// <summary>A first row of mostly text (not dates or amounts) is a header.</summary>
        public static bool LooksLikeHeader(string[] row)
        {
            if (row.Length == 0) return false;
            int texty = row.Count(v =>
                v.Length > 0 && !Money.TryParseSigned(v, out _) && DetectDateFormat(new[] { v }) == null && !Regex.IsMatch(v, @"^[A-Z]{2}\d{2}[A-Z0-9]{8,30}$"));
            return texty >= Math.Max(2, row.Length * 0.6);
        }

        private static readonly string[] DateFormats =
        {
            "yyyyMMdd", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy",
            "yyyy/MM/dd", "dd-MM-yy", "d-M-yy", "MM/dd/yyyy", "M/d/yyyy"
        };

        /// <summary>The first format that reads (almost) every value as a sensible date.</summary>
        public static string? DetectDateFormat(IEnumerable<string> values)
        {
            var list = values.Select(StripTime).Where(v => v.Length > 0).Take(200).ToList();
            if (list.Count == 0) return null;
            foreach (var f in DateFormats)
            {
                int ok = list.Count(v => DateTime.TryParseExact(v, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                                         && d.Year >= 1990 && d.Year <= 2100);
                if (ok >= list.Count * 0.9) return f;
            }
            return null;
        }

        public static bool TryParseDate(string value, string? format, out DateTime date)
        {
            var v = StripTime(value);
            if (format != null && DateTime.TryParseExact(v, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
            foreach (var f in DateFormats)
                if (DateTime.TryParseExact(v, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
            date = default;
            return false;
        }

        private static bool LooksLikeDates(CsvTable table, int col, string format) =>
            col >= 0 && table.Rows.Skip(1).Take(20).Where(r => col < r.Length && r[col].Length > 0)
                .All(r => DateTime.TryParseExact(StripTime(r[col]), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));

        private static string StripTime(string v)
        {
            v = (v ?? "").Trim();
            int space = v.IndexOf(' ');
            if (space > 0 && v.IndexOf(':') > space) v = v[..space];
            int t = v.IndexOf('T');
            if (t == 10 && v.Length > 10) v = v[..10];
            return v;
        }

        internal static string Norm(string s) => Regex.Replace((s ?? "").Trim().Trim('"').ToLowerInvariant(), @"\s+", " ");
    }
}
