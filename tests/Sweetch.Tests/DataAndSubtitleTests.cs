using System.Text;
using System.Text.Json.Nodes;
using Sweetch.Core;
using Sweetch.Core.Backends;

namespace Sweetch.Tests;

public class DataAndSubtitleTests
{
    [Fact]
    public void Every_subtitle_format_reads_into_the_same_cues()
    {
        var ass = """
            [Script Info]
            Title: x

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,{\i1}Привет,{\i0} мир\Nвторая
            """;
        var lrc = "[ti:Song]\n[00:01.00]Привет, мир\n[00:02.50]\n";
        var sami = "<SAMI><BODY><SYNC Start=1000><P Class=RUCC>Привет,<br>мир<SYNC Start=2500><P Class=RUCC>&nbsp;</BODY></SAMI>";
        var ttml = """<tt xmlns="http://www.w3.org/ns/ttml"><body><div><p begin="00:00:01.000" end="2.5s">Привет,<br/>мир</p></div></body></tt>""";
        var microDvd = "{1}{1}25\n{25}{62}Привет,|мир\n";

        Assert.Equal(new SubtitleBackend.Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), "Привет, мир\nвторая"), SubtitleBackend.ParseAss(ass).Single());
        Assert.Equal(new SubtitleBackend.Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), "Привет, мир"), SubtitleBackend.ParseLrc(lrc).Single());
        Assert.Equal(new SubtitleBackend.Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), "Привет,\nмир"), SubtitleBackend.ParseSami(sami).Single());
        Assert.Equal(new SubtitleBackend.Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), "Привет,\nмир"), SubtitleBackend.ParseTtml(ttml).Single());
        Assert.Equal(new SubtitleBackend.Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.48), "Привет,\nмир"), SubtitleBackend.ParseMicroDvd(microDvd).Single());

        var cues = SubtitleBackend.ParseAss(ass);
        Assert.Equal("[00:00:01] Привет, мир вторая\n", SubtitleBackend.ToTimedText(cues));
        Assert.Equal(cues, SubtitleBackend.ParseAss(SubtitleBackend.ToAss(cues)));
        Assert.Equal("[00:01.00]Привет, мир вторая\n", SubtitleBackend.ToLrc(cues));
    }

    [Fact]
    public void Ttml_times_follow_the_documents_frame_and_tick_rates()
    {
        static TimeSpan Start(string root, string begin) => SubtitleBackend.ParseTtml(
            $"""<tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttp="http://www.w3.org/ns/ttml#parameter" {root}><body><div><p begin="{begin}" dur="1s">x</p></div></body></tt>""").Single().Start;
        Assert.Equal(TimeSpan.FromSeconds(1), Start("""ttp:frameRate="25" """, "25f"));
        Assert.Equal(TimeSpan.FromSeconds(1), Start("""ttp:tickRate="10" """, "10t"));
        Assert.Equal(TimeSpan.FromSeconds(1), Start("", "1t")); // no rates: one tick per second
        Assert.Equal(TimeSpan.FromSeconds(2.5), Start("""ttp:frameRate="24" """, "00:00:02:12"));
    }

    [Fact]
    public void Stylesheets_get_pictures_listed_after_them()
    {
        var css = new MimeKit.TextPart("css") { Text = "body{background:url(https://example.com/bg.png)}", ContentLocation = new Uri("https://example.com/a.css") };
        var page = new MimeKit.Multipart("related")
        {
            new MimeKit.TextPart("html") { Text = "<link rel=stylesheet href=\"https://example.com/a.css\">" },
            css,
            new MimeKit.MimePart("image", "png") { Content = new MimeKit.MimeContent(new MemoryStream([1, 2, 3])), ContentLocation = new Uri("https://example.com/bg.png") },
        };
        var html = MailBackend.ToHtml(new MimeKit.MimeMessage { Body = page }, MailBackend.DataUri, page: true);
        var embedded = Encoding.UTF8.GetString(Convert.FromBase64String(html.Split("data:text/css;base64,")[1].Split('"')[0]));
        Assert.Contains("data:image/png;base64,AQID", embedded);
    }

    [Fact]
    public void Old_code_pages_are_read_as_what_they_are()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dir = OutputPaths.TempDirectory();
        try
        {
            var cyrillic = Path.Combine(dir, "ru.srt");
            File.WriteAllBytes(cyrillic, Encoding.GetEncoding(1251).GetBytes("Съешь же ещё этих мягких булок"));
            Assert.Equal("Съешь же ещё этих мягких булок", SubtitleBackend.ReadText(cyrillic));
            var western = Path.Combine(dir, "de.srt");
            File.WriteAllBytes(western, Encoding.GetEncoding(1252).GetBytes("Größe und Übermaß"));
            Assert.Equal("Größe und Übermaß", SubtitleBackend.ReadText(western));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public void Data_survives_a_trip_through_every_format()
    {
        var source = JsonNode.Parse("""{"app":{"name":"Sweetch","версия":2,"beta":false,"zip":"007","tags":["a","b"],"people":[{"n":"Аня","age":30},{"n":"Боб","age":41.5}]}}""");
        foreach (var format in new[] { "yaml", "toml", "json", "jsonl" })
        {
            var dir = OutputPaths.TempDirectory();
            var path = Path.Combine(dir, "data." + format);
            File.WriteAllText(path, DataBackend.Write(source, format));
            var back = DataBackend.Read(path, format);
            Assert.True(JsonNode.DeepEquals(format == "jsonl" ? new JsonArray(source!.DeepClone()) : source, back), $"{format}:\n{File.ReadAllText(path)}");
            OutputPaths.TryDelete(dir);
        }
        // XML has no types: values come back as text, structure stays.
        var xmlDir = OutputPaths.TempDirectory();
        var xml = Path.Combine(xmlDir, "data.xml");
        File.WriteAllText(xml, DataBackend.Write(source, "xml"));
        var fromXml = DataBackend.Read(xml, "xml")!;
        Assert.Equal("Аня", (string?)fromXml["app"]!["people"]![0]!["n"]);
        Assert.Equal("2", (string?)fromXml["app"]!["версия"]);
        OutputPaths.TryDelete(xmlDir);

        var rows = DataBackend.ToRows(source);
        Assert.Equal(["n", "age"], rows[0]);
        Assert.Equal(["Боб", "41.5"], rows[2]);
        var nested = DataBackend.FromRows([["name", "address.city", "zip"], ["Аня", "Казань", "007"]]);
        Assert.Equal("""[{"name":"Аня","address":{"city":"Казань"},"zip":"007"}]""", nested.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    [Fact]
    public void Each_helper_program_owns_its_formats()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var coverage = ToolCoverage.Build(Sweetch.Core.Operations.Catalog.Build());
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
        Assert.Empty(ToolCoverage.For(coverage, "png"));
        Assert.Equal([ExternalTool.Ffmpeg], ToolCoverage.For(coverage, ".MP4"));
        // TEX → PDF goes through both: pandoc writes a DOCX, LibreOffice prints it.
        Assert.Equal([ExternalTool.Pandoc, ExternalTool.LibreOffice], ToolCoverage.For(coverage, "notes.tex"));
        Assert.Contains(ExternalTool.LibreOffice, ToolCoverage.For(coverage, "pages"));
        // A tool on the wheel, not only conversions: "Compress" for video comes with FFmpeg.
        Assert.Contains(coverage[ExternalTool.Ffmpeg], g => g.Formats.Contains("mkv") && g.Tools.Count > 0);
    }

    [Fact]
    public void Page_ranges_read_like_a_print_dialog()
    {
        Assert.Equal([0, 1, 2, 4, 7, 8, 9], PdfBackend.ParsePages("1-3, 5; 8-", 10));
        Assert.Equal([0, 1], PdfBackend.ParsePages("-2", 10));
        Assert.Equal([3], PdfBackend.ParsePages(" 4 ", 10));
        Assert.Throws<ConversionException>(() => PdfBackend.ParsePages("9-12", 10));
        Assert.Throws<ConversionException>(() => PdfBackend.ParsePages("abc", 10));
    }

    [Fact]
    public void Letters_become_a_page_with_their_pictures()
    {
        var body = new MimeKit.BodyBuilder { HtmlBody = "<img src=\"cid:a\">" };
        body.LinkedResources.Add("a.png", [1, 2, 3]).ContentId = "a";
        var mail = new MimeKit.MimeMessage { Subject = "Тема", Body = body.ToMessageBody() };
        mail.From.Add(new MimeKit.MailboxAddress("Аня", "a@example.com"));
        var html = MailBackend.ToHtml(mail, MailBackend.DataUri);
        Assert.Contains("src=\"data:image/png;base64,AQID\"", html);
        Assert.Contains("Аня", html);
        Assert.Contains("<title>Тема</title>", html);
    }

    [Fact]
    public void Contacts_go_to_a_table_and_back()
    {
        var vcf = "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:=D0=98=D0=B2=D0=B0=D0=BD=D0=BE=D0=B2;=D0=98=D0=B2=D0=B0=\r\n=D0=BD\r\n"
            + "FN:Иван Иванов\r\nitem1.TEL;TYPE=CELL:+7 900 000-00-01\r\nTEL:+7 900 000-00-02\r\nEMAIL:ivan@example.com\r\nNOTE:строка\\, вторая\r\nEND:VCARD\r\n";
        var rows = VCard.ToRows(vcf);
        Assert.Equal(["Name", "Last name", "First name", "Phone", "Email", "Note"], rows[0]);
        Assert.Equal(["Иван Иванов", "Иванов", "Иван", "+7 900 000-00-01; +7 900 000-00-02", "ivan@example.com", "строка, вторая"], rows[1]);

        var back = VCard.FromRows(rows);
        Assert.Contains("TEL:+7 900 000-00-02\r\n", back);
        Assert.Equal(rows, VCard.ToRows(back));
        // Someone else's columns: Google's export, Russian headers.
        Assert.Contains("FN:Аня\r\n", VCard.FromRows([["Фамилия", "Имя", "Телефон"], ["", "Аня", "+7 1"]]));
        Assert.Throws<ConversionException>(() => VCard.FromRows([["Город"], ["Казань"]]));
    }
}
