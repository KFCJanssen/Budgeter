using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BudgetApp.Services
{
    /// <summary>
    /// One place for reading and writing euro amounts, so the whole app behaves the same
    /// regardless of the Windows region setting.
    /// </summary>
    public static class Money
    {
        /// <summary>Dutch formatting: € 1.234,56. Used for every amount shown in the UI and exports.</summary>
        public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("nl-NL");

        public static string Format(decimal amount) => amount.ToString("C2", Culture);

        /// <summary>
        /// Parses what people actually type: "12.50", "12,50", "€ 12,50", "1.234,56", "1,234.56", "1 234".
        /// - Both '.' and ',' present: whichever comes last is the decimal separator.
        /// - Only one kind present, appearing once and followed by exactly 3 digits ("1.250"): thousands separator.
        /// - Otherwise a single separator is the decimal separator.
        /// The result is rounded to cents. Negative numbers are rejected (the type field decides the sign).
        /// </summary>
        public static bool TryParse(string? input, out decimal amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var s = input.Trim()
                .Replace("€", "")
                .Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "")
                .Replace(" ", "")
                .Replace("'", "");

            if (s.Length == 0 || !Regex.IsMatch(s, @"^[0-9.,]+$")) return false;

            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');
            char? decimalSep = null;

            if (lastDot >= 0 && lastComma >= 0)
            {
                decimalSep = lastDot > lastComma ? '.' : ',';
            }
            else if (lastDot >= 0 || lastComma >= 0)
            {
                char sep = lastDot >= 0 ? '.' : ',';
                int count = s.Split(sep).Length - 1;
                int digitsAfter = s.Length - s.LastIndexOf(sep) - 1;
                bool looksLikeThousands = digitsAfter == 3 && s.IndexOf(sep) > 0;
                if (count == 1 && !looksLikeThousands) decimalSep = sep;
                // count > 1 ("1.000.000") or "1.250" -> grouping separators, no decimals
            }

            string intPart, fracPart = "";
            if (decimalSep is char d)
            {
                var parts = s.Split(d);
                if (parts.Length != 2) return false;          // e.g. "1,2,3" with ',' as decimal
                intPart = parts[0];
                fracPart = parts[1];
                if (fracPart.Contains('.') || fracPart.Contains(',')) return false;
            }
            else
            {
                intPart = s;
            }

            // Whatever separators remain in the whole-number part must be valid thousands groups: 1.234.567
            var groups = intPart.Split('.', ',');
            if (groups.Length > 1 &&
                (groups[0].Length is < 1 or > 3 || groups.Skip(1).Any(g => g.Length != 3)))
                return false;

            var digits = string.Concat(groups);
            var normalized = (digits.Length == 0 ? "0" : digits) + (fracPart.Length > 0 ? "." + fracPart : "");

            if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                return false;

            amount = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return true;
        }
    }
}
