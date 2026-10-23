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
            // Formats Magick.NET reads besides the common ones; edits are saved back in each of them.
            foreach (var ext in new[] { "psd", "tga", "jxl", "jp2", "exr", "qoi", "dds", "ppm" })
            {
                using var image = new MagickImage(png);
                Files[ext] = Path.Combine(Dir, $"obraz.{ext}");
                image.Write(Files[ext]);
            }
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
            File.WriteAllText(Path.Combine(Dir, "anime.ass"), SubtitleBackend.ToAss(SubtitleBackend.Parse(File.ReadAllText(Files["srt"]))));
            Files["ass"] = Path.Combine(Dir, "anime.ass");
            File.WriteAllText(Path.Combine(Dir, "píseň.lrc"), "[ar:Kapela]\n[00:01.00]První řádek\n[00:03.20]Druhý\n");
            Files["lrc"] = Path.Combine(Dir, "píseň.lrc");

            File.WriteAllText(Path.Combine(Dir, "nastavení.json"), """{"name":"Sweetch","items":[{"id":1,"title":"Žluťák"},{"id":2,"title":"Kůň","tags":["a","b"]}]}""");
            Files["json"] = Path.Combine(Dir, "nastavení.json");
            File.WriteAllText(Path.Combine(Dir, "config.yaml"), "name: Sweetch\nversion: 2\nlist:\n  - a\n  - b\n");
            Files["yaml"] = Path.Combine(Dir, "config.yaml");
            File.WriteAllText(Path.Combine(Dir, "cargo.toml"), "[package]\nname = \"sweetch\"\nversion = \"2.0\"\n\n[[bin]]\nname = \"a\"\n");
            Files["toml"] = Path.Combine(Dir, "cargo.toml");
            File.WriteAllText(Path.Combine(Dir, "feed.xml"), "<feed lang=\"cs\"><entry><title>Ahoj</title></entry><entry><title>Svět</title></entry></feed>");
            Files["xml"] = Path.Combine(Dir, "feed.xml");
            File.WriteAllText(Path.Combine(Dir, "kontakty.vcf"), "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Jan Novák\r\nTEL:+420 123\r\nEND:VCARD\r\n");
            Files["vcf"] = Path.Combine(Dir, "kontakty.vcf");

            // A letter with an inline picture and an attachment, and a saved web page that pulls in a picture by URL.
            var letter = new MimeKit.BodyBuilder { HtmlBody = "<p>Ahoj, <b>světe</b></p><img src=\"cid:logo\">" };
            letter.LinkedResources.Add("logo.png", File.ReadAllBytes(png)).ContentId = "logo";
            letter.Attachments.Add("tabulka.csv", File.ReadAllBytes(Files["csv"]));
            var mail = new MimeKit.MimeMessage { Subject = "Pozdrav", Body = letter.ToMessageBody() };
            mail.From.Add(new MimeKit.MailboxAddress("Jan", "jan@example.com"));
            mail.To.Add(new MimeKit.MailboxAddress("Eva", "eva@example.com"));
            Files["eml"] = Path.Combine(Dir, "dopis.eml");
            mail.WriteTo(Files["eml"]);
            var page = new MimeKit.Multipart("related")
            {
                new MimeKit.TextPart("html") { Text = "<html><body><h1>Stránka</h1><img src=\"https://example.com/a.png\"></body></html>" },
                new MimeKit.MimePart("image", "png") { Content = new MimeKit.MimeContent(File.OpenRead(png)), ContentLocation = new Uri("https://example.com/a.png") },
            };
            Files["mht"] = Path.Combine(Dir, "stránka.mht");
            new MimeKit.MimeMessage { Body = page }.WriteTo(Files["mht"]);
            Files["msg"] = Path.Combine(Dir, "outlook.msg");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Samples", "EmailWithAttachments.msg"), Files["msg"]);

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
                Ffmpeg("-i", Files["mp4"], "-i", Files["srt"], "-map", "0", "-map", "1", "-c", "copy", "-c:s", "srt", Path.Combine(Dir, "s titulky.mkv"));
                Files["subs"] = Path.Combine(Dir, "s titulky.mkv");
                Ffmpeg("-i", Files["mp4"], "-c", "copy", "-f", "mpegts", Path.Combine(Dir, "kamera.m2ts"));
                Files["m2ts"] = Path.Combine(Dir, "kamera.m2ts");
                Ffmpeg("-f", "lavfi", "-i", "sine=frequency=330:duration=1", "-c:a", "wavpack", Path.Combine(Dir, "bezztrát.wv"));
                Files["wv"] = Path.Combine(Dir, "bezztrát.wv");
                Ffmpeg("-f", "lavfi", "-i", "sine=frequency=550:duration=1", "-c:a", "ac3", Path.Combine(Dir, "kino.ac3"));
                Files["ac3"] = Path.Combine(Dir, "kino.ac3");
            }
            // Markup that only pandoc reads.
            File.WriteAllText(Path.Combine(Dir, "článek.tex"), """
                \documentclass{article}
                \begin{document}
                \section{Nadpis}
                Příliš žluťoučký kůň.
                \end{document}
                """, new UTF8Encoding(false));
            Files["tex"] = Path.Combine(Dir, "článek.tex");
            File.WriteAllText(Path.Combine(Dir, "návod.rst"), "Nadpis\n======\n\n- jedna\n- dvě\n", new UTF8Encoding(false));
            Files["rst"] = Path.Combine(Dir, "návod.rst");
            File.WriteAllText(Path.Combine(Dir, "poznámky.org"), "* Nadpis\n** Úkol\nText.\n", new UTF8Encoding(false));
            Files["org"] = Path.Combine(Dir, "poznámky.org");
            File.WriteAllText(Path.Combine(Dir, "sešit.ipynb"),
                "{\"cells\":[{\"cell_type\":\"markdown\",\"metadata\":{},\"source\":[\"# Nadpis\"]}],\"metadata\":{},\"nbformat\":4,\"nbformat_minor\":5}", new UTF8Encoding(false));
            Files["ipynb"] = Path.Combine(Dir, "sešit.ipynb");

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
        "png", "jpg", "svg", "pdf", "md", "html", "txt", "xlsx", "csv", "srt", "folder", "zip", "tgz", "mp4", "avi", "wav", "mp3", "gif", "docx", "pptx",
        "psd", "tga", "jxl", "jp2", "exr", "qoi", "dds", "ppm", "subs", "m2ts", "wv", "ac3", "tex", "rst", "org", "ipynb",
        "ass", "lrc", "json", "yaml", "toml", "xml", "vcf", "eml", "mht", "msg");

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
                // Every text route (TXT, MD, and DOCX/HTML/EPUB once Pandoc is installed) needs a text layer.
                catch (ConversionException ex) when (key is "pdf" && !_s.PdfHasText && ex.Message.Contains("contains no text"))
                {
                    output.WriteLine($"ok   {op.Id} (expected: image-only PDF has no text)");
                }
                catch (ConversionException ex) when (op.Id == "sheet.to.vcf" && ex.Message.Contains("no name, phone or email"))
                {
                    output.WriteLine($"ok   {op.Id} (expected: not a contact list)");
                }
                catch (ConversionException ex) when (op.Id == "video.subtitles" && key != "subs" && ex.Message.Contains("no text subtitles"))
                {
                    output.WriteLine($"ok   {op.Id} (expected: no subtitles in this sample)");
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

    [Fact]
    public async Task Image_batch_runs_side_by_side_with_distinct_names()
    {
        var dir = Path.Combine(_s.Dir, "batch");
        Directory.CreateDirectory(dir);
        var items = new[] { "png", "jpg", "bmp", "gif", "tiff", "webp" }.Select(ext =>
        {
            var path = Path.Combine(dir, "same." + ext);
            File.Copy(_s.Files["png"], path);
            return new FileItem(path);
        }).ToList();
        var op = (PerFileOperation)Catalog.Build().Single(o => o.Id == "image.to.avif");
        Assert.True(op.Parallelism >= 1);
        var result = await op.ExecuteAsync(items, _s.Context(Path.Combine(dir, "out")));
        Assert.Null(result.Message);
        Assert.Equal(items.Count, result.Outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result.Outputs, o => Assert.True(File.Exists(o)));
    }
}
