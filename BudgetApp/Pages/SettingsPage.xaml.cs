using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BudgetApp.Data;
using BudgetApp.Services;
using Microsoft.Win32;

namespace BudgetApp.Pages
{
    public partial class SettingsPage : UserControl, IPage
    {
        private bool _loading;
        private UpdateService.UpdateInfo? _update;

        public SettingsPage() => InitializeComponent();

        public void OnShown(object? argument)
        {
            var store = AppState.Store;
            _loading = true;
            AutoCheck.IsChecked = store.GetBool("updates.autoCheck", true);
            BuiltInRules.IsChecked = store.UseBuiltInRules;
            MyAccounts.Text = string.Join(Environment.NewLine, store.MyAccounts);
            _loading = false;

            VersionInfo.Text = $"You have Budgeter {VersionCheck.Display(UpdateService.CurrentVersion)}.";
            LocationInfo.Text = $"Running from: {AppPaths.ExePath}";
            DevWarning.Visibility = AppPaths.IsDevelopmentBuild ? Visibility.Visible : Visibility.Collapsed;
            if (UpdateStatus.Text.Length == 0)
            {
                var last = store.GetSetting("updates.lastCheck");
                UpdateStatus.Text = DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                    ? $"Last checked {when.ToString("d MMMM, HH:mm", CultureInfo.InvariantCulture)}."
                    : "Not checked yet.";
            }

            DataInfo.Text = $"{store.Transactions.Count} transactions, {store.Recurring.Count} programmed items, {store.Rules.Count} rules, {store.Imports.Count} imports.";
            DataPath.Text = $"Stored in {AppPaths.DatabasePath}";
            RefreshBackupInfo();
        }

        private void RefreshBackupInfo()
        {
            var files = Directory.GetFiles(AppPaths.BackupFolder, "*.db").Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).ToList();
            BackupInfo.Text = files.Count == 0
                ? "No backups yet. Budgeter makes one automatically every day you use it."
                : $"{files.Count} backup{(files.Count == 1 ? "" : "s")}. Newest: {files[0].LastWriteTime.ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}.";
        }

        // ------------------------------------------------------------------ updates

        private async void Check_Click(object sender, RoutedEventArgs e)
        {
            CheckButton.IsEnabled = false;
            UpdateStatus.Text = "Checking…";
            try
            {
                _update = await UpdateService.CheckAsync();
                await AppState.Store.SetSettingAsync("updates.lastCheck", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                await AppState.Store.SetSettingAsync("updates.pendingVersion", _update?.VersionText ?? "");
                UpdateStatus.Text = _update == null
                    ? "You have the newest version."
                    : $"Budgeter {_update.VersionText} is available." + (string.IsNullOrWhiteSpace(_update.Notes) ? "" : $"\nWhat's new: {_update.Notes.Trim()}");
                InstallButton.Visibility = _update != null && !AppPaths.IsDevelopmentBuild ? Visibility.Visible : Visibility.Collapsed;
                (Application.Current.MainWindow as MainWindow)?.ShowUpdate(_update);
            }
            catch (Exception ex)
            {
                UpdateStatus.Text = $"Couldn't reach GitHub ({ex.GetBaseException().Message}). Check your internet connection.";
            }
            finally { CheckButton.IsEnabled = true; }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_update == null) return;
            InstallButton.IsEnabled = false;
            UpdateStatus.Text = "Downloading the update… Budgeter restarts by itself when it's done.";
            try { await UpdateService.DownloadAndRestartAsync(_update); }
            catch (Exception ex)
            {
                InstallButton.IsEnabled = true;
                UpdateStatus.Text = "The update couldn't be installed.";
                Ui.Error("The update couldn't be installed. Use 'Open download page' to download it yourself.", ex);
            }
        }

        private void DownloadPage_Click(object sender, RoutedEventArgs e) => UpdateService.OpenInBrowser(UpdateService.ReleasesPage);
        private void GitHub_Click(object sender, RoutedEventArgs e) => UpdateService.OpenInBrowser("https://github.com/" + UpdateService.Repository);
        private void ShortcutGuide_Click(object sender, RoutedEventArgs e) => AppState.Navigate(PageKey.Help, "shortcut");
        private void BackupGuide_Click(object sender, RoutedEventArgs e) => AppState.Navigate(PageKey.Help, "backups");

        private async void AutoCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try { await AppState.Store.SetBoolAsync("updates.autoCheck", AutoCheck.IsChecked == true); } catch (Exception ex) { Ui.Error("Couldn't save the setting.", ex); }
        }

        // ------------------------------------------------------------------ import options

        private async void BuiltInRules_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try { await AppState.Store.SetBoolAsync("import.builtInRules", BuiltInRules.IsChecked == true); } catch (Exception ex) { Ui.Error("Couldn't save the setting.", ex); }
        }

        private async void SaveAccounts_Click(object sender, RoutedEventArgs e)
        {
            var accounts = MyAccounts.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => Import.ImportParser.NormalizeAccount(a)).Where(a => a.Length >= 8).Distinct().ToList();
            try { await AppState.Store.SetSettingAsync("import.myAccounts", string.Join(",", accounts)); }
            catch (Exception ex) { Ui.Error("Couldn't save.", ex); return; }
            MyAccounts.Text = string.Join(Environment.NewLine, accounts);
            AppState.Toast($"{accounts.Count} account{(accounts.Count == 1 ? "" : "s")} saved");
        }

        // ------------------------------------------------------------------ backups

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = BackupService.BackupNow(AppPaths.DatabasePath, AppPaths.BackupFolder);
                RefreshBackupInfo();
                AppState.Toast($"Backup saved: {Path.GetFileName(file)}", "Show", () => UpdateService.OpenFolder(AppPaths.BackupFolder));
            }
            catch (Exception ex) { Ui.Error("The backup couldn't be made.", ex); }
        }

        private void OpenBackups_Click(object sender, RoutedEventArgs e) => UpdateService.OpenFolder(AppPaths.BackupFolder);

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a Budgeter backup",
                Filter = "Budgeter backup (*.db)|*.db",
                InitialDirectory = AppPaths.BackupFolder
            };
            if (dialog.ShowDialog() != true) return;
            if (!BackupService.IsBudgetDatabase(dialog.FileName))
            {
                Ui.Warn("That file isn't a Budgeter backup.", "Restore");
                return;
            }
            var when = File.GetLastWriteTime(dialog.FileName).ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture);
            if (!Ui.Confirm($"Replace all your current data with the backup from {when}?\n\nYour current data is backed up first, so you can go back. Budgeter will restart.", "Restore backup"))
                return;

            try
            {
                AppState.Store.Dispose();
                BackupService.Restore(dialog.FileName, AppPaths.DatabasePath, AppPaths.BackupFolder);
                Process.Start(new ProcessStartInfo(AppPaths.ExePath) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Ui.Error("The backup couldn't be restored. Please restart Budgeter.", ex);
                Application.Current.Shutdown();
            }
        }
    }
}
