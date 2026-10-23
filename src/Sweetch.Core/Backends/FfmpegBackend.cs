using System.Globalization;
using System.Text.RegularExpressions;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>Audio/video work through the ffmpeg command line.</summary>
public static partial class FfmpegBackend
{
    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"\btime=\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimeRegex();

    /// <summary>Encoder arguments for a target container.</summary>
    public static IReadOnlyList<string> CodecArgs(string format, Settings s, bool hasVideo = true)
    {
        var crf = s.VideoCrf.ToString(CultureInfo.InvariantCulture);
        string[] h264 = ["-c:v", "libx264", "-preset", "medium", "-crf", crf, "-pix_fmt", "yuv420p", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2"];
        return format switch
        {
            "mp4" => [.. h264, "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"],
            // x265 CRF N + 5 looks like x264 CRF N at roughly half the size; hvc1 so Apple players open it.
            "hevc" => ["-c:v", "libx265", "-preset", "medium", "-crf", (s.VideoCrf + 5).ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
                "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-tag:v", "hvc1", "-x265-params", "log-level=error",
                "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"],
            "mov" => [.. h264, "-c:a", "aac", "-b:a", "160k"],
            "mkv" => [.. h264, "-c:a", "aac", "-b:a", "160k"],
            "webm" => ["-c:v", "libvpx-vp9", "-pix_fmt", "yuv420p", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k"],
            "gif" => ["-vf", "fps=12,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4", "-loop", "0"],
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "aac" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "aiff" => ["-vn", "-c:a", "pcm_s16be"],
            "flac" => ["-vn", "-c:a", "flac"],
            "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
            "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
            "wma" => ["-vn", "-c:a", "wmav2", "-b:a", "192k"],
            "alac" => ["-vn", "-c:a", "alac"],
            "wv" => ["-vn", "-c:a", "wavpack"],
            "tta" => ["-vn", "-c:a", "tta"],
            "ac3" => ["-vn", "-c:a", "ac3", "-b:a", "448k"],
            "apng" => ["-an", "-vf", "fps=12,scale='min(640,iw)':-2:flags=lanczos", "-plays", "0", "-f", "apng"],
            "avi" => ["-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-q:a", "3"],
            "wmv" => ["-c:v", "wmv2", "-q:v", "4", "-c:a", "wmav2"],
            _ => [.. h264, "-c:a", "aac", "-b:a", "160k"],
        };
    }

    /// <summary>
    /// Runs ffmpeg with <c>-i input [args] output</c> and maps its time= output to progress. H.264 work goes to
    /// the graphics card when it has an encoder (NVIDIA, Intel or AMD), several times faster; if that fails
    /// for any reason the same job runs again on the CPU.
    /// </summary>
    public static Task RunAsync(
        OperationContext ctx,
        IReadOnlyList<string> inputs,
        IEnumerable<string> args,
        string output,
        IProgress<double> progress,
        IEnumerable<string>? inputArgs = null,
        double? knownDuration = null)
    {
        var ffmpeg = ctx.Tools.Require(ExternalTool.Ffmpeg);
        var encode = args.ToList();
        List<string> Command(string? gpu)
        {
            var all = new List<string> { "-hide_banner", "-nostdin", "-y" };
            if (gpu is not null)
                all.AddRange(["-hwaccel", "auto"]);
            if (inputArgs is not null)
                all.AddRange(inputArgs);
            foreach (var i in inputs)
            {
                all.Add("-i");
                all.Add(i);
            }
            all.AddRange(gpu is null ? encode : OnGpu(encode, gpu));
            all.Add(output);
            return all;
        }

        double total = 0;
        var noAudio = false;
        return RunWithHintsAsync();

        async Task RunWithHintsAsync()
        {
            try
            {
                // "Compress" asks x264 for its slow preset: smaller files than any card encoder makes, so it stays on the CPU.
                var codec = encode.Contains("libx264") ? "h264" : encode.Contains("libx265") ? "hevc" : null;
                var gpu = codec is not null && !encode.Contains("slow") ? await GpuEncoderAsync(ffmpeg, codec, ctx.CancellationToken).ConfigureAwait(false) : null;
                if (gpu is not null)
                {
                    try
                    {
                        total = knownDuration ?? 0;
                        await ProcessRunner.RunAsync(ffmpeg, Command(gpu), ctx.CancellationToken, OnLine).ConfigureAwait(false);
                        return;
                    }
                    catch (ConversionException) when (!noAudio)
                    {
                        // Drivers refuse some sizes and formats the CPU encoder takes.
                    }
                }
                total = knownDuration ?? 0;
                await ProcessRunner.RunAsync(ffmpeg, Command(null), ctx.CancellationToken, OnLine).ConfigureAwait(false);
            }
            catch (ConversionException) when (noAudio)
            {
                throw new ConversionException(encode.Contains("-c:s")
                    ? T("The video has no text subtitles.", "В видео нет текстовых субтитров.")
                    : T("The file has no audio track.", "В файле нет звуковой дорожки."));
            }
        }

        void OnLine(string line)
        {
            if (line.Contains("does not contain any stream", StringComparison.Ordinal)
                || line.Contains("matches no streams", StringComparison.Ordinal))
                noAudio = true;
            if (knownDuration is null && DurationRegex().Match(line) is { Success: true } d)
                total += Seconds(d); // several inputs (concat) add up
            else if (total > 0 && TimeRegex().Match(line) is { Success: true } t)
                progress.Report(Math.Clamp(Seconds(t) / total, 0, 0.99));
        }
    }

    private static readonly Dictionary<string, string?> GpuEncoders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The first hardware encoder for <paramref name="codec"/> (h264, hevc) that really works here, tried once per
    /// ffmpeg: a build lists all three whatever the card, so each one encodes a few blank frames to prove it.
    /// </summary>
    private static async Task<string?> GpuEncoderAsync(string ffmpeg, string codec, CancellationToken ct)
    {
        var key = ffmpeg + "|" + codec;
        lock (GpuEncoders)
        {
            if (GpuEncoders.TryGetValue(key, out var known))
                return known;
        }
        string? found = null;
        foreach (var encoder in new[] { codec + "_nvenc", codec + "_qsv", codec + "_amf" })
        {
            try
            {
                await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-f", "lavfi", "-i", "color=black:s=320x240:d=0.2", "-frames:v", "3", "-c:v", encoder, "-f", "null", "-"], ct).ConfigureAwait(false);
                found = encoder;
                break;
            }
            catch (ConversionException)
            {
            }
        }
        lock (GpuEncoders)
            GpuEncoders[key] = found;
        return found;
    }

    /// <summary>
    /// Swaps <c>-c:v libx264 -preset … -crf N</c> for the hardware encoder at about the same quality: measured
    /// on NVENC, quality level N + 4 matches x264's CRF N in SSIM and file size. x265's CRF is already on the
    /// card encoders' HEVC scale, so it passes as is.
    /// </summary>
    public static List<string> OnGpu(List<string> args, string encoder)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count ? args[i + 1] : "";
            switch (args[i])
            {
                case "-c:v" when next is "libx264" or "libx265":
                    result.AddRange(["-c:v", encoder]);
                    i++;
                    break;
                case "-x265-params":
                    i++;
                    break;
                case "-preset":
                    i++; // x264 speed presets; each encoder gets its own below
                    break;
                case "-crf":
                    var q = (int.Parse(next, CultureInfo.InvariantCulture) + (encoder.StartsWith("h264", StringComparison.Ordinal) ? 4 : 0)).ToString(CultureInfo.InvariantCulture);
                    result.AddRange(encoder switch
                    {
                        _ when encoder.EndsWith("_nvenc", StringComparison.Ordinal) => ["-preset", "p5", "-rc", "vbr", "-cq", q, "-b:v", "0"],
                        _ when encoder.EndsWith("_qsv", StringComparison.Ordinal) => ["-preset", "medium", "-global_quality", q],
                        _ => ["-quality", "balanced", "-rc", "cqp", "-qp_i", q, "-qp_p", q, "-qp_b", q],
                    });
                    i++;
                    break;
                case "-pix_fmt" when encoder.EndsWith("_qsv", StringComparison.Ordinal):
                    result.AddRange(["-pix_fmt", "nv12"]); // Quick Sync takes NV12, the same 4:2:0 layout
                    i++;
                    break;
                default:
                    result.Add(args[i]);
                    break;
            }
        }
        return result;
    }

