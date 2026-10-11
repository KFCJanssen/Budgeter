using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BudgetApp.Data;
using BudgetApp.Pages;
using BudgetApp.Services;

namespace BudgetApp
{
    /// <summary>The window: a sidebar with pages, a page area and a small toast for messages.</summary>
    public partial class MainWindow : Window
    {
        private readonly Dictionary<PageKey, FrameworkElement> _pages = new();
        private readonly Dictionary<PageKey, RadioButton> _nav;
        private PageKey _current;
        private object? _pendingArgument;
        private bool _navigating;
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
        private Action? _toastAction;
        private UpdateService.UpdateInfo? _update;

        public MainWindow()
        {
            InitializeComponent();
            _nav = new Dictionary<PageKey, RadioButton>
            {
                [PageKey.Dashboard] = NavDashboard,
                [PageKey.Transactions] = NavTransactions,
                [PageKey.Programmed] = NavProgrammed,
                [PageKey.Budgets] = NavBudgets,
                [PageKey.Trends] = NavTrends,
                [PageKey.Import] = NavImport,
                [PageKey.Tools] = NavTools,
                [PageKey.Profile] = NavProfile,
                [PageKey.Help] = NavHelp,
                [PageKey.Settings] = NavSettings,
            };

            AppState.Navigator = Navigate;
            AppState.Toaster = ShowToast;
            AppState.Store.Changed += () => Dispatcher.BeginInvoke(RefreshCurrent);
            _toastTimer.Tick += (_, _) => HideToast();

            VersionText.Text = $"Version {VersionCheck.Display(UpdateService.CurrentVersion)}";
            DevBuildBanner.Visibility = AppPaths.IsDevelopmentBuild && !AppState.Unattended ? Visibility.Visible : Visibility.Collapsed;

            Navigate(PageKey.Dashboard, null);
            Loaded += async (_, _) =>
            {
                if (AppState.Unattended) return;
                _update = await UpdateService.CheckIfDueAsync(AppState.Store);
                ShowUpdate(_update);
            };
        }

        public FrameworkElement GetPage(PageKey key)
        {
            if (_pages.TryGetValue(key, out var page)) return page;
            page = key switch
            {
                PageKey.Dashboard => new DashboardPage(),
                PageKey.Transactions => new TransactionsPage(),
                PageKey.Programmed => new ProgrammedPage(),
                PageKey.Budgets => new BudgetsPage(),
                PageKey.Trends => new TrendsPage(),
                PageKey.Import => new ImportPage(),
                PageKey.Tools => new ToolsPage(),
                PageKey.Profile => new ProfilePage(),
                PageKey.Help => new HelpPage(),
                PageKey.Settings => new SettingsPage(),
                _ => throw new ArgumentOutOfRangeException(nameof(key))
            };
            _pages[key] = page;
            return page;
        }

        public void Navigate(PageKey key, object? argument)
        {
            _current = key;
            _pendingArgument = argument;
            var page = GetPage(key);
            PageHost.Content = page;

            _navigating = true;
            _nav[key].IsChecked = true;
            _navigating = false;

            (page as IPage)?.OnShown(argument);
            _pendingArgument = null;
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_navigating || _nav == null) return;
            foreach (var kv in _nav)
                if (ReferenceEquals(kv.Value, sender) && kv.Key != _current) Navigate(kv.Key, null);
        }

        private void RefreshCurrent()
        {
            if (PageHost.Content is IPage page) page.OnShown(_pendingArgument);
        }

        // ------------------------------------------------------------------ toast

        private void ShowToast(string message, string? actionText, Action? action)
        {
            ToastText.Text = message;
            _toastAction = action;
            ToastAction.Content = actionText ?? "";
            ToastAction.Visibility = action != null && actionText != null ? Visibility.Visible : Visibility.Collapsed;
            ToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(action != null ? 10 : 5);
            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            ToastBorder.Visibility = Visibility.Collapsed;
            _toastAction = null;
        }

        private void ToastAction_Click(object sender, RoutedEventArgs e)
        {
            var action = _toastAction;
            HideToast();
            action?.Invoke();
        }

        // ------------------------------------------------------------------ updates

        public void ShowUpdate(UpdateService.UpdateInfo? update)
        {
            _update = update;
            if (update == null)
            {
                UpdateBanner.Visibility = Visibility.Collapsed;
                return;
            }
            UpdateText.Text = $"Budgeter {update.VersionText} is available";
            UpdateButton.Content = AppPaths.IsDevelopmentBuild ? "See how to update" : "Install update";
            UpdateBanner.Visibility = Visibility.Visible;
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_update == null) return;
            if (AppPaths.IsDevelopmentBuild)
            {
                Navigate(PageKey.Help, "shortcut");
                return;
            }
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Downloading…";
            try
            {
                await UpdateService.DownloadAndRestartAsync(_update);
            }
            catch (Exception ex)
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "Install update";
                Ui.Error("The update couldn't be installed. You can download it from GitHub instead (Settings > Updates).", ex);
            }
        }

        private void DevBuildHelp_Click(object sender, RoutedEventArgs e) => Navigate(PageKey.Help, "shortcut");
    }
}
