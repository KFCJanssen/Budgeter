using System;
using System.Globalization;
using System.Windows.Data;

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
}