    private static double Seconds(Match m) =>
        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
        + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
        + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

    /// <summary>HEVC and ALAC are codecs, not containers: their files are .mp4 and .m4a.</summary>
    public static string Extension(string format) => format switch { "hevc" => "mp4", "alac" => "m4a", _ => format };

    public static Task<string> ConvertAsync(FileItem item, string format, OperationContext ctx, IProgress<double> progress) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, Extension(format), tmp =>
            RunAsync(ctx, [item.Path], CodecArgs(format, ctx.Settings), tmp, progress));

    /// <summary>Re-encodes in the source container with extra filter/encoder args; output gets a suffix.</summary>
    public static Task<string> TransformAsync(
        FileItem item,
        string suffix,
        OperationContext ctx,
        IProgress<double> progress,
        IEnumerable<string> args,
        string? outputFormat = null,
        IEnumerable<string>? inputArgs = null,
        double? duration = null)
    {
        var format = outputFormat ?? SafeContainer(item.Format);
        return OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({suffix})", format, tmp =>
            RunAsync(ctx, [item.Path], args, tmp, progress, inputArgs, duration));
    }

    /// <summary>Containers we can re-encode into; legacy ones become MP4.</summary>
    public static string SafeContainer(string format) => format switch
    {
        "avi" or "wmv" or "flv" or "mpg" or "mpeg" or "3gp" or "ts" or "mts"
            or "ogv" or "vob" or "m2ts" or "mxf" or "divx" or "asf" or "rm" or "f4v" => "mp4",
        "ape" or "wv" or "tta" or "dsf" or "dff" or "w64" => "flac", // lossless stays lossless
        "wma" or "amr" or "dts" or "ac3" or "eac3" or "mka" or "caf" or "au" or "voc" => "mp3",
        _ => format,
    };

    /// <summary>Parses "90", "1:30", "01:02:03.5" into seconds.</summary>
    public static double ParseTime(string text)
    {
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0)
            throw new ConversionException(T("Missing time.", "Время не указано."));
        var parts = text.Split(':');
        double seconds = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
                throw new ConversionException(T($"Invalid time: {text}", $"Некорректное время: {text}"));
            seconds = seconds * 60 + v;
        }
        return seconds;
    }

    public static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
