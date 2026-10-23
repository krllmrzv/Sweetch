using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Sweetch.Core;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Shell;

/// <summary>
/// Downloads FFmpeg or Pandoc into %LOCALAPPDATA%\Sweetch\tools the first time a conversion needs it,
/// so the app stays small (Pandoc alone is 225 MB unpacked). Every download is checked against a SHA-256.
/// </summary>
internal static class ToolInstaller
{
    public static readonly ExternalTool[] Installable = [ExternalTool.Ffmpeg, ExternalTool.Pandoc];

    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.7z";

    // A fixed, tested release; its checksum is pinned here rather than fetched next to the file.
    private const string PandocUrl = "https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-windows-x86_64.zip";
    private const string PandocSha256 = "6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587";
    public static readonly HttpClient Http = CreateHttp();

    public static bool CanInstall(ExternalTool tool) => Installable.Contains(tool);

    /// <summary>Approximate download size for the confirmation question.</summary>
    public static string DownloadSize(ExternalTool tool) => tool == ExternalTool.Ffmpeg ? "34 MB" : "42 MB";

    public static async Task InstallAsync(ExternalTool tool, IProgress<double> progress, CancellationToken ct)
    {
        var (url, sha256, exeName) = tool switch
        {
            ExternalTool.Ffmpeg => (FfmpegUrl, (await Http.GetStringAsync(FfmpegUrl + ".sha256", ct)).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant(), "ffmpeg.exe"),
            ExternalTool.Pandoc => (PandocUrl, PandocSha256, "pandoc.exe"),
            _ => throw new ConversionException(T("This tool is installed separately.", "Этот инструмент устанавливается отдельно.")),
        };

        Directory.CreateDirectory(ToolLocator.DownloadFolder);
        var archive = Path.Combine(Path.GetTempPath(), $"sweetch-{exeName}-{Guid.NewGuid():N}{Path.GetExtension(new Uri(url).AbsolutePath)}");
        try
        {
            await DownloadVerifiedAsync(url, archive, sha256, progress, ct);
            await Task.Run(() => ExtractExe(archive, exeName, ct), ct);
        }
        finally
        {
            OutputPaths.TryDelete(archive);
        }
    }

    /// <summary>Downloads to <paramref name="target"/> and, when a checksum is known, refuses a file that doesn't match it.</summary>
    public static async Task DownloadVerifiedAsync(string url, string target, string? sha256, IProgress<double> progress, CancellationToken ct)
    {
        await DownloadAsync(url, target, progress, ct);
        if (sha256 is null)
            return;
        await using var file = File.OpenRead(target);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        if (actual != sha256)
            throw new ConversionException(T("The download doesn't match the checksum published by its authors.", "Контрольная сумма загрузки не совпадает с опубликованной авторами."));
    }

    private static async Task DownloadAsync(string url, string target, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
                progress.Report(0.9 * done / total); // the rest is unpacking
        }
    }

    /// <summary>Takes just the program out of the archive (the archives also hold other programs and docs).</summary>
    private static void ExtractExe(string archivePath, string exeName, CancellationToken ct)
    {
        var target = Path.Combine(ToolLocator.DownloadFolder, exeName);
        var temp = target + ".part";
        using (var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions()))
        {
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.Key is { } key
                && Path.GetFileName(key.Replace('\\', '/')).Equals(exeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ConversionException(T($"{exeName} is missing from the archive.", $"В архиве отсутствует {exeName}."));
            ct.ThrowIfCancellationRequested();
            entry.WriteToFile(temp, new ExtractionOptions { Overwrite = true });
        }
        File.Move(temp, target, overwrite: true);
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sweetch");
        return client;
    }
}

/// <summary>A wheel slice whose conversion needs a tool that Sweetch downloads on first use.</summary>
internal sealed class NeedsToolOperation : Operation
{
    public NeedsToolOperation(Operation inner, IReadOnlyList<ExternalTool> missing)
        : base(inner.Id, inner.Wheel, inner.Label, inner.Glyph, T("get ", "скачать ") + string.Join(" + ", missing.Select(ToolLocator.DisplayName)))
    {
        Inner = inner;
        Missing = missing;
        Parameters = inner.Parameters;
        Rare = inner.Rare;
    }

    public Operation Inner { get; }

    public IReadOnlyList<ExternalTool> Missing { get; }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => Inner.AppliesTo(items);

    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) => true;

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        throw new NotSupportedException("Install the tools first.");
}
