using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace BudgetApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // WPF formats bound dates/numbers as en-US unless told otherwise. Use British English:
            // English text with day-first dates (11/10/2026), which matches how dates are written in NL.
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-GB")));

            // Show unexpected errors instead of silently closing the app.
            DispatcherUnhandledException += OnUnhandledException;

            base.OnStartup(e);
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Something went wrong:\n{e.Exception.GetBaseException().Message}\n\nYour saved data is safe.",
                "Budgeter", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
