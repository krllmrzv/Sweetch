using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Sweetch.Core;

namespace Sweetch.App.Shell;

/// <summary>
/// Asks GitHub for the latest release and installs it the way this copy was installed: an installed copy
/// runs the new setup silently; an unpacked ZIP copy has its files replaced by a short script once Sweetch
/// has quit. Both start again with --updated. Downloads are checked against GitHub's SHA-256 of the upload.
/// </summary>
internal sealed class Updater
{
    public const string Repository = "chikirao/Sweetchiki";

    public static Version Current { get; } = UpdateInfo.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(2, 0, 0));

    /// <summary>The setup leaves its uninstaller next to the app; a copy unpacked from the ZIP has none.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public UpdateInfo? Available { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public string? LastError { get; private set; }
    public bool Installing { get; private set; }
    public double Progress { get; private set; }

    /// <summary>Raised on the UI thread whenever anything above changes.</summary>
    public event Action? Changed;

    private DispatcherTimer? _timer;
    private Version? _announced;

    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await ToolInstaller.Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            var latest = UpdateInfo.Parse(json);
            Available = latest is not null && latest.IsNewerThan(Current) ? latest : null;
            LastError = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            LastError = ex.Message;
        }
        LastChecked = DateTime.Now;
        Changed?.Invoke();
        return Available;
    }

    /// <summary>Checks a little after start and then every six hours, while the setting is on; each new version is announced once.</summary>
    public void StartBackgroundChecks(AppSettings settings, Action<UpdateInfo> found)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(6);
            if (!settings.AutoUpdateCheck || Installing)
                return;
            if (await CheckAsync() is { } update && update.Version != _announced)
            {
                _announced = update.Version;
                found(update);
            }
        };
        _timer.Start();
    }

    public async Task InstallAsync(UpdateInfo update)
    {
        var asset = IsInstalled ? update.Installer : update.Zip;
        if (Installing)
            return;
        if (asset is null)
        {
            Open(update.PageUrl); // a release without the expected files: let the person pick
            return;
        }
        Installing = true;
        Progress = 0;
        Changed?.Invoke();
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Sweetch-update-" + update.Tag);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Path.GetFileName(new Uri(asset.Url).AbsolutePath));
            await ToolInstaller.DownloadVerifiedAsync(asset.Url, file, asset.Sha256, new Progress<double>(p =>
            {
                Progress = p;
                Changed?.Invoke();
            }), CancellationToken.None);
            if (IsInstalled)
                Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /update=1") { UseShellExecute = true });
            else
                await Task.Run(() => ReplaceFilesAfterExit(file, dir));
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or ConversionException or Win32Exception)
        {
            LastError = ex.Message;
            Installing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Unpacks the new version, then a hidden PowerShell waits for this process to end, copies it over and starts it.</summary>
    private static void ReplaceFilesAfterExit(string zip, string dir)
    {
        var unpacked = Path.Combine(dir, "files");
        if (Directory.Exists(unpacked))
            Directory.Delete(unpacked, recursive: true);
        ZipFile.ExtractToDirectory(zip, unpacked);
        var script = Path.Combine(dir, "update.ps1");
        // BOM: Windows PowerShell reads a script without one in the ANSI code page, which breaks Cyrillic folder names.
        File.WriteAllText(script, ReplaceScript(Environment.ProcessId, unpacked, AppContext.BaseDirectory.TrimEnd('\\')), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    public static string ReplaceScript(int processId, string from, string to)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        return $$"""
            $ErrorActionPreference = 'Stop'
            while (Get-Process -Id {{processId}} -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
            # Antivirus or Explorer may still hold a file for a moment. If it never lets go, the old version
            # starts again and says the update failed, rather than claiming success.
            $copied = $false
            for ($i = 0; $i -lt 10 -and -not $copied; $i++) {
                try { Copy-Item -Path (Join-Path {{Quote(from)}} '*') -Destination {{Quote(to)}} -Recurse -Force; $copied = $true }
                catch { Start-Sleep -Seconds 1 }
            }
            Start-Process -FilePath (Join-Path {{Quote(to)}} 'Sweetch.exe') -ArgumentList $(if ($copied) { '--updated' } else { '--update-failed' })
            """;
    }

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
