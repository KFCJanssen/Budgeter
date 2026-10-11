using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BudgetApp.Import
{
    /// <summary>
    /// A bank file read into rows and columns. Handles comma, semicolon and tab separated files,
    /// quoted fields (with separators, quotes and line breaks inside), a UTF-8 byte order mark and
    /// files saved in the older Windows-1252 encoding (some banks still use it for é, ü, €).
    /// </summary>
    public sealed class CsvTable
    {
        public char Delimiter { get; }
        public IReadOnlyList<string[]> Rows { get; }

        /// <summary>Number of columns in the widest row.</summary>
        public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);

        private CsvTable(char delimiter, List<string[]> rows)
        {
            Delimiter = delimiter;
            Rows = rows;
        }

        public static CsvTable FromBytes(byte[] bytes) => Parse(Decode(bytes));

        /// <summary>UTF-8 when the bytes are valid UTF-8 (with or without BOM); otherwise Windows-1252.</summary>
        public static string Decode(byte[] bytes)
        {
            int offset = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) offset = 3;
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                return Windows1252.GetString(bytes, offset, bytes.Length - offset);
            }
        }

        private static Encoding Windows1252
        {
            get
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return Encoding.GetEncoding(1252);
                }
                catch
                {
                    return Encoding.Latin1;
                }
            }
        }

        public static CsvTable Parse(string text)
        {
            var delimiter = DetectDelimiter(text);
            var rows = Split(text, delimiter)
                .Where(r => !(r.Length == 1 && string.IsNullOrWhiteSpace(r[0])))   // blank lines
                .Where(r => !(r.Length > 0 && r[0].StartsWith('#')))                  // comment lines
                .ToList();
            return new CsvTable(delimiter, rows);
        }

        /// <summary>
        /// Picks the separator that splits the first lines into the same, largest number of columns.
        /// </summary>
        internal static char DetectDelimiter(string text)
        {
            var sample = text;
            char best = ',';
            double bestScore = 0;
            foreach (var d in new[] { ',', ';', '\t', '|' })
            {
                var rows = Split(sample, d, maxRows: 12)
                    .Where(r => !(r.Length == 1 && string.IsNullOrWhiteSpace(r[0])))
                    .Where(r => !(r.Length > 0 && r[0].StartsWith('#')))
                    .ToList();
                if (rows.Count == 0) continue;
                var counts = rows.Select(r => r.Length).ToList();
                int mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
                if (mode < 2) continue;
                double consistency = counts.Count(c => c == mode) / (double)counts.Count;
                double score = mode * consistency * consistency;
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        /// <summary>RFC 4180-style splitter: quotes may contain separators, doubled quotes and newlines.</summary>
        internal static List<string[]> Split(string text, char delimiter, int maxRows = int.MaxValue)
        {
            var rows = new List<string[]>();
            var field = new StringBuilder();
            var row = new List<string>();
            bool inQuotes = false;
            bool fieldWasQuoted = false;

            void EndField()
            {
                row.Add(fieldWasQuoted ? field.ToString() : field.ToString().Trim());
                field.Clear();
                fieldWasQuoted = false;
            }

            void EndRow()
            {
                EndField();
                rows.Add(row.ToArray());
                row = new List<string>();
            }

            for (int i = 0; i < text.Length && rows.Count < maxRows; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                if (c == '"' && field.ToString().Trim().Length == 0)
                {
                    field.Clear();
                    inQuotes = true;
                    fieldWasQuoted = true;
                }
                else if (c == delimiter) EndField();
                else if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow();
                }
                else if (c == '\n') EndRow();
                else field.Append(c);
            }

            if (rows.Count < maxRows && (field.Length > 0 || row.Count > 0)) EndRow();
            return rows;
        }
    }
}
