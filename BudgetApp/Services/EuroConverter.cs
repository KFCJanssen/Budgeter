using System;
using System.Globalization;
using System.Windows.Data;
using BudgetApp.Models;

namespace BudgetApp.Services
{
    /// <summary>
    /// Shows a decimal as euros in the tables (€ 1.234,56), independent of the Windows language.
    /// </summary>
    public sealed class EuroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is decimal d ? Money.Format(d) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Money.TryParse(value as string, out var d) ? d : Binding.DoNothing;
    }

    /// <summary>+€ 12,50 for income, −€ 12,50 for expenses, € 12,50 for transfers.</summary>
    public sealed class TransactionAmountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Transaction t ? t.Type switch
            {
                TransactionType.Income => "+" + Money.Format(t.Amount),
                TransactionType.Expense => "−" + Money.Format(t.Amount),
                _ => Money.Format(t.Amount)
            } : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Short note on where a transaction came from or why it doesn't count.</summary>
    public sealed class TransactionNoteConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Transaction t
                ? t.Type == TransactionType.Transfer ? "Transfer"
                : t.RecurringId != null ? "Programmed"
                : t.ImportBatchId != null || t.ImportKey != null ? "Imported"
                : ""
                : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
