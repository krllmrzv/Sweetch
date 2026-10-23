using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>
/// Subtitles between SRT, WebVTT, ASS/SSA, LRC, SAMI, TTML and MicroDVD, and to plain or timed text.
/// Every output is UTF-8, whatever code page the source was saved in.
/// </summary>
public static partial class SubtitleBackend
{
    public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

    [GeneratedRegex(@"^\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})")]
    private static partial Regex TimingRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssTagRegex();

    [GeneratedRegex(@"\[(\d+):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex LrcStampRegex();

    [GeneratedRegex(@"<sync[^>]*start\s*=\s*""?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SamiSyncRegex();

    [GeneratedRegex(@"^\{(\d+)\}\{(\d*)\}(.*)$")]
    private static partial Regex MicroDvdRegex();

    /// <summary>Subtitles are read from SRT/VTT; the other formats go through <see cref="ParseAny"/>.</summary>
    public static List<Cue> Parse(string text)
    {
        var cues = new List<Cue>();
        var blocks = Regex.Split(Normalize(text), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n').ToList();
            var timingIndex = lines.FindIndex(l => TimingRegex().IsMatch(l));
            if (timingIndex < 0)
                continue; // WEBVTT header, NOTE, STYLE, …
            var m = TimingRegex().Match(lines[timingIndex]);
            var body = string.Join("\n", lines.Skip(timingIndex + 1)).Trim();
            cues.Add(new Cue(ParseTimestamp(m.Groups[1].Value), ParseTimestamp(m.Groups[2].Value), body));
        }
        return cues;
    }

    public static List<Cue> ParseAny(string text, string format) => format switch
    {
        "ass" or "ssa" => ParseAss(text),
        "lrc" => ParseLrc(text),
        "smi" => ParseSami(text),
        "ttml" => ParseTtml(text),
        "sub" => ParseMicroDvd(text),
        _ => Parse(text),
    };

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');

    /// <summary>
    /// [Events] lines: "Dialogue: layer,start,end,style,…,text". The Format line says where the times and the
    /// text are; the text is the last field and may itself hold commas.
    /// </summary>
    public static List<Cue> ParseAss(string text)
    {
        var cues = new List<Cue>();
        string[] format = ["layer", "start", "end", "style", "name", "marginl", "marginr", "marginv", "effect", "text"];
        var inEvents = false;
        foreach (var raw in Normalize(text).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
            else if (inEvents && line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                format = line[7..].Split(',').Select(f => f.Trim().ToLowerInvariant()).ToArray();
            else if (inEvents && line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                var fields = line[9..].Split(',', format.Length);
                if (fields.Length < format.Length)
                    continue;
                var body = AssTagRegex().Replace(fields[^1], "").Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ").Trim();
                cues.Add(new Cue(ParseTimestamp(fields[Array.IndexOf(format, "start")].Trim()), ParseTimestamp(fields[Array.IndexOf(format, "end")].Trim()), body));
            }
        }
        return [.. cues.OrderBy(c => c.Start)];
    }

    /// <summary>Song lyrics: "[01:02.50]line", several stamps per line allowed; a line lasts until the next one.</summary>
    public static List<Cue> ParseLrc(string text)
    {
        var lines = new List<(TimeSpan At, string Text)>();
        foreach (var line in Normalize(text).Split('\n'))
        {
            var stamps = LrcStampRegex().Matches(line);
            if (stamps.Count == 0)
                continue; // [ar:…], [ti:…] and other tags
            var body = line[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match s in stamps)
                lines.Add((TimeSpan.FromMinutes(int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture)) + TimeSpan.FromSeconds(double.Parse(s.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture)), body));
        }
        lines.Sort((a, b) => a.At.CompareTo(b.At));
        return Timeline(lines);
    }

    /// <summary>SAMI (.smi): HTML-like blocks, each &lt;SYNC Start=ms&gt; replacing the previous text; "&amp;nbsp;" clears it.</summary>
    public static List<Cue> ParseSami(string text)
    {
        var lines = new List<(TimeSpan At, string Text)>();
        var syncs = SamiSyncRegex().Matches(text);
        for (var i = 0; i < syncs.Count; i++)
        {
            var end = i + 1 < syncs.Count ? syncs[i + 1].Index : text.Length;
            var chunk = text[syncs[i].Index..end];
            chunk = chunk[(chunk.IndexOf('>') + 1)..];
            // SAMI files often hold one paragraph per language; the first is the one shown by default.
            var p = Regex.Match(chunk, @"<p[^>]*>(.*?)(?=<p[\s>]|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var body = p.Success ? p.Groups[1].Value : chunk;
            body = Regex.Replace(body, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            body = WebUtility.HtmlDecode(TagRegex().Replace(body, "")).Replace('\u00A0', ' ');
            body = string.Join("\n", body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            lines.Add((TimeSpan.FromMilliseconds(long.Parse(syncs[i].Groups[1].Value, CultureInfo.InvariantCulture)), body));
        }
        return Timeline(lines);
    }

    /// <summary>TTML / DFXP: &lt;p begin end|dur&gt; in any namespace, with the document's own frame and tick rates.</summary>
    public static List<Cue> ParseTtml(string text)
    {
        var cues = new List<Cue>();
        var document = XDocument.Parse(text);
        var rate = TtmlRate.Of(document.Root!);
        foreach (var p in document.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            var begin = (string?)p.Attribute("begin");
            if (begin is null)
                continue;
            var start = TtmlTime(begin, rate);
            var end = (string?)p.Attribute("end") is { } e ? TtmlTime(e, rate) : start + TtmlTime((string?)p.Attribute("dur") ?? "3s", rate);
            var body = new StringBuilder();
            foreach (var node in p.DescendantNodes())
            {
                if (node is XText t)
                    body.Append(t.Value);
                else if (node is XElement { Name.LocalName: "br" })
                    body.Append('\n');
            }
            cues.Add(new Cue(start, end, string.Join("\n", body.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))));
        }
        return cues;
    }

    /// <summary>
    /// ttp:frameRate (× frameRateMultiplier, default 30) for "f" and hh:mm:ss:ff; ttp:tickRate for "t", which
    /// defaults to the frame rate when one is given and to 1 otherwise (TTML 1, 10.2.10).
    /// </summary>
    private sealed record TtmlRate(double Fps, double Ticks)
    {
        public static TtmlRate Of(XElement root)
        {
            string? Param(string name) => root.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
            double Number(string? s, double fallback) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
            var fps = Number(Param("frameRate"), 30);
            if (Param("frameRateMultiplier")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var n, var d])
                fps *= Number(n, 1) / Number(d, 1);
            return new TtmlRate(fps, Number(Param("tickRate"), Param("frameRate") is null ? 1 : fps));
        }
    }

    private static TimeSpan TtmlTime(string value, TtmlRate rate)
    {
        value = value.Trim();
        if (value.Contains(':'))
        {
            var parts = value.Split(':');
            if (parts.Length == 4) // hh:mm:ss:frames
                return ParseTimestamp(string.Join(':', parts[..3])) + TimeSpan.FromSeconds(double.Parse(parts[3], CultureInfo.InvariantCulture) / rate.Fps);
            return ParseTimestamp(value);
        }
        var unit = value.TrimStart("0123456789.".ToCharArray());
        var number = double.Parse(value[..^unit.Length], CultureInfo.InvariantCulture);
        return unit switch
        {
            "h" => TimeSpan.FromHours(number),
            "m" => TimeSpan.FromMinutes(number),
            "ms" => TimeSpan.FromMilliseconds(number),
            "f" => TimeSpan.FromSeconds(number / rate.Fps),
            "t" => TimeSpan.FromSeconds(number / rate.Ticks),
            _ => TimeSpan.FromSeconds(number),
        };
    }

    /// <summary>MicroDVD: "{start frame}{end frame}text|second line". A first cue of "{1}{1}25" names the frame rate.</summary>
    public static List<Cue> ParseMicroDvd(string text)
    {
        var cues = new List<Cue>();
        var fps = 23.976;
        foreach (var line in Normalize(text).Split('\n'))
        {
            var m = MicroDvdRegex().Match(line.Trim());
            if (!m.Success)
                continue;
            long from = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            long to = m.Groups[2].Value.Length > 0 ? long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : from + (long)(fps * 3);
            var body = m.Groups[3].Value;
            if (cues.Count == 0 && from <= 1 && double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate is > 1 and < 200)
            {
                fps = rate;
                continue;
            }
            body = AssTagRegex().Replace(body, "").Replace('|', '\n').Trim();
            cues.Add(new Cue(TimeSpan.FromSeconds(from / fps), TimeSpan.FromSeconds(to / fps), body));
        }
        return cues;
    }

    /// <summary>Lines that each last until the next one starts (LRC, SAMI); blank lines only end the previous one.</summary>
    private static List<Cue> Timeline(List<(TimeSpan At, string Text)> lines)
    {
        var cues = new List<Cue>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Text.Length == 0)
                continue;
            var end = i + 1 < lines.Count ? lines[i + 1].At : lines[i].At + TimeSpan.FromSeconds(4);
            cues.Add(new Cue(lines[i].At, end, lines[i].Text));
        }
        return cues;
    }

    public static TimeSpan ParseTimestamp(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        var hours = parts.Length == 3 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        var seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        return new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
    }

    private static string Stamp(TimeSpan t, char separator) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}");

    public static string ToSrt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        var n = 1;
        foreach (var c in cues)
        {
            sb.Append(n++).Append('\n')
              .Append(Stamp(c.Start, ',')).Append(" --> ").Append(Stamp(c.End, ',')).Append('\n')
              .Append(c.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var c in cues)
        {
            sb.Append(Stamp(c.Start, '.')).Append(" --> ").Append(Stamp(c.End, '.')).Append('\n')
              .Append(c.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToAss(IEnumerable<Cue> cues)
    {
        static string Time(TimeSpan t) => string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}");
        var sb = new StringBuilder("""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 1920
            PlayResY: 1080

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,64,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,3,1,2,60,60,50,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text

            """.Replace("\r\n", "\n"));
        foreach (var c in cues)
            sb.Append("Dialogue: 0,").Append(Time(c.Start)).Append(',').Append(Time(c.End)).Append(",Default,,0,0,0,,").Append(Plain(c.Text).Replace("\n", "\\N")).Append('\n');
        return sb.ToString();
    }

    public static string ToLrc(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        foreach (var c in cues)
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"[{(int)c.Start.TotalMinutes:00}:{c.Start.Seconds:00}.{c.Start.Milliseconds / 10:00}]")).Append(Plain(c.Text).Replace('\n', ' ')).Append('\n');
        return sb.ToString();
    }

    private static string Plain(string text) => TagRegex().Replace(text, "").Trim();

    public static string ToText(IEnumerable<Cue> cues) => Lines(cues, null);

    /// <summary>One line per cue with its start time: "[00:01:02] text".</summary>
    public static string ToTimedText(IEnumerable<Cue> cues) =>
        Lines(cues, t => string.Create(CultureInfo.InvariantCulture, $"[{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}] "));

    private static string Lines(IEnumerable<Cue> cues, Func<TimeSpan, string>? stamp)
    {
        var sb = new StringBuilder();
        string? previous = null;
        foreach (var c in cues)
        {
            var line = Plain(c.Text).Replace('\n', ' ').Trim();
            if (line.Length == 0 || line == previous)
                continue;
            sb.Append(stamp?.Invoke(c.Start)).Append(line).Append('\n');
            previous = line;
        }
        return sb.ToString();
    }

    /// <summary>
    /// UTF-8 or UTF-16 with a BOM as marked; UTF-8 when the bytes are valid UTF-8; otherwise the old
    /// Windows code page whose letters fit: Cyrillic 1251 when most letters come out Cyrillic, else 1252.
    /// </summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..] or [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
        {
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cyrillic = Encoding.GetEncoding(1251).GetString(bytes);
            var letters = cyrillic.Count(char.IsLetter);
            return cyrillic.Count(c => c is >= '\u0400' and <= '\u04FF') * 2 > letters ? cyrillic : Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    public static async Task<string> ConvertAsync(FileItem item, string format, OperationContext ctx, string? name = null)
    {
        // "timed" is plain text with start times, saved as .txt.
        var extension = format == "timed" ? "txt" : format;
        name ??= format == "timed" ? $"{item.BaseName} ({T("timed", "с таймкодами")})" : item.BaseName;
        var cues = ParseAny(await Task.Run(() => ReadText(item.Path), ctx.CancellationToken).ConfigureAwait(false), item.Format);
        if (cues.Count == 0)
            throw new ConversionException(T("The file contains no subtitles.", "В файле нет субтитров."));
        return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), name, extension, tmp =>
            File.WriteAllTextAsync(tmp, Write(cues, format), new UTF8Encoding(false), ctx.CancellationToken)).ConfigureAwait(false);
    }

    public static string Write(IEnumerable<Cue> cues, string format) => format switch
    {
        "srt" => ToSrt(cues),
        "vtt" => ToVtt(cues),
        "ass" => ToAss(cues),
        "lrc" => ToLrc(cues),
        "timed" => ToTimedText(cues),
        _ => ToText(cues),
    };

    /// <summary>Rewrites the file as UTF-8 in its own format: fixes "РџСЂРёРІРµС‚" in players that expect UTF-8.</summary>
    public static Task<string> ToUtf8Async(FileItem item, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} (UTF-8)", item.Format, async tmp =>
            await File.WriteAllTextAsync(tmp, await Task.Run(() => ReadText(item.Path), ctx.CancellationToken).ConfigureAwait(false), new UTF8Encoding(false), ctx.CancellationToken).ConfigureAwait(false));
}
