using System.Text;
using ClosedXML.Excel;
using Sweetch.Core;
using Sweetch.Core.Backends;
using Sweetch.Core.Operations;
using ImageMagick;
using Xunit.Abstractions;

namespace Sweetch.Tests;

/// <summary>
/// Runs every operation that applies to a set of generated sample files and checks that it produces output.
/// Operations whose external tool (ffmpeg, pandoc, LibreOffice) is not installed are skipped.
/// </summary>
[Trait("Category", "Integration")]
public class IntegrationTests : IClassFixture<IntegrationTests.Samples>
{
    public sealed class Samples : IDisposable
    {
        public string Dir { get; } = OutputPaths.TempDirectory();
        public ToolLocator Tools { get; } = new();
        public Dictionary<string, string> Files { get; } = new();

        /// <summary>Without LibreOffice the sample PDF is made of images only, so it has no text layer.</summary>
        public bool PdfHasText { get; private set; }

        public Samples()
        {
            var png = Path.Combine(Dir, "obrázek.png");
            using (var image = new MagickImage(MagickColors.Orange, 320, 200))
            {
                image.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.SkyBlue).Rectangle(20, 20, 160, 120));
                image.Write(png);
                image.Write(Path.Combine(Dir, "druhý.jpg"));
            }
            Files["png"] = png;
            Files["jpg"] = Path.Combine(Dir, "druhý.jpg");
            File.WriteAllText(Path.Combine(Dir, "vektor.svg"),
                "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='30' fill='orange'/></svg>");
            Files["svg"] = Path.Combine(Dir, "vektor.svg");

            var pdf = Path.Combine(Dir, "dokument.pdf");
            File.WriteAllText(Path.Combine(Dir, "zdroj.md"), "# Nadpis\n\nPříliš žluťoučký kůň úpěl ďábelské ódy.\n\n- jedna\n- dvě\n", new UTF8Encoding(false));
            Files["md"] = Path.Combine(Dir, "zdroj.md");
            if (Tools.Has(ExternalTool.LibreOffice))
            {
                var produced = OfficeBackend.ConvertToTempAsync(Context(), Files["md"], "pdf").GetAwaiter().GetResult();
                File.Move(produced, pdf);
                PdfHasText = true;
            }
            else
            {
                ImageBackend.ImagesToPdfAsync([png, Files["jpg"]], pdf, 90, default).GetAwaiter().GetResult();
            }
            Files["pdf"] = pdf;
            File.Copy(pdf, Path.Combine(Dir, "druhý.pdf"));
            Files["pdf2"] = Path.Combine(Dir, "druhý.pdf");

            File.WriteAllText(Path.Combine(Dir, "web.html"), "<html><body><h1>Ahoj</h1><p>Odstavec</p></body></html>");
            Files["html"] = Path.Combine(Dir, "web.html");
            File.WriteAllText(Path.Combine(Dir, "poznámky.txt"), "Řádek jedna\n\nŘádek dva\n");
            Files["txt"] = Path.Combine(Dir, "poznámky.txt");

