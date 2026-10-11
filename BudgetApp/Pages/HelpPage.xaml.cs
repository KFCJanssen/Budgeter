using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BudgetApp.Help;

namespace BudgetApp.Pages
{
    public partial class HelpPage : UserControl, IPage
    {
        private readonly Dictionary<string, RadioButton> _buttons = new();
        private Guide? _current;

        public HelpPage()
        {
            InitializeComponent();
            foreach (var group in Guides.All.GroupBy(g => g.Group))
            {
                GuideList.Children.Add(new TextBlock
                {
                    Text = group.Key.ToUpperInvariant(),
                    Style = (Style)FindResource("StatLabel"),
                    Margin = new Thickness(12, GuideList.Children.Count == 0 ? 4 : 14, 0, 6)
                });
                foreach (var g in group)
                {
                    var button = new RadioButton { Content = g.Title, GroupName = "Guide", Style = (Style)FindResource("ListChoice") };
                    button.Checked += (_, _) => Show(g);
                    _buttons[g.Id] = button;
                    GuideList.Children.Add(button);
                }
            }
        }

        public void OnShown(object? argument)
        {
            var id = argument as string ?? _current?.Id ?? Guides.All[0].Id;
            if (!_buttons.ContainsKey(id)) id = Guides.All[0].Id;
            _buttons[id].IsChecked = true;
            if (_current?.Id != id) Show(Guides.Find(id)!);
        }

        internal IEnumerable<string> GuideIds => Guides.All.Select(g => g.Id);

        private void Show(Guide g)
        {
            _current = g;
            Guide.Show(g);
            GuideScroll.ScrollToTop();
            GoToButton.Visibility = g.GoTo != null ? Visibility.Visible : Visibility.Collapsed;
            GoToButton.Content = g.GoToText ?? "Open";
        }

        private void GoTo_Click(object sender, RoutedEventArgs e)
        {
            if (_current?.GoTo is PageKey page) AppState.Navigate(page);
        }
    }
}
