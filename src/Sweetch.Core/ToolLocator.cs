using System.Runtime.InteropServices;
using static Sweetch.Core.Loc;

namespace Sweetch.Core;

/// <summary>Finds ffmpeg, pandoc and LibreOffice next to the app, in PATH or in their default install folders.</summary>
public sealed class ToolLocator
{
    // Shared with the views made by Assuming, so "what if" checks don't search PATH again.
    private readonly Dictionary<ExternalTool, string?> _cache;
    private readonly string _appDir;
    private readonly HashSet<ExternalTool> _assumed = [];
    private bool _onlyAssumed;

    /// <summary>Where the Windows app downloads FFmpeg and Pandoc when they are needed.</summary>
    public static string DownloadFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sweetch", "tools");

    /// <summary>A view that pretends <paramref name="tools"/> are installed: "would this work after downloading X?"</summary>
    public ToolLocator Assuming(IEnumerable<ExternalTool> tools)
    {
        var view = new ToolLocator(_appDir, _cache);
        view._assumed.UnionWith(tools);
        return view;
    }

    /// <summary>A view where exactly <paramref name="tools"/> exist, installed or not: "what does LibreOffice add?"</summary>
    public static ToolLocator Pretending(IEnumerable<ExternalTool> tools)
    {
        var view = new ToolLocator(AppContext.BaseDirectory, []) { _onlyAssumed = true };
        view._assumed.UnionWith(tools);
        return view;
    }

    public ToolLocator(string? appDir = null) : this(appDir ?? AppContext.BaseDirectory, new Dictionary<ExternalTool, string?>())
    {
    }

    private ToolLocator(string appDir, Dictionary<ExternalTool, string?> cache)
    {
        _appDir = appDir;
        _cache = cache;
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public string? Ffmpeg => Find(ExternalTool.Ffmpeg);
    public string? Pandoc => Find(ExternalTool.Pandoc);
    public string? LibreOffice => Find(ExternalTool.LibreOffice);

    public bool Has(ExternalTool tool) => Find(tool) is not null;

    public void Refresh()
    {
        lock (_cache)
            _cache.Clear();
    }

    public string Require(ExternalTool tool) =>
        Find(tool) ?? throw new ConversionException(T($"{DisplayName(tool)} is missing. {InstallHint(tool)}", $"Отсутствует {DisplayName(tool)}. {InstallHint(tool)}"));

    public static string DisplayName(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg => "FFmpeg",
        ExternalTool.Pandoc => "Pandoc",
        ExternalTool.LibreOffice => "LibreOffice",
        _ => tool.ToString(),
    };

    public static string InstallHint(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg => T("Install it with: ", "Установите командой: ") + "winget install Gyan.FFmpeg",
        ExternalTool.Pandoc => T("Install it with: ", "Установите командой: ") + "winget install JohnMacFarlane.Pandoc",
        ExternalTool.LibreOffice => T("Install it with: ", "Установите командой: ") + "winget install TheDocumentFoundation.LibreOffice",
        _ => "",
    };

    public string? Find(ExternalTool tool)
    {
        if (_onlyAssumed)
            return _assumed.Contains(tool) ? "(assumed)" : null;
        lock (_cache)
        {
            if (!_cache.TryGetValue(tool, out var path))
                _cache[tool] = path = Locate(tool);
            return path ?? (_assumed.Contains(tool) ? "(assumed)" : null);
        }
    }

    private string? Locate(ExternalTool tool)
    {
        var exe = tool switch
        {
            ExternalTool.Ffmpeg => "ffmpeg",
            ExternalTool.Pandoc => "pandoc",
            _ => "soffice",
        };

        var candidates = new List<string>();
        var bundledName = IsWindows ? exe + ".exe" : exe;
        candidates.Add(Path.Combine(_appDir, "tools", bundledName));
        candidates.Add(Path.Combine(_appDir, bundledName));
        candidates.Add(Path.Combine(DownloadFolder, bundledName));

        if (tool == ExternalTool.LibreOffice && IsWindows)
        {
            // soffice.com is the console launcher: it waits for the conversion to finish.
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     })
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.com"));
                candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.exe"));
            }
        }

        if (tool == ExternalTool.Pandoc && IsWindows)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "Pandoc", "pandoc.exe"));
        }

        if (tool == ExternalTool.Ffmpeg && IsWindows)
        {
            // winget installs Gyan.FFmpeg into a versioned package folder and adds a link here.
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (IsWindows)
        {
            // Pick up tools installed after the app started (winget updates the registry, not our environment).
            pathVar += Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        }
        var names = IsWindows
            ? (tool == ExternalTool.LibreOffice ? new[] { "soffice.com", "soffice.exe" } : new[] { exe + ".exe" })
            : new[] { exe, tool == ExternalTool.LibreOffice ? "libreoffice" : exe };
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var n in names)
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), n);
                    if (File.Exists(full))
                        return full;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }
}