            var xlsx = Path.Combine(Dir, "tabulka.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Data");
                ws.Cell(1, 1).Value = "Jméno";
                ws.Cell(1, 2).Value = "Počet";
                ws.Cell(2, 1).Value = "Žluťák";
                ws.Cell(2, 2).Value = 42;
                wb.Worksheets.Add("Druhý").Cell(1, 1).Value = "x";
                wb.SaveAs(xlsx);
            }
            Files["xlsx"] = xlsx;
            File.WriteAllText(Path.Combine(Dir, "data.csv"), "a;b\n1;\"x;y\"\n", new UTF8Encoding(false));
            Files["csv"] = Path.Combine(Dir, "data.csv");

            File.WriteAllText(Path.Combine(Dir, "titulky.srt"), "1\n00:00:01,000 --> 00:00:02,000\nAhoj\n");
            Files["srt"] = Path.Combine(Dir, "titulky.srt");

            var folder = Path.Combine(Dir, "složka");
            Directory.CreateDirectory(Path.Combine(folder, "pod"));
            File.WriteAllText(Path.Combine(folder, "pod", "a.txt"), "a");
            File.WriteAllText(Path.Combine(folder, "b.txt"), "b");
            Files["folder"] = folder;
            var zip = Path.Combine(Dir, "archiv.zip");
            ArchiveBackend.PackAsync([folder], zip, "zip", default).GetAwaiter().GetResult();
            Files["zip"] = zip;
            var tgz = Path.Combine(Dir, "archiv2.tar.gz");
            ArchiveBackend.PackAsync([folder, Files["txt"]], tgz, "tar.gz", default).GetAwaiter().GetResult();
            Files["tgz"] = tgz;

            if (Tools.Has(ExternalTool.Ffmpeg))
            {
                Ffmpeg("-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2", "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", Path.Combine(Dir, "video.mp4"));
                Files["mp4"] = Path.Combine(Dir, "video.mp4");
                Ffmpeg("-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1", "-f", "lavfi", "-i", "sine=duration=1", "-shortest", Path.Combine(Dir, "staré.avi"));
                Files["avi"] = Path.Combine(Dir, "staré.avi");
                Ffmpeg("-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1", Path.Combine(Dir, "nemé.mkv"));
                Files["mute"] = Path.Combine(Dir, "nemé.mkv");
                Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:duration=2", Path.Combine(Dir, "zvuk.wav"));
                Files["wav"] = Path.Combine(Dir, "zvuk.wav");
                Ffmpeg("-f", "lavfi", "-i", "sine=frequency=880:duration=1", Path.Combine(Dir, "zvuk2.mp3"));
                Files["mp3"] = Path.Combine(Dir, "zvuk2.mp3");
                Ffmpeg("-f", "lavfi", "-i", "testsrc=size=80x60:rate=5:duration=1", Path.Combine(Dir, "animace.gif"));
                Files["gif"] = Path.Combine(Dir, "animace.gif");
            }
            if (Tools.Has(ExternalTool.Pandoc))
            {
                var docx = Path.Combine(Dir, "text.docx");
                PandocBackend.ConvertAsync(Context(), Files["md"], "md", docx, "docx").GetAwaiter().GetResult();
                Files["docx"] = docx;
            }
            if (Tools.Has(ExternalTool.LibreOffice))
            {
                var odp = OfficeBackend.ConvertToTempAsync(Context(), Files["pdf"], "odp", "impress_pdf_import").GetAwaiter().GetResult();
                var pptx = Path.Combine(Dir, "prezentace.pptx");
                var tmp = OfficeBackend.ConvertToTempAsync(Context(), odp, "pptx").GetAwaiter().GetResult();
                File.Move(tmp, pptx);
                Files["pptx"] = pptx;
            }
        }

        private void Ffmpeg(params string[] args) =>
            ProcessRunner.RunAsync(Tools.Ffmpeg!, ["-hide_banner", "-y", .. args], default).GetAwaiter().GetResult();

        public OperationContext Context(string? outDir = null, Dictionary<string, string>? parameters = null) => new()
        {
            Tools = Tools,
            OutputDirectory = outDir,
            Parameters = parameters ?? new Dictionary<string, string>(),
        };

        public void Dispose() => OutputPaths.TryDelete(Dir);
    }

    private readonly Samples _s;
    private readonly ITestOutputHelper output;

    public IntegrationTests(ITestOutputHelper output, Samples samples)
    {
        this.output = output;
        _s = samples;
    }

    public static TheoryData<string> SampleKeys() => new(
        "png", "jpg", "svg", "pdf", "md", "html", "txt", "xlsx", "csv", "srt", "folder", "zip", "tgz", "mp4", "avi", "wav", "mp3", "gif", "docx", "pptx");

    [Theory]
    [MemberData(nameof(SampleKeys))]
    public async Task Every_operation_produces_output(string key)
    {
        if (!_s.Files.TryGetValue(key, out var path))
        {
            output.WriteLine($"sample {key} skipped (tool missing)");
            return;
        }
        var items = new List<FileItem> { new(path) };
        var all = Catalog.Build();
        var failures = new List<string>();
        foreach (var wheel in new[] { WheelKind.Convert, WheelKind.Tools })
        {
            foreach (var op in Catalog.For(all, wheel, items, _s.Tools))
            {
                var outDir = Path.Combine(_s.Dir, "out", key, op.Id);
                Directory.CreateDirectory(outDir);
                var parameters = op.Parameters.ToDictionary(p => p.Key, p => p.Key == "end" ? "1.5" : p.Key == "start" ? "0.5" : p.DefaultValue);
                try
                {
                    var result = await op.ExecuteAsync(items, _s.Context(outDir, parameters));
                    Assert.NotEmpty(result.Outputs);
                    foreach (var o in result.Outputs)
                    {
                        Assert.True(File.Exists(o) || Directory.Exists(o), $"missing {o}");
                        if (File.Exists(o))
                            Assert.True(new FileInfo(o).Length > 0, $"empty {o}");
                    }
                    Assert.Empty(Directory.GetFiles(outDir, "*.part.*"));
                    output.WriteLine($"ok   {op.Id} → {string.Join(", ", result.Outputs.Select(Path.GetFileName))}");
                }
                catch (ConversionException) when (key is "pdf" && !_s.PdfHasText && op.Id is "pdf.to.txt" or "pdf.to.md")
                {
                    output.WriteLine($"ok   {op.Id} (expected: image-only PDF has no text)");
                }
                catch (Exception ex)
                {
                    failures.Add($"{op.Id}: {ex.Message}");
                    output.WriteLine($"FAIL {op.Id}: {ex}");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Batch_operations_work()
    {
        var all = Catalog.Build();
        var outDir = Path.Combine(_s.Dir, "out", "batch");
        Directory.CreateDirectory(outDir);

        async Task Run(string id, params string[] keys)
        {
            var items = keys.Select(k => new FileItem(_s.Files[k])).ToList();
            var op = all.Single(o => o.Id == id);
            Assert.True(op.AppliesTo(items), id);
            var result = await op.ExecuteAsync(items, _s.Context(outDir));
            Assert.Single(result.Outputs);
            output.WriteLine($"ok   {id} → {Path.GetFileName(result.Outputs[0])}");
        }

        await Run("pdf.merge", "pdf", "pdf2");
        await Run("image.merge.pdf", "png", "jpg");
        await Run("pack.zip", "folder", "png");
        if (_s.Tools.Has(ExternalTool.Ffmpeg))
            await Run("audio.concat", "wav", "mp3");

        var merged = Directory.GetFiles(outDir, "*.pdf").First(f => f.Contains("merged"));
        Assert.Equal(PdfBackend.PageCount(_s.Files["pdf"]) * 2, PdfBackend.PageCount(merged));
    }

    [Fact]
    public async Task Trim_marks_keep_exactly_the_marked_frames()
    {
        if (!_s.Tools.Has(ExternalTool.Ffmpeg))
            return;
        // 23.976 fps: frame starts are not whole milliseconds, where an off-by-one would show.
        var dir = Path.Combine(_s.Dir, "out", "frames");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "ntsc.mp4");
        await ProcessRunner.RunAsync(_s.Tools.Ffmpeg!, ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=24000/1001:duration=3",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", source], default);
        var (_, fps) = await FfmpegBackend.ProbeAsync(_s.Tools.Ffmpeg!, source);
        Assert.Equal(24000.0 / 1001, fps!.Value, 9);
        var frameMs = 1000 / fps!.Value;

        // Start mark on frame 36, end mark on frame 40, both kept: 5 frames.
        var op = Catalog.Build().Single(o => o.Id == "video.trim");
        var parameters = new Dictionary<string, string>
        {
            ["start"] = FfmpegBackend.FormatTime(FfmpegBackend.FrameStart(36, frameMs)),
            ["end"] = FfmpegBackend.FormatTime(FfmpegBackend.FrameEnd(40, frameMs)),
        };
        var result = await op.ExecuteAsync([new FileItem(source)], _s.Context(dir, parameters));

        var frames = 0;
        try
        {
            await ProcessRunner.RunAsync(_s.Tools.Ffmpeg!, ["-hide_banner", "-i", result.Outputs[0], "-map", "0:v:0", "-f", "null", "-"], default, line =>
            {
                if (System.Text.RegularExpressions.Regex.Match(line, @"frame=\s*(\d+)") is { Success: true } m)
                    frames = int.Parse(m.Groups[1].Value);
            });
        }
        finally
        {
            output.WriteLine($"{parameters["start"]} – {parameters["end"]}: {frames} frames");
        }
        Assert.Equal(5, frames);
    }

    [Theory]
    [InlineData("H.264", "60", "h264", 60)]
    [InlineData("H.265", "", "hevc", 25)]
    [InlineData("AV1", "24", "av1", 24)]
    [InlineData("VP9", "", "vp9", 25)]
    [InlineData("ProRes", "30", "prores", 30)]
    [InlineData("H.264", "12", "h264", 12)]
    [InlineData("VP9", "30000/1001", "vp9", 30000.0 / 1001)]
    public async Task Codec_and_fps(string codec, string fps, string expectedCodec, double expectedFps)
    {
        if (!_s.Tools.Has(ExternalTool.Ffmpeg))
            return;
        var op = Catalog.Build().Single(o => o.Id == "video.encode");
        var result = await op.ExecuteAsync([new FileItem(_s.Files["mp4"])],
            _s.Context(Path.Combine(_s.Dir, "out", "encode", codec), new Dictionary<string, string> { ["codec"] = codec, ["fps"] = fps }));
        var info = "";
        try
        {
            await ProcessRunner.RunAsync(_s.Tools.Ffmpeg!, ["-hide_banner", "-i", result.Outputs[0]], default, line => info += line + Environment.NewLine);
        }
        catch (ConversionException)
        {
            // No output file: ffmpeg only prints the stream info.
        }
        Assert.Contains($"Video: {expectedCodec}", info);
        Assert.Equal(expectedFps, (await FfmpegBackend.ProbeAsync(_s.Tools.Ffmpeg!, result.Outputs[0])).Fps!.Value, 6);
    }

    [Fact]
    public async Task Codec_rejects_a_junk_frame_rate()
    {
        if (!_s.Tools.Has(ExternalTool.Ffmpeg))
            return;
        var op = Catalog.Build().Single(o => o.Id == "video.encode");
        await Assert.ThrowsAsync<ConversionException>(() => op.ExecuteAsync([new FileItem(_s.Files["mp4"])],
            _s.Context(Path.Combine(_s.Dir, "out", "encode-junk"), new Dictionary<string, string> { ["codec"] = "H.264", ["fps"] = "3o fps" })));
    }

    [Theory]
    [InlineData("0.25×", 8)]
    [InlineData("1.5×", 1.333)]
    [InlineData("1,25", 1.6)]
    public async Task Speed_changes_duration_and_keeps_sound(string speed, double seconds)
    {
        if (!_s.Tools.Has(ExternalTool.Ffmpeg))
            return;
        var op = Catalog.Build().Single(o => o.Id == "video.speed2");
        var result = await op.ExecuteAsync([new FileItem(_s.Files["mp4"])],
            _s.Context(Path.Combine(_s.Dir, "out", "speed", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Dictionary<string, string> { ["speed"] = speed }));
        var (duration, _) = await FfmpegBackend.ProbeAsync(_s.Tools.Ffmpeg!, result.Outputs[0]);
        Assert.InRange(duration!.Value, seconds - 0.15, seconds + 0.15);
    }

    [Fact]
    public async Task Frames_exports_exactly_the_picked_frames()
    {
        if (!_s.Tools.Has(ExternalTool.Ffmpeg))
            return;
        var dir = Path.Combine(_s.Dir, "out", "pick");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "ntsc.mp4");
        await ProcessRunner.RunAsync(_s.Tools.Ffmpeg!, ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=24000/1001:duration=3",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", source], default);
        var frameMs = 1000 / (await FfmpegBackend.ProbeAsync(_s.Tools.Ffmpeg!, source)).Fps!.Value;
        var times = string.Join("; ", new[] { 12, 37 }.Select(n => FfmpegBackend.FormatTime(FfmpegBackend.FrameStart(n, frameMs))));

        var op = Catalog.Build().Single(o => o.Id == "video.thumbnail");
        var result = await op.ExecuteAsync([new FileItem(source)], _s.Context(dir, new Dictionary<string, string> { ["times"] = times }));
        Assert.Equal(2, result.Outputs.Count);
        foreach (var (output, n) in result.Outputs.Zip([12, 37]))
        {
            var expected = Path.Combine(dir, $"expected-{n}.png");
            await ProcessRunner.RunAsync(_s.Tools.Ffmpeg!, ["-hide_banner", "-y", "-i", source, "-vf", $"select='eq(n,{n})'", "-frames:v", "1", expected], default);
            using var a = new ImageMagick.MagickImage(output);
            using var b = new ImageMagick.MagickImage(expected);
            Assert.True(a.Compare(b, ImageMagick.ErrorMetric.RootMeanSquared) < 0.001, $"{Path.GetFileName(output)} is frame {n}");
        }
    }

    [Fact]
    public async Task Relight_runs_every_knob_and_brightens()
    {
        if (!_s.Tools.Has(ExternalTool.Ffmpeg))
            return;
        var all = Catalog.Build();
        var everything = Relight.Knobs().ToDictionary(k => k.Key, k => "40");
        var dir = Path.Combine(_s.Dir, "out", "relight");
        foreach (var (id, key) in new[] { ("image.relight", "png"), ("image.relight", "jpg"), ("video.relight", "mp4") })
        {
            var result = await all.Single(o => o.Id == id).ExecuteAsync([new FileItem(_s.Files[key])], _s.Context(dir, everything));
            Assert.True(new FileInfo(result.Outputs[0]).Length > 0, $"{id} on {key}");
        }

        var brighter = await all.Single(o => o.Id == "image.relight").ExecuteAsync([new FileItem(_s.Files["jpg"])],
            _s.Context(Path.Combine(dir, "exposure"), new Dictionary<string, string> { ["exposure"] = "50" }));
        static double Mean(string path)
        {
            using var image = new ImageMagick.MagickImage(path);
            return image.Statistics().Composite()!.Mean;
        }
        Assert.True(Mean(brighter.Outputs[0]) > Mean(_s.Files["jpg"]) * 1.05, "+50 exposure brightens the photo");
    }

    [Fact]
    public async Task Video_without_audio_gives_clear_error()
    {
        if (!_s.Files.TryGetValue("mute", out var path))
            return;
        var op = Catalog.Build().Single(o => o.Id == "video.to.mp3");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => op.ExecuteAsync([new FileItem(path)], _s.Context(Path.Combine(_s.Dir, "out", "mute"))));
        Assert.Contains("audio track", ex.Message);
        // Other video operations still work without audio.
        var speed = Catalog.Build().Single(o => o.Id == "video.speed2");
        Assert.NotEmpty((await speed.ExecuteAsync([new FileItem(path)], _s.Context(Path.Combine(_s.Dir, "out", "mute")))).Outputs);
    }

    [Fact]
    public async Task Extract_restores_tree()
    {
        var outDir = Path.Combine(_s.Dir, "out", "extract");
        Directory.CreateDirectory(outDir);
        var single = await ArchiveBackend.ExtractAsync(_s.Files["zip"], outDir, default);
        Assert.Equal("složka", Path.GetFileName(single));
        Assert.True(File.Exists(Path.Combine(single, "pod", "a.txt")));

        var multi = await ArchiveBackend.ExtractAsync(_s.Files["tgz"], outDir, default);
        Assert.Equal("archiv2", Path.GetFileName(multi));
        Assert.True(File.Exists(Path.Combine(multi, "složka", "b.txt")));
        Assert.True(File.Exists(Path.Combine(multi, "poznámky.txt")));
    }
}
