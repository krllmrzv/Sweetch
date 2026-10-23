using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sweetch.App.Input;
using Sweetch.Core;
using static Sweetch.Core.Loc;

namespace Sweetch.App;

public enum AppAppearance { System, Dark, Light }

public enum HotkeyPreset
{
    None,
    CtrlAltSpace,
    CtrlShiftSpace,
    CtrlAltF,
    CtrlAltW,
}

/// <summary>User preferences, stored as JSON in %APPDATA%\Sweetch\settings.json.</summary>
public sealed class AppSettings
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sweetch");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public ModifierKey ConvertKey { get; set; } = ModifierKey.Shift;
    public ModifierKey ToolsKey { get; set; } = ModifierKey.Ctrl;
    public ModifierKey ActionsKey { get; set; } = ModifierKey.Alt;
    public HotkeyPreset Hotkey { get; set; } = HotkeyPreset.CtrlAltSpace;

    /// <summary>Null = follow the Windows display language.</summary>
    public Language? Language { get; set; } = Core.Language.Russian;
    public AppAppearance Appearance { get; set; } = AppAppearance.System;

    /// <summary>Calm colours: tinted slices and tiles instead of bright ones.</summary>
    public bool LowContrast { get; set; }

    /// <summary>Empty = save results next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    public bool RevealResult { get; set; }

    /// <summary>The user closed the "install helper programs" strip in Settings.</summary>
    public bool HideMissingTools { get; set; }
    public bool Paused { get; set; }
    public bool FirstRunDone { get; set; }
    public string? LastCopyFolder { get; set; }
    public string? LastMoveFolder { get; set; }

    public Settings Quality { get; set; } = new();

    /// <summary>Operation ids the user removed from the wheels.</summary>
    public List<string> HiddenOperations { get; set; } = [];

    /// <summary>Operation ids that always come first on their wheel, in this order.</summary>
    public List<string> FavoriteOperations { get; set; } = [];

    /// <summary>Slices the user moved to the other side of the wheel than the one they come on.</summary>
    public List<string> FlippedOperations { get; set; } = [];

    /// <summary>The person has turned a wheel to its other page once; until then the wheel shows how.</summary>
    public bool FlipLearned { get; set; }

    /// <summary>On the wheel's other side (scroll to flip): rare ones, unless the user swapped them.</summary>
    public bool IsBackSide(Operation op) => op.Rare != FlippedOperations.Contains(op.Id);

    /// <summary>The last operation run, for "repeat last" (with the values it asked for, e.g. trim times).</summary>
    public string? LastOperationId { get; set; }

    public Dictionary<string, string>? LastParameters { get; set; }

    /// <summary>
    /// Programs (process names) a drag must start in to open a wheel: file managers. Elsewhere a drag is a layer, a
    /// window or a selection, and the program's own cursors would pass for a file drag.
    /// </summary>
    public List<string> DragApps { get; set; } = [.. DefaultDragApps];

    public static readonly string[] DefaultDragApps =
        ["explorer", "totalcmd", "totalcmd64", "doublecmd", "Files", "XYplorer", "dopus", "OneCommander", "FreeCommander", "FarManager", "Far"];

    /// <summary>Look for a new release on GitHub. A new name, so settings saved while updates were off don't keep them off.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>Slices the user put together (e.g. "JPG 70 %"); they appear on the wheels like the built-in ones.</summary>
    public List<Preset> Presets { get; set; } = [];

    /// <summary>Folders whose new files are processed automatically.</summary>
    public List<WatchFolder> WatchFolders { get; set; } = [];

    public WheelKind? WheelFor(ModifierKey key)
    {
        if (key == ModifierKey.None)
            return null;
        if (key == ConvertKey)
            return WheelKind.Convert;
        if (key == ToolsKey)
            return WheelKind.Tools;
        if (key == ActionsKey)
            return WheelKind.Actions;
        return null;
    }

    public ModifierKey KeyFor(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => ConvertKey,
        WheelKind.Tools => ToolsKey,
        _ => ActionsKey,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Broken settings file: start with defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal; settings simply won't persist.
        }
    }

    public static string KeyName(ModifierKey key) => key switch
    {
        ModifierKey.Shift => "Shift",
        ModifierKey.Ctrl => "Ctrl",
        ModifierKey.Alt => "Alt",
        _ => "—",
    };

    public static string WheelName(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => T("Convert", "Конвертация"),
        WheelKind.Tools => T("Tools", "Инструменты"),
        _ => T("Actions", "Действия"),
    };

    public static string HotkeyName(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.CtrlAltSpace => T("Ctrl + Alt + Space", "Ctrl + Alt + Space"),
        HotkeyPreset.CtrlShiftSpace => T("Ctrl + Shift + Space", "Ctrl + Shift + Space"),
        HotkeyPreset.CtrlAltF => "Ctrl + Alt + F",
        HotkeyPreset.CtrlAltW => "Ctrl + Alt + W",
        _ => T("Off", "Выкл."),
    };
}

/// <summary>Source-generated JSON for the settings file (works in the trimmed build without reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
