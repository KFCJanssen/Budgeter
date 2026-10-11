using System.Windows;
using System.Windows.Input;

namespace BudgetApp.Controls
{
    public partial class InputDialog : Window
    {
        private InputDialog(string title, string prompt, string initial)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            ValueBox.Text = initial;
            Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
        }

        /// <summary>Returns the typed text, or null when cancelled.</summary>
        public static string? Ask(string title, string prompt, string initial = "")
        {
            if (AppState.Unattended) return initial;
            var dialog = new InputDialog(title, prompt, initial) { Owner = Application.Current.MainWindow };
            return dialog.ShowDialog() == true ? dialog.ValueBox.Text : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; DialogResult = true; }
        }
    }
}
