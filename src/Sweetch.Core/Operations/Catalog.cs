using System.Globalization;
using Sweetch.Core.Backends;
using static Sweetch.Core.Operations.Helpers;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Operations;

/// <summary>Every conversion and tool the wheels can offer. The app adds its own file actions.</summary>
public static class Catalog
{
    public static IReadOnlyList<Operation> Build()
    {
        var ops = new List<Operation>();
        AddImages(ops);
        AddAudio(ops);
        AddVideo(ops);
        AddPdf(ops);
        AddDocuments(ops);
        AddSlides(ops);
        AddSpreadsheets(ops);
        AddSubtitles(ops);
        AddArchives(ops);
        return ops;
    }

    /// <summary>Operations for a wheel that apply to the dropped items and whose tools are installed.</summary>
    public static IReadOnlyList<Operation> For(IEnumerable<Operation> all, WheelKind wheel, IReadOnlyList<FileItem> items, ToolLocator tools) =>
        all.Where(o => o.Wheel == wheel && o.AppliesTo(items) && o.IsAvailable(tools, items)).ToList();

    // ---------------------------------------------------------------- images

    private static void AddImages(List<Operation> ops)
    {
        foreach (var target in Formats.ImageOutputs)
        {
            ops.Add(new PerFileOperation($"image.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Photo, Formats.ImageInputs,
                async (item, ctx, _) => [await Produce(item, ctx, target, tmp => ImageBackend.ConvertAsync(item.Path, tmp, target, ctx.Settings, ctx.CancellationToken))],
                targetFormat: target, subtitle: target == "pdf" ? T("document", "документ") : null));
        }
        ops.Add(new PerFileOperation("gif.to.mp4", WheelKind.Convert, "MP4", Glyphs.Video, ["gif"],
            async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, "mp4", ctx, p)], subtitle: T("video", "видео"))
        { Requires = [ExternalTool.Ffmpeg] });
        ops.Add(new PerFileOperation("gif.to.webm", WheelKind.Convert, "WEBM", Glyphs.Video, ["gif"],
            async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, "webm", ctx, p)], subtitle: T("video", "видео"))
        { Requires = [ExternalTool.Ffmpeg] });

        ImageTool(ops, "image.compress", T("Compress", "Сжать"), Glyphs.Shrink, T("compressed", "сжато"),
            (item, ctx, tmp) => ImageBackend.CompressAsync(item.Path, tmp, ctx.Settings.CompressQuality, ctx.CancellationToken));
        ImageEdit(ops, "image.half", T("Shrink", "Уменьшить"), Glyphs.Shrink, "50 %", T("smaller", "уменьшено"), img => ImageBackend.ResizePercent(img, 50));
        ImageEdit(ops, "image.max1920", T("Max 1920 px", "Макс. 1920 px"), Glyphs.Shrink, T("for web", "для интернета"), "1920", img => ImageBackend.ResizeMax(img, 1920));
        ImageEdit(ops, "image.rotate.right", T("Rotate", "Повернуть"), Glyphs.Rotate, T("right", "вправо"), T("rotated", "повёрнуто"), img => img.Rotate(90));
        ImageEdit(ops, "image.rotate.left", T("Rotate", "Повернуть"), Glyphs.Rotate, T("left", "влево"), T("rotated", "повёрнуто"), img => img.Rotate(-90));
        ImageEdit(ops, "image.flip", T("Mirror", "Отразить"), Glyphs.Flip, null, T("mirrored", "отражено"), img => img.Flop());
        ImageEdit(ops, "image.square", T("Square", "Квадрат"), Glyphs.Crop, T("crop", "обрезка"), T("square", "квадрат"), ImageBackend.CropSquare);
        ImageEdit(ops, "image.grayscale", T("Grayscale", "Оттенки серого"), Glyphs.Color, null, T("grayscale", "оттенки серого"), ImageBackend.Grayscale);
        ops.Add(new PerFileOperation("image.relight", WheelKind.Tools, T("Light & color", "Свет и цвет"), Glyphs.Brightness, Formats.ImageInputs,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("edited", "обработано")})",
                ImageBackend.OutputFormatFor(item.Format), tmp => ImageBackend.FilterAsync(item.Path, tmp, ImageBackend.OutputFormatFor(item.Format),
                    Relight.Filter(ctx.Parameters), ctx.Settings.JpegQuality, ctx))],
            subtitle: T("exposure, warmth", "экспозиция, теплота"))
        { Requires = [ExternalTool.Ffmpeg], Parameters = Relight.Parameters() });
        ImageEdit(ops, "image.strip", T("No metadata", "Без метаданных"), Glyphs.Clear, "GPS, EXIF", T("no metadata", "без метаданных"), img => img.Strip());
        ops.Add(new BatchOperation("image.merge.pdf", WheelKind.Tools, T("Merge to PDF", "Объединить в PDF"), Glyphs.Merge,
            BatchOperation.AllOf(Formats.ImageInputs, min: 2),
            async (items, ctx) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(items[0].Path), items[0].BaseName, "pdf",
                tmp => ImageBackend.ImagesToPdfAsync(items.Select(i => i.Path).OrderBy(p => p, NaturalComparer.Instance).ToList(), tmp, ctx.Settings.JpegQuality, ctx.CancellationToken))]));
    }

    private static void ImageTool(List<Operation> ops, string id, string label, string glyph, string suffix, Func<FileItem, OperationContext, string, Task> run, string? subtitle = null) =>
        ops.Add(new PerFileOperation(id, WheelKind.Tools, label, glyph, Formats.ImageInputs,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({suffix})",
                ImageBackend.OutputFormatFor(item.Format), tmp => run(item, ctx, tmp))], subtitle: subtitle));

    private static void ImageEdit(List<Operation> ops, string id, string label, string glyph, string? subtitle, string suffix, Action<ImageMagick.MagickImage> edit) =>
        ImageTool(ops, id, label, glyph, suffix, (item, ctx, tmp) => ImageBackend.EditAsync(item.Path, tmp, edit, ctx.Settings.JpegQuality, ctx.CancellationToken), subtitle);

    // ---------------------------------------------------------------- audio

    private static void AddAudio(List<Operation> ops)
    {
        foreach (var target in Formats.AudioOutputs)
        {
            ops.Add(new PerFileOperation($"audio.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Audio, Formats.Audio,
                async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, target, ctx, p)], targetFormat: target)
            { Requires = [ExternalTool.Ffmpeg] });
        }

        Ffmpeg(ops, "audio.compress", T("Compress", "Сжать"), Glyphs.Shrink, Formats.Audio, T("compressed", "сжато"),
            (item, ctx) => ["-vn", "-c:a", "libmp3lame", "-b:a", "128k"], outputFormat: _ => "mp3", subtitle: "MP3 128k");
        Ffmpeg(ops, "audio.normalize", T("Normalize", "Нормализовать"), Glyphs.Equalizer, Formats.Audio, T("normalized", "нормализовано"),
            (item, ctx) => ["-af", "loudnorm=I=-16:TP=-1.5:LRA=11", .. FfmpegBackend.CodecArgs(FfmpegBackend.SafeContainer(item.Format), ctx.Settings)],
            subtitle: T("loudness", "громкость"));
        ops.Add(Trim("audio.trim", Formats.Audio));
        ops.Add(new BatchOperation("audio.concat", WheelKind.Tools, T("Join", "Объединить"), Glyphs.Merge, BatchOperation.AllOf(Formats.Audio, min: 2),
            async (items, ctx) =>
            {
                var sorted = items.OrderBy(i => i.Path, NaturalComparer.Instance).ToList();
                var format = FfmpegBackend.SafeContainer(sorted[0].Format);
                var graph = string.Concat(Enumerable.Range(0, sorted.Count).Select(i => $"[{i}:a]")) + $"concat=n={sorted.Count}:v=0:a=1[a]";
                return [await OutputPaths.ProduceAsync(ctx.OutputDirFor(sorted[0].Path), $"{sorted[0].BaseName} ({T("joined", "объединено")})", format, tmp =>
                    FfmpegBackend.RunAsync(ctx, sorted.Select(i => i.Path).ToList(),
                        ["-filter_complex", graph, "-map", "[a]", .. FfmpegBackend.CodecArgs(format, ctx.Settings)], tmp, ctx.Progress))];
            })
        { Requires = [ExternalTool.Ffmpeg] });
    }

    // ---------------------------------------------------------------- video

    private static void AddVideo(List<Operation> ops)
    {
        foreach (var target in Formats.VideoOutputs)
        {
            ops.Add(new PerFileOperation($"video.to.{target}", WheelKind.Convert, Formats.Display(target), target == "mp3" ? Glyphs.Audio : Glyphs.Video, Formats.Video,
                async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, target, ctx, p)], targetFormat: target,
                subtitle: target switch { "mp3" => T("audio only", "только звук"), "gif" => T("animation", "анимация"), _ => null })
            { Requires = [ExternalTool.Ffmpeg] });
        }

        Ffmpeg(ops, "video.compress", T("Compress", "Сжать"), Glyphs.Shrink, Formats.Video, T("compressed", "сжато"),
            (item, ctx) => H264(ctx.Settings.CompressVideoCrf, "slow", null, "128k"), outputFormat: _ => "mp4", subtitle: "H.264");
        Ffmpeg(ops, "video.720p", "720p", Glyphs.Shrink, Formats.Video, "720p",
            (item, ctx) => H264(ctx.Settings.VideoCrf, "medium", "scale=-2:'min(720,ih)'", "160k"), outputFormat: _ => "mp4", subtitle: T("downscale", "уменьшение"));
        Ffmpeg(ops, "video.mute", T("Mute", "Без звука"), Glyphs.Mute, Formats.Video, T("muted", "без звука"),
            (item, ctx) => FfmpegBackend.SafeContainer(item.Format) == item.Format
                ? ["-map", "0:v", "-c:v", "copy", "-an"]
                : [.. H264(ctx.Settings.VideoCrf, "medium", null, null), "-an"],
            subtitle: T("muted", "без звука"));
        ops.Add(Trim("video.trim", Formats.Video));
        string[] codecs = ["H.264", "H.265", "AV1", "VP9", "ProRes"];
        ops.Add(new PerFileOperation("video.encode", WheelKind.Tools, T("Codec & FPS", "Кодек и FPS"), Glyphs.Video, Formats.Video, async (item, ctx, p) =>
        {
            var codec = ctx.Param("codec", "H.264");
            // "Same" (no digits) keeps the rate; anything else must be a rate.
            var fpsText = ctx.Param("fps");
            double? fps = !fpsText.Any(char.IsDigit) ? null
                : FfmpegBackend.ParseRate(fpsText) is { } r and <= 1000 ? r
                : throw new ConversionException(T($"Invalid frame rate: {fpsText}", $"Некорректная частота кадров: {fpsText}"));
            var (args, container) = EncodeArgs(codec, fps, ctx.Settings.VideoCrf);
            var suffix = fps is null ? codec : $"{codec} {fps.Value.ToString("0.###", CultureInfo.InvariantCulture)} fps";
            return [await FfmpegBackend.TransformAsync(item, suffix, ctx, p, args, container)];
        }, subtitle: "H.265, AV1, 60 fps…")
        {
            Requires = [ExternalTool.Ffmpeg],
            Parameters =
            [
                new ParameterSpec("codec", T("Codec", "Кодек"), "H.264", T("H.264 plays everywhere; H.265 and AV1 are smaller but slower to encode; ProRes is for editing (large files)",
                    "H.264 открывается везде; H.265 и AV1 меньше, но кодируются дольше; ProRes — для монтажа (большие файлы)"), codecs),
                new ParameterSpec("fps", T("Frame rate", "Частота кадров"), T("Same", "Как есть"),
                    T("Lowering drops frames, raising repeats them. Own value: e.g. 12, 29.97 or 30000/1001", "Понижение выкидывает кадры, повышение повторяет их. Своё значение: например, 12, 29.97 или 30000/1001"),
                    [T("Same", "Как есть"), "24", "25", "30", "50", "60"], OwnValue: true),
            ],
        });
        ops.Add(new PerFileOperation("video.relight", WheelKind.Tools, T("Light & color", "Свет и цвет"), Glyphs.Brightness, Formats.Video,
            async (item, ctx, p) => [await FfmpegBackend.TransformAsync(item, T("edited", "обработано"), ctx, p,
                H264(ctx.Settings.VideoCrf, "medium", Relight.Filter(ctx.Parameters), "160k"), "mp4")],
            subtitle: T("exposure, warmth", "экспозиция, теплота"))
        { Requires = [ExternalTool.Ffmpeg], Parameters = Relight.Parameters() });
        Ffmpeg(ops, "video.rotate", T("Rotate", "Повернуть"), Glyphs.Rotate, Formats.Video, T("rotated", "повёрнуто"),
            (item, ctx) => H264(ctx.Settings.VideoCrf, "medium", "transpose=1", "160k"), outputFormat: _ => "mp4", subtitle: T("right", "вправо"));
        // Keeps the old id, so wheels customised with the former fixed 2× slice still show it.
        ops.Add(new PerFileOperation("video.speed2", WheelKind.Tools, T("Speed", "Скорость"), Glyphs.Play, Formats.Video, async (item, ctx, p) =>
        {
            var text = ctx.Param("speed", "2").TrimEnd('×', 'x', 'х', ' ');
            var speed = FfmpegBackend.ParseRate(text) is { } s and >= 0.1 and <= 16 ? s
                : throw new ConversionException(T($"Speed must be from 0.1 to 16: {text}", $"Скорость должна быть от 0,1 до 16: {text}"));
            return [await FfmpegBackend.TransformAsync(item, $"{speed.ToString("0.###", CultureInfo.InvariantCulture)}×", ctx, p,
                [.. H264(ctx.Settings.VideoCrf, "medium", $"setpts=PTS/{speed.ToString("R", CultureInfo.InvariantCulture)}", "160k"), "-af", Atempo(speed)], "mp4")];
        }, subtitle: "0.5× … 4×")
        {
            Requires = [ExternalTool.Ffmpeg],
            Parameters =
            [
                new ParameterSpec("speed", T("Speed", "Скорость"), "2×", T("Sound keeps its pitch. Own value: from 0.1 to 16, e.g. 1.25", "Звук сохраняет высоту тона. Своё значение: от 0,1 до 16, например 1,25"),
                    ["0.25×", "0.5×", "0.75×", "1.5×", "2×", "4×"], OwnValue: true),
            ],
        });
        ops.Add(new PerFileOperation("video.thumbnail", WheelKind.Tools, T("Frames", "Кадры"), Glyphs.Camera, Formats.Video, async (item, ctx, p) =>
        {
            var times = ctx.Param("times").Split([';', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(FfmpegBackend.ParseTime).ToList();
            if (times.Count == 0)
                return [await FfmpegBackend.TransformAsync(item, T("thumbnail", "миниатюра"), ctx, p, ["-vf", "thumbnail=150", "-frames:v", "1", "-update", "1"], "png")];
            var outputs = new List<string>();
            for (var i = 0; i < times.Count; i++)
            {
                // -ss before -i decodes up to the exact frame (the player marks frame starts).
                var stamp = FfmpegBackend.FormatTime(times[i]).Replace(':', '-');
                outputs.Add(await FfmpegBackend.TransformAsync(item, $"{T("frame", "кадр")} {stamp}", ctx, new Progress<double>(),
                    ["-frames:v", "1", "-update", "1"], "png", inputArgs: ["-ss", times[i].ToString("0.###", CultureInfo.InvariantCulture)], duration: 1));
                p.Report((i + 1.0) / times.Count);
            }
            return outputs;
        }, subtitle: T("to PNG", "в PNG"))
        {
            Requires = [ExternalTool.Ffmpeg],
            Parameters = [new ParameterSpec("times", T("Frames", "Кадры"), "", T("times separated by “;”, e.g. 0:05; 1:30.5 — empty = pick one automatically", "время через «;», например 0:05; 1:30.5 — пусто = выбрать автоматически"))],
        });
    }

    /// <summary>atempo takes 0.5…100 per filter, so slower speeds chain several 0.5 steps.</summary>
    public static string Atempo(double speed)
    {
        var chain = new List<string>();
        for (; speed < 0.5; speed /= 0.5)
            chain.Add("atempo=0.5");
        chain.Add($"atempo={speed.ToString("R", CultureInfo.InvariantCulture)}");
        return string.Join(",", chain);
    }

    /// <summary>Encoder arguments and container for a codec; <paramref name="crf"/> is the H.264 quality, the others are matched to it.</summary>
    internal static (string[] Args, string Container) EncodeArgs(string codec, double? fps, int crf)
    {
        var vf = (fps is { } f ? $"fps={f.ToString("R", CultureInfo.InvariantCulture)}," : "") + "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        string Crf(int offset) => (crf + offset).ToString(CultureInfo.InvariantCulture);
        return codec switch
        {
            // hvc1 tag: otherwise Apple players refuse H.265 in MP4.
            "H.265" => (["-c:v", "libx265", "-preset", "medium", "-crf", Crf(5), "-tag:v", "hvc1", "-pix_fmt", "yuv420p", "-vf", vf, "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"], "mp4"),
            // The FFmpeg build Sweetch downloads has libaom, not SVT-AV1; cpu-used 6 keeps it bearable.
            "AV1" => (["-c:v", "libaom-av1", "-crf", Crf(9), "-b:v", "0", "-cpu-used", "6", "-row-mt", "1", "-pix_fmt", "yuv420p", "-vf", vf, "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"], "mp4"),
            "VP9" => (["-c:v", "libvpx-vp9", "-crf", Crf(9), "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p", "-vf", vf, "-c:a", "libopus", "-b:a", "128k"], "webm"),
            "ProRes" => (["-c:v", "prores_ks", "-profile:v", "3", "-pix_fmt", "yuv422p10le", "-vf", vf, "-c:a", "pcm_s16le"], "mov"),
            _ => ([.. H264(crf, "medium", fps is { } r ? $"fps={r.ToString("R", CultureInfo.InvariantCulture)}" : null, "160k")], "mp4"),
        };
    }

    private static string[] H264(int crf, string preset, string? filter, string? audioBitrate)
    {
        var vf = (filter is null ? "" : filter + ",") + "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        var args = new List<string> { "-c:v", "libx264", "-preset", preset, "-crf", crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-vf", vf, "-movflags", "+faststart" };
        if (audioBitrate is not null)
            args.AddRange(["-c:a", "aac", "-b:a", audioBitrate]);
        return [.. args];
    }

    private static void Ffmpeg(List<Operation> ops, string id, string label, string glyph, ISet<string> inputs, string suffix,
        Func<FileItem, OperationContext, IEnumerable<string>> args, Func<FileItem, string>? outputFormat = null, string? subtitle = null) =>
        ops.Add(new PerFileOperation(id, WheelKind.Tools, label, glyph, inputs,
            async (item, ctx, p) => [await FfmpegBackend.TransformAsync(item, suffix, ctx, p, args(item, ctx), outputFormat?.Invoke(item))],
            subtitle: subtitle)
        { Requires = [ExternalTool.Ffmpeg] });

    private static Operation Trim(string id, ISet<string> inputs) =>
        new PerFileOperation(id, WheelKind.Tools, T("Trim", "Обрезать"), Glyphs.Stopwatch, inputs, async (item, ctx, p) =>
        {
            var start = FfmpegBackend.ParseTime(ctx.Param("start", "0"));
            var endText = ctx.Param("end");
            double? length = endText.Length > 0 ? FfmpegBackend.ParseTime(endText) - start : null;
            if (length is <= 0)
                throw new ConversionException(T("The end must come after the start.", "Конец должен быть позже начала."));
            var format = FfmpegBackend.SafeContainer(item.Format);
            var args = new List<string>();
            if (length is not null)
                args.AddRange(["-t", length.Value.ToString("0.###", CultureInfo.InvariantCulture)]);
            args.AddRange(Formats.Video.Contains(item.Format)
                ? H264(ctx.Settings.VideoCrf, "medium", null, "160k")
                : FfmpegBackend.CodecArgs(format, ctx.Settings));
            return [await FfmpegBackend.TransformAsync(item, T("trimmed", "обрезано"), ctx, p, args,
                Formats.Video.Contains(item.Format) ? "mp4" : format,
                inputArgs: start > 0 ? ["-ss", start.ToString("0.###", CultureInfo.InvariantCulture)] : null,
                duration: length)];
        }, subtitle: T("start–end", "начало–конец"))
        {
            Requires = [ExternalTool.Ffmpeg],
            Parameters =
            [
                new ParameterSpec("start", T("Start", "Начало"), "0:00", T("e.g. 0:30 or 1:02:03", "например, 0:30 или 1:02:03")),
                new ParameterSpec("end", T("End", "Конец"), "", T("empty = until the end", "пусто = до конца")),
            ],
        };

    // ---------------------------------------------------------------- PDF

    private static void AddPdf(List<Operation> ops)
    {
        ops.Add(PdfDoc("docx", "Word"));
        foreach (var image in new[] { "jpg", "png" })
        {
            ops.Add(new PerFileOperation($"pdf.to.{image}", WheelKind.Convert, Formats.Display(image), Glyphs.Photo, Formats.Pdf,
                (item, ctx, p) => PdfBackend.RenderAsync(item, image, ctx, p), subtitle: T("all pages", "все страницы")));
        }
        ops.Add(new PerFileOperation("pdf.to.pptx", WheelKind.Convert, "PPTX", Glyphs.Page, Formats.Pdf,
            async (item, ctx, _) => [await OfficeBackend.ConvertAsync(item, "pptx", ctx, "impress_pdf_import")], subtitle: "PowerPoint")
        { Requires = [ExternalTool.LibreOffice] });
        foreach (var text in new[] { "txt", "md" })
        {
            ops.Add(new PerFileOperation($"pdf.to.{text}", WheelKind.Convert, Formats.Display(text), Glyphs.Text, Formats.Pdf,
                async (item, ctx, _) => [await PdfBackend.ToTextAsync(item, text, ctx)], subtitle: text == "md" ? "Markdown" : "text"));
        }
        ops.Add(PdfDoc("html", "web"));
        ops.Add(PdfDoc("epub", T("e-book", "электронная книга")));

        ops.Add(new BatchOperation("pdf.merge", WheelKind.Tools, T("Merge PDFs", "Объединить PDF"), Glyphs.Merge, BatchOperation.AllOf(Formats.Pdf, min: 2),
            async (items, ctx) =>
            {
                var sorted = items.Select(i => i.Path).OrderBy(p => p, NaturalComparer.Instance).ToList();
                return [await OutputPaths.ProduceAsync(ctx.OutputDirFor(sorted[0]), $"{items[0].BaseName} ({T("merged", "объединено")})", "pdf",
                    tmp => Task.Run(() => PdfBackend.Merge(sorted, tmp), ctx.CancellationToken))];
            }));
        ops.Add(new PerFileOperation("pdf.split", WheelKind.Tools, T("Split", "Разделить"), Glyphs.Split, Formats.Pdf,
            (item, ctx, p) => PdfBackend.SplitAsync(item, ctx, p), subtitle: T("into pages", "на страницы")));
        ops.Add(new PerFileOperation("pdf.rotate", WheelKind.Tools, T("Rotate", "Повернуть"), Glyphs.Rotate, Formats.Pdf,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("rotated", "повёрнуто")})", "pdf",
                tmp => Task.Run(() => PdfBackend.Rotate(item.Path, tmp, 90), ctx.CancellationToken))], subtitle: T("right", "вправо")));
        ops.Add(new PerFileOperation("pdf.optimize", WheelKind.Tools, T("Optimize", "Оптимизировать"), Glyphs.Shrink, Formats.Pdf,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("optimized", "оптимизировано")})", "pdf",
                tmp => Task.Run(() => PdfBackend.Optimize(item.Path, tmp), ctx.CancellationToken))]));
    }

    private static Operation PdfDoc(string target, string subtitle) =>
        new PerFileOperation($"pdf.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Document, Formats.Pdf,
            async (item, ctx, _) => [await PdfBackend.ToDocumentAsync(item, target, ctx)], subtitle: subtitle)
        { Requires = [ExternalTool.Pandoc] };

    // ---------------------------------------------------------------- documents

    private static void AddDocuments(List<Operation> ops)
    {
        foreach (var target in Formats.DocumentOutputs)
        {
            ops.Add(new PerFileOperation($"doc.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Document, Formats.Documents,
                async (item, ctx, _) => [await DocumentAsync(item, target, ctx)], targetFormat: target,
                subtitle: target switch { "docx" => "Word", "md" => "Markdown", "epub" => T("e-book", "электронная книга"), "txt" => "text", "html" => "web", _ => null })
            {
                AvailableFor = (tools, item) => DocumentRoute(item.Format, target, tools) is not null,
            });
        }
    }

    private enum DocRoute { Pandoc, Office, PandocThenOffice, OfficeThenPandoc }

    private static DocRoute? DocumentRoute(string input, string target, ToolLocator tools)
    {
        var pandoc = tools.Has(ExternalTool.Pandoc);
        var office = tools.Has(ExternalTool.LibreOffice);
        bool pandocReads = Formats.PandocInputs.Contains(input), officeReads = input is "doc" or "docx" or "odt" or "rtf" or "txt" or "html";
        bool pandocWrites = target != "pdf", officeWrites = target is "pdf" or "docx" or "odt" or "txt" or "html" || (target == "rtf" && input != "html");

        // LibreOffice keeps layout better between word-processor formats; pandoc is better at everything else.
        if (office && officeReads && officeWrites && (target is "pdf" or "docx" or "odt" or "rtf" || !pandoc || !pandocReads))
            return DocRoute.Office;
        if (pandoc && pandocReads && pandocWrites)
            return DocRoute.Pandoc;
        if (pandoc && office && pandocReads && target == "pdf")
            return DocRoute.PandocThenOffice;
        if (pandoc && office && officeReads && pandocWrites)
            return DocRoute.OfficeThenPandoc;
        return null;
    }

    private static async Task<string> DocumentAsync(FileItem item, string target, OperationContext ctx)
    {
        var route = DocumentRoute(item.Format, target, ctx.Tools) ?? throw new ConversionException(
            T($"Converting {Formats.Display(item.Format)} → {Formats.Display(target)} needs Pandoc or LibreOffice.", $"Для конвертации {Formats.Display(item.Format)} → {Formats.Display(target)} требуется Pandoc или LibreOffice."));
        var dir = ctx.OutputDirFor(item.Path);
        switch (route)
        {
            case DocRoute.Office:
                return await OfficeBackend.ConvertAsync(item, target, ctx);
            case DocRoute.Pandoc:
                return await OutputPaths.ProduceAsync(dir, item.BaseName, target, tmp => PandocBackend.ConvertAsync(ctx, item.Path, item.Format, tmp, target));
            case DocRoute.PandocThenOffice:
            {
                var temp = OutputPaths.TempDirectory();
                try
                {
                    var docx = Path.Combine(temp, item.BaseName + ".docx");
                    await PandocBackend.ConvertAsync(ctx, item.Path, item.Format, docx, "docx");
                    return await OfficeBackend.ConvertAsync(new FileItem(docx), target, new OperationContextOverride(ctx, dir).Context);
                }
                finally
                {
                    OutputPaths.TryDelete(temp);
                }
            }
            default:
            {
                var docx = await OfficeBackend.ConvertToTempAsync(ctx, item.Path, "docx");
                try
                {
                    return await OutputPaths.ProduceAsync(dir, item.BaseName, target, tmp => PandocBackend.ConvertAsync(ctx, docx, "docx", tmp, target));
                }
                finally
                {
                    OutputPaths.TryDelete(Path.GetDirectoryName(docx)!);
                }
            }
        }
    }

    // ---------------------------------------------------------------- slides

    private static void AddSlides(List<Operation> ops)
    {
        foreach (var target in Formats.SlideOutputs)
        {
            ops.Add(new PerFileOperation($"slides.to.{target}", WheelKind.Convert, Formats.Display(target), target is "png" or "jpg" ? Glyphs.Photo : Glyphs.Page,
                Formats.Slides,
                async (item, ctx, p) =>
                {
                    if (target is "png" or "jpg")
                    {
                        var pdf = await OfficeBackend.ConvertToTempAsync(ctx, item.Path, "pdf");
                        try
                        {
                            // Render the temporary PDF but name the output after the presentation.
                            var renamed = Path.Combine(Path.GetDirectoryName(pdf)!, item.BaseName + ".pdf");
                            if (!string.Equals(renamed, pdf, StringComparison.Ordinal))
                                File.Move(pdf, renamed);
                            return await PdfBackend.RenderAsync(new FileItem(renamed), target, new OperationContextOverride(ctx, ctx.OutputDirFor(item.Path)).Context, p);
                        }
                        finally
                        {
                            OutputPaths.TryDelete(Path.GetDirectoryName(pdf)!);
                        }
                    }
                    return [await OfficeBackend.ConvertAsync(item, target, ctx)];
                }, targetFormat: target, subtitle: target is "png" or "jpg" ? T("all slides", "все слайды") : null)
            { Requires = [ExternalTool.LibreOffice] });
        }
    }

    // ---------------------------------------------------------------- spreadsheets

    private static void AddSpreadsheets(List<Operation> ops)
    {
        var native = new HashSet<string>(["xlsx", "xlsm", "xls", "csv", "tsv"], StringComparer.OrdinalIgnoreCase);
        foreach (var target in Formats.SpreadsheetOutputs)
        {
            var nativeTarget = target is "xlsx" or "csv" or "tsv";
            ops.Add(new PerFileOperation($"sheet.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Table, Formats.Spreadsheets,
                async (item, ctx, _) => nativeTarget && native.Contains(item.Format)
                    ? await SpreadsheetBackend.ConvertAsync(item, target, ctx)
                    : [await OfficeBackend.ConvertAsync(item, target, ctx)],
                targetFormat: target, subtitle: target == "xlsx" ? "Excel" : null)
            {
                AvailableFor = (tools, item) => (nativeTarget && native.Contains(item.Format)) || tools.Has(ExternalTool.LibreOffice),
            });
        }
    }

    // ---------------------------------------------------------------- subtitles

    private static void AddSubtitles(List<Operation> ops)
    {
        foreach (var target in Formats.SubtitleOutputs)
        {
            ops.Add(new PerFileOperation($"subs.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Subtitles, Formats.Subtitles,
                async (item, ctx, _) => [await SubtitleBackend.ConvertAsync(item, target, ctx)], targetFormat: target,
                subtitle: target == "txt" ? T("text only", "только текст") : null));
        }
    }

    // ---------------------------------------------------------------- archives

    private static void AddArchives(List<Operation> ops)
    {
        ops.Add(new PerFileOperation("archive.extract", WheelKind.Convert, T("Extract", "Распаковать"), Glyphs.Extract, Formats.Archives,
            async (item, ctx, _) => [await ArchiveBackend.ExtractAsync(item.Path, ctx.OutputDirFor(item.Path), ctx.CancellationToken)]));
        foreach (var target in Formats.ArchiveOutputs)
        {
            ops.Add(new PerFileOperation($"archive.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Pack, Formats.Archives,
                async (item, ctx, _) => [await ArchiveBackend.RepackAsync(item, target, ctx)], targetFormat: target));
        }

        foreach (var (target, label) in new[] { ("zip", "ZIP"), ("tar.gz", "TAR.GZ") })
        {
            ops.Add(new BatchOperation($"pack.{target}", WheelKind.Tools, T("Archive", "В архив"), Glyphs.Pack, _ => true,
                async (items, ctx) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(items[0].Path), ArchiveBackend.ArchiveNameFor(items), target,
                    tmp => ArchiveBackend.PackAsync(items.Select(i => i.Path).ToList(), tmp, target, ctx.CancellationToken))],
                subtitle: label));
        }
    }
}

internal static class Helpers
{
    public static Task<string> Produce(FileItem item, OperationContext ctx, string format, Func<string, Task> produce) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, produce);
}

/// <summary>Copy of a context that writes into a fixed folder (for multi-step conversions via temp files).</summary>
internal sealed class OperationContextOverride(OperationContext source, string outputDirectory)
{
    public OperationContext Context { get; } = new()
    {
        Tools = source.Tools,
        Progress = source.Progress,
        CancellationToken = source.CancellationToken,
        OutputDirectory = outputDirectory,
        Parameters = source.Parameters,
        Settings = source.Settings,
    };
}

/// <summary>Sorts "page 2" before "page 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0)
                    return c;
            }
            else
            {
                var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
