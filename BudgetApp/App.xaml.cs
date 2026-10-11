using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using BudgetApp.Data;

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

            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            // "BudgetApp.exe --self-test <folder>" checks the whole app without a person (used by GitHub Actions).
            int selfTest = Array.FindIndex(e.Args, a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
            if (selfTest >= 0)
            {
                base.OnStartup(e);
                var output = selfTest + 1 < e.Args.Length ? e.Args[selfTest + 1] : Path.Combine(Path.GetTempPath(), "budgeter-selftest");
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(new Action(async () => Shutdown(await SelfTest.RunAsync(output))));
                return;
            }

            // "BudgetApp.exe --update-test": checks GitHub and installs a newer release without restarting
            // (used by GitHub Actions with a build that pretends to be an old version).
            if (e.Args.Any(a => a.Equals("--update-test", StringComparison.OrdinalIgnoreCase)))
            {
                base.OnStartup(e);
                AppState.Unattended = true;
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        var update = await Services.UpdateService.CheckAsync();
                        if (update == null) { Shutdown(3); return; }
                        await Services.UpdateService.DownloadAndRestartAsync(update, restart: false);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(AppPaths.ExeFolder, "update-test-error.txt"), ex.ToString());
                        Shutdown(4);
                    }
                }));
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;
            base.OnStartup(e);

            BudgetStore store;
            try
            {
                BackupService.AutoBackupIfDue(AppPaths.DatabasePath, AppPaths.BackupFolder, DateTime.Now);
                store = BudgetStore.Open(AppPaths.DatabasePath, AppPaths.BackupFolder);
            }
            catch (NewerDatabaseException ex)
            {
                MessageBox.Show(ex.Message, "Budgeter", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(2);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Budgeter couldn't open your budget file:\n{AppPaths.DatabasePath}\n\n{ex.GetBaseException().Message}\n\n" +
                    $"Nothing was changed. Backups are in:\n{AppPaths.BackupFolder}",
                    "Budgeter", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            AppState.Store = store;
            Services.UpdateService.CleanUpOldVersion();
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppState.Store?.Dispose();
            base.OnExit(e);
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
