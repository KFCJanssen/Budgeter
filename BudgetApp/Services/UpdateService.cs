using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using BudgetApp.Data;

namespace BudgetApp.Services
{
    /// <summary>
    /// Checks GitHub for a newer release and installs it in place: the new BudgetApp.exe is downloaded
    /// next to the running one, then a tiny script waits for Budgeter to close, swaps the files and starts
    /// the new version. Your shortcut keeps working because the file name and folder stay the same.
    /// </summary>
    public static class UpdateService
    {
        public const string Repository = "KFCJanssen/Budgeter";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

        public sealed record UpdateInfo(Version Version, string VersionText, string DownloadUrl, string Notes, string PageUrl);

        public static Version CurrentVersion =>
            VersionCheck.Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Budgeter-update-check");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Checks at most once a day, unless turned off in Settings. Never throws.</summary>
        public static async Task<UpdateInfo?> CheckIfDueAsync(BudgetStore store)
        {
            try
            {
                if (!store.GetBool("updates.autoCheck", true)) return null;
                var last = store.GetSetting("updates.lastCheck");
                if (DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) && (DateTime.Now - when).TotalHours < 20)
                {
                    // Still show a known pending update without asking GitHub again.
                    var pending = store.GetSetting("updates.pendingVersion");
                    if (VersionCheck.IsNewer(pending, CurrentVersion)) return await CheckAsync();
                    return null;
                }
                var info = await CheckAsync();
                await store.SetSettingAsync("updates.lastCheck", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                await store.SetSettingAsync("updates.pendingVersion", info?.VersionText ?? "");
                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Asks GitHub for the latest release. Returns null when you're up to date.</summary>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            if (!VersionCheck.TryParseTag(tag, out var version) || !VersionCheck.IsNewer(tag, CurrentVersion)) return null;

            var asset = root.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), "BudgetApp.exe", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind != JsonValueKind.Object) return null;

            return new UpdateInfo(
                version,
                VersionCheck.Display(version),
                asset.GetProperty("browser_download_url").GetString() ?? "",
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                root.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesPage : ReleasesPage);
        }

        /// <summary>Downloads the new exe and restarts into it. Only for the single-file release build.</summary>
        public static async Task DownloadAndRestartAsync(UpdateInfo update, bool restart = true)
        {
            if (AppPaths.IsDevelopmentBuild)
                throw new InvalidOperationException("This is a development build; it can't replace itself.");

            var exe = AppPaths.ExePath;
            var folder = AppPaths.ExeFolder;
            var newExe = Path.Combine(folder, "BudgetApp.new.exe");
            var oldExe = Path.Combine(folder, "BudgetApp.old.exe");

            // Fail early with a clear message if we can't write next to the exe (e.g. Program Files).
            try { File.WriteAllText(Path.Combine(folder, ".budgeter-write-test"), ""); File.Delete(Path.Combine(folder, ".budgeter-write-test")); }
            catch (Exception ex) { throw new InvalidOperationException($"Budgeter can't write to its folder ({folder}). Move BudgetApp.exe to a folder you own, like C:\\Git Projects\\Budgeter\\publish.", ex); }

            using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(newExe);
                await response.Content.CopyToAsync(file);
            }
            if (new FileInfo(newExe).Length < 1_000_000)
            {
                File.Delete(newExe);
                throw new InvalidOperationException("The downloaded file looks incomplete.");
            }

            // A small script: wait until this process has exited, swap the files, start the new version.
            var script = Path.Combine(Path.GetTempPath(), "budgeter-update.cmd");
            var pid = Environment.ProcessId;
            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" /NH | find \" {pid} \" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"del /f /q \"{oldExe}\" >nul 2>&1\r\n" +
                $"move /y \"{exe}\" \"{oldExe}\" >nul\r\n" +
                $"move /y \"{newExe}\" \"{exe}\" >nul\r\n" +
                (restart ? $"start \"\" \"{exe}\"\r\n" : "") +
                "(goto) 2>nul & del \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Current.Shutdown();
        }

        /// <summary>Removes the previous version left behind by an update.</summary>
        public static void CleanUpOldVersion()
        {
            try
            {
                var old = Path.Combine(AppPaths.ExeFolder, "BudgetApp.old.exe");
                if (File.Exists(old)) File.Delete(old);
            }
            catch { /* still locked or not allowed; try next time */ }
        }

        public static void OpenInBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no browser */ }
        }

        public static void OpenFolder(string folder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
            catch { /* ignore */ }
        }
    }
}
