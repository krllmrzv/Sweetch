using Sweetch.Core;
using Sweetch.Core.Backends;
using Sweetch.Core.Operations;

namespace Sweetch.Tests;

public class FormatTests
{
    [Theory]
    [InlineData("a/photo.JPEG", "jpg")]
    [InlineData("a/scan.tif", "tiff")]
    [InlineData("a/backup.tar.gz", "tar.gz")]
    [InlineData("a/backup.tgz", "tar.gz")]
    [InlineData("a/readme.markdown", "md")]
    [InlineData("a/noext", "")]
    public void KeyOf_normalizes(string path, string expected) => Assert.Equal(expected, Formats.KeyOf(path));

    [Fact]
    public void BaseName_handles_double_extension() =>
        Assert.Equal("backup", Formats.BaseNameOf("x/backup.tar.gz"));

    [Fact]
    public void Natural_sort_orders_numbers()
    {
        var sorted = new[] { "p10.pdf", "p2.pdf", "p1.pdf" }.OrderBy(s => s, NaturalComparer.Instance);
        Assert.Equal(["p1.pdf", "p2.pdf", "p10.pdf"], sorted);
    }
}

public class OutputPathTests
{
    [Fact]
    public void Unique_appends_counter()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.png"), "");
            File.WriteAllText(Path.Combine(dir, "a (2).png"), "");
            Assert.Equal(Path.Combine(dir, "a (3).png"), OutputPaths.Unique(dir, "a", "png"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Produce_cleans_up_on_failure()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => OutputPaths.ProduceAsync(dir, "x", "txt", tmp =>
            {
                File.WriteAllText(tmp, "partial");
                throw new InvalidOperationException();
            }));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }
}

public class SubtitleTests
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,500\nAhoj\n\n2\n00:00:03,000 --> 00:00:04,000\n<i>Světe</i>\n";

    [Fact]
    public void Srt_to_vtt_and_back()
    {
        var cues = SubtitleBackend.Parse(Srt);
        Assert.Equal(2, cues.Count);
        var vtt = SubtitleBackend.ToVtt(cues);
        Assert.StartsWith("WEBVTT", vtt);
        Assert.Contains("00:00:01.000 --> 00:00:02.500", vtt);
        var back = SubtitleBackend.Parse(vtt);
        Assert.Equal(cues, back);
        Assert.Contains("00:00:01,000 --> 00:00:02,500", SubtitleBackend.ToSrt(back));
    }

    [Fact]
    public void Vtt_without_hours_parses() =>
        Assert.Equal(TimeSpan.FromSeconds(75.5), SubtitleBackend.ParseTimestamp("01:15.500"));

    [Fact]
    public void Text_strips_tags() =>
        Assert.Equal("Ahoj\nSvěte\n", SubtitleBackend.ToText(SubtitleBackend.Parse(Srt)));
}

public class SpreadsheetTests
{
    [Fact]
    public void Parses_quotes_and_newlines()
    {
        var rows = SpreadsheetBackend.ParseDelimited("a;\"b;c\";\"d\"\"e\"\r\n\"multi\nline\";2;3", ';');
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b;c", "d\"e"], rows[0]);
        Assert.Equal("multi\nline", rows[1][0]);
    }

    [Fact]
    public void Guesses_semicolon() => Assert.Equal(';', SpreadsheetBackend.GuessDelimiter("a;b;c\n1;2;3"));

    [Fact]
    public void Writes_escaped_csv() =>
        Assert.Equal("a,\"b,c\",\"x\"\"y\"\r\n", SpreadsheetBackend.ToDelimited([["a", "b,c", "x\"y"]], ','));
}

public class MiscTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03.5", 3723.5)]
    [InlineData("0,5", 0.5)]
    public void ParseTime(string text, double seconds) => Assert.Equal(seconds, FfmpegBackend.ParseTime(text), 3);

    [Fact]
    public void Archive_rejects_path_traversal()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        Assert.Throws<ConversionException>(() => ArchiveBackend.SafeTarget(root + "x" + Path.DirectorySeparatorChar, "../../evil.txt"));
        Assert.EndsWith("ok.txt", ArchiveBackend.SafeTarget(root, "sub/ok.txt"));
    }

    [Fact]
    public void Catalog_ids_are_unique()
    {
        var ids = Catalog.Build().Select(o => o.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Convert_wheel_hides_same_format()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var png = Path.Combine(dir, "a.png");
            File.WriteAllBytes(png, [0]);
            var ops = Catalog.For(Catalog.Build(), WheelKind.Convert, [new FileItem(png)], new ToolLocator(dir));
            Assert.DoesNotContain(ops, o => o.Label == "PNG");
            Assert.Contains(ops, o => o.Label == "JPG");
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public void Gpu_encoding_swaps_only_the_x264_options()
    {
        var cpu = FfmpegBackend.CodecArgs("mp4", new Settings { VideoCrf = 23 }).ToList();
        var nvenc = FfmpegBackend.OnGpu(cpu, "h264_nvenc");
        Assert.Equal(["-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr", "-cq", "27", "-b:v", "0", "-pix_fmt", "yuv420p"], nvenc.Take(12));
        Assert.DoesNotContain("libx264", nvenc);
        Assert.Equal(cpu.SkipWhile(a => a != "-vf"), nvenc.SkipWhile(a => a != "-vf")); // filters and audio untouched
        var qsv = FfmpegBackend.OnGpu(cpu, "h264_qsv");
        Assert.Contains("nv12", qsv);
        Assert.Equal("27", qsv[qsv.IndexOf("-global_quality") + 1]);
        // x265's CRF already matches the card's HEVC scale, and its log option means nothing there.
        var hevc = FfmpegBackend.OnGpu(FfmpegBackend.CodecArgs("hevc", new Settings { VideoCrf = 23 }).ToList(), "hevc_nvenc");
        Assert.Equal("28", hevc[hevc.IndexOf("-cq") + 1]);
        Assert.DoesNotContain("-x265-params", hevc);
        Assert.Contains("hvc1", hevc);
    }
}
