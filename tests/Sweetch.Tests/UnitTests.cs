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

public class RateTests
{
    [Theory]
    [InlineData("29.97", 29.97)]
    [InlineData("29,97", 29.97)]
    [InlineData(" 30000/1001 ", 30000.0 / 1001)]
    public void ParseRate_reads_numbers_and_fractions(string text, double expected) => Assert.Equal(expected, FfmpegBackend.ParseRate(text)!.Value, 9);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1/0")]
    [InlineData("1/2/3")]
    public void ParseRate_rejects_junk(string text) => Assert.Null(FfmpegBackend.ParseRate(text));

    [Theory]
    [InlineData(2, "atempo=2")]
    [InlineData(0.5, "atempo=0.5")]
    [InlineData(0.25, "atempo=0.5,atempo=0.5")]
    [InlineData(0.1, "atempo=0.5,atempo=0.5,atempo=0.5,atempo=0.8")]
    public void Atempo_chains_below_half_speed(double speed, string expected) => Assert.Equal(expected, Catalog.Atempo(speed));
}

public class RelightTests
{
    [Fact]
    public void Neutral_values_pass_the_picture_through() =>
        Assert.Equal("null", Relight.Filter(Relight.Parameters().ToDictionary(p => p.Key, p => p.DefaultValue)));

    [Fact]
    public void Filter_uses_invariant_numbers_and_clamps()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
        try
        {
            var filter = Relight.Filter(new Dictionary<string, string> { ["exposure"] = "25", ["warmth"] = "100", ["gamma"] = "500", ["tint"] = "junk" });
            Assert.StartsWith("curves=all='0/0 0.1/0.1277 0.2/", filter);
            Assert.EndsWith(" 1/1',colortemperature=temperature=4288:pl=1,format=yuv444p,eq=contrast=1:brightness=0:gamma=2:saturation=1", filter);
            Assert.DoesNotContain("colorbalance", filter); // "junk" is ignored
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }
}

public class OutputPathTests
{
    [Theory]
    [InlineData("my video (trimmed)", "my_video_trimmed")]
    [InlineData("моё видео (кадр 00-00-01.440)", "moe_video_kadr_00-00-01.440")]
    [InlineData("Щука Юля Объём", "Shchuka_Yulya_Obem")]
    [InlineData("Žluťoučký kůň 東京", "Zlutoucky_kun")]
    [InlineData("a & b's [final]!", "a_b_s_final")]
    [InlineData("()", "file")]
    public void PlainName_has_nothing_to_quote(string name, string expected) => Assert.Equal(expected, OutputPaths.PlainName(name));

    [Fact]
    public void MakePlain_renames_without_overwriting()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "my_clip_trimmed.mp4"), "taken");
            var produced = Path.Combine(dir, "my clip (trimmed).mp4");
            File.WriteAllText(produced, "new");
            Assert.Equal(Path.Combine(dir, "my_clip_trimmed_2.mp4"), OutputPaths.MakePlain(produced, recurse: false));

            var pages = Directory.CreateDirectory(Path.Combine(dir, "doc (pages)")).FullName;
            File.WriteAllText(Path.Combine(pages, "doc - 01.pdf"), "");
            var renamed = OutputPaths.MakePlain(pages, recurse: true);
            Assert.Equal(Path.Combine(dir, "doc_pages"), renamed);
            Assert.True(File.Exists(Path.Combine(renamed, "doc_-_01.pdf")));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

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
}
