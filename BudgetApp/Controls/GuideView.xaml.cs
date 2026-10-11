using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BudgetApp.Help;

namespace BudgetApp.Controls
{
    public partial class GuideView : UserControl
    {
        public GuideView() => InitializeComponent();

        public bool ShowTitle
        {
            get => TitleText.Visibility == Visibility.Visible;
            set => TitleText.Visibility = SummaryText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Show(Guide guide)
        {
            TitleText.Text = guide.Title;
            SummaryText.Text = guide.Summary;
            StepList.ItemsSource = guide.Steps.Select((s, i) => new { Number = i + 1, s.Title, s.Text }).ToList();
            TipList.ItemsSource = guide.Tips;
        }
    }
}
