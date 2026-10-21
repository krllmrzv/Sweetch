namespace Sweetch.Core;

/// <summary>Which radial wheel an operation lives on.</summary>
public enum WheelKind
{
    Convert,
    Tools,
    Actions,
}

/// <summary>External programs some operations need.</summary>
public enum ExternalTool
{
    Ffmpeg,
    Pandoc,
    LibreOffice,
}

/// <summary>A file or folder the user dropped onto the wheel.</summary>
public sealed record FileItem(string Path)
{
    public bool IsDirectory => Directory.Exists(Path);

    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));

    /// <summary>Normalized format key (e.g. "jpg", "tar.gz"); empty for folders.</summary>
    public string Format => IsDirectory ? "" : Formats.KeyOf(Path);

    /// <summary>File name without the format extension (handles double extensions like .tar.gz).</summary>
    public string BaseName => Formats.BaseNameOf(Path);
}

/// <summary>A value the user is asked for before an operation runs (e.g. trim start/end).</summary>
/// <param name="Choices">When set, the value is picked from these instead of typed.</param>
/// <param name="OwnValue">With <paramref name="Choices"/>: a box next to them for a value of one's own, which wins when filled.</param>
public sealed record ParameterSpec(string Key, string Label, string DefaultValue, string? Hint = null, IReadOnlyList<string>? Choices = null, bool OwnValue = false);

/// <param name="Message">A warning (e.g. some files of a batch failed).</param>
/// <param name="Note">Extra information for the user, e.g. "text copied to the clipboard".</param>
public sealed record OperationResult(IReadOnlyList<string> Outputs, string? Message = null, string? Note = null)
{
    public static OperationResult Of(IEnumerable<string> outputs) => new(outputs.ToList());
}

public sealed class OperationContext
{
    public required ToolLocator Tools { get; init; }

    public IProgress<double> Progress { get; init; } = new Progress<double>();

    public CancellationToken CancellationToken { get; init; }

    /// <summary>Folder for results; null means "next to the source file".</summary>
    public string? OutputDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public Settings Settings { get; init; } = new();

    public string OutputDirFor(string sourcePath)
    {
        if (!string.IsNullOrEmpty(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
            return OutputDirectory;
        }
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sourcePath.TrimEnd('\\', '/')));
        return string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir;
    }

    public string Param(string key, string fallback = "") =>
        Parameters.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    /// <summary>Progress reporter for item <paramref name="index"/> of <paramref name="count"/>.</summary>
    public IProgress<double> SubProgress(int index, int count) =>
        new SubProgressReporter(Progress, index, Math.Max(1, count));

    private sealed class SubProgressReporter(IProgress<double> parent, int index, int count) : IProgress<double>
    {
        public void Report(double value) => parent.Report((index + Math.Clamp(value, 0, 1)) / count);
    }
}

/// <summary>Quality knobs shared by the backends; the app persists these.</summary>
public sealed class Settings
{
    public int JpegQuality { get; set; } = 90;
    public int CompressQuality { get; set; } = 70;
    public int PdfDpi { get; set; } = 300;
    public int VideoCrf { get; set; } = 23;
    public int CompressVideoCrf { get; set; } = 28;
}

public abstract class Operation
{
    protected Operation(string id, WheelKind wheel, string label, string glyph, string? subtitle = null)
    {
        Id = id;
        Wheel = wheel;
        Label = label;
        Glyph = glyph;
        Subtitle = subtitle;
    }

    public string Id { get; }
    public WheelKind Wheel { get; }

    /// <summary>Short text shown on the wheel slice.</summary>
    public string Label { get; }

    /// <summary>Optional second line (e.g. "300 DPI").</summary>
    public string? Subtitle { get; }

    /// <summary>Segoe Fluent / MDL2 icon glyph.</summary>
    public string Glyph { get; }

    public IReadOnlyList<ExternalTool> Requires { get; init; } = [];

    public IReadOnlyList<ParameterSpec> Parameters { get; init; } = [];

    public abstract bool AppliesTo(IReadOnlyList<FileItem> items);

    /// <summary>False when an external program the operation needs is missing.</summary>
    public virtual bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) => Requires.All(tools.Has);

    public abstract Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx);

    public override string ToString() => $"{Wheel}:{Id}";
}
