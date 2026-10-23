namespace Sweetch.Core;

/// <summary>Extension groups and normalization ("jpeg" → "jpg", "tgz" → "tar.gz").</summary>
public static class Formats
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg",
        ["jpe"] = "jpg",
        ["jfif"] = "jpg",
        ["tif"] = "tiff",
        ["heif"] = "heic",
        ["htm"] = "html",
        ["markdown"] = "md",
        ["aif"] = "aiff",
        ["oga"] = "ogg",
        ["tgz"] = "tar.gz",
        ["m4v"] = "mp4",
        ["pgm"] = "ppm",
        ["pbm"] = "ppm",
        ["pnm"] = "ppm",
        ["j2k"] = "jp2",
        ["jpf"] = "jp2",
        ["dcm"] = "dicom",
        ["m2t"] = "m2ts",
        ["rmvb"] = "rm",
        ["latex"] = "tex",
        ["rest"] = "rst",
        ["asciidoc"] = "adoc",
        ["mediawiki"] = "wiki",
        ["dbk"] = "docbook",
        ["sami"] = "smi",
        ["dfxp"] = "ttml",
        ["yml"] = "yaml",
        ["ndjson"] = "jsonl",
        ["vcard"] = "vcf",
        ["mhtml"] = "mht",
    };

    private static readonly string[] DoubleExtensions = [".tar.gz", ".tar.bz2", ".tar.xz"];

    public static readonly HashSet<string> Images = Set("jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "gif", "svg", "ico",
        "psd", "psb", "tga", "ppm", "pcx", "qoi", "dds", "jxl", "jp2", "exr", "hdr", "cur", "apng", "mng", "xcf", "dicom",
        "svgz", "emf", "wmf");
    public static readonly string[] ImageOutputs = ["jpg", "png", "webp", "avif", "jxl", "tiff", "bmp", "gif", "ico", "pdf"];

    /// <summary>Camera RAW, decoded by LibRaw inside Magick.NET. Read-only: edits are saved as JPG.</summary>
    public static readonly HashSet<string> Raw = Set(
        "dng", "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "raf", "orf", "rw2", "rwl",
        "pef", "srw", "3fr", "fff", "iiq", "erf", "kdc", "dcr", "mrw", "mos", "x3f", "gpr");

    /// <summary>Everything the image operations accept: regular images plus camera RAW.</summary>
    public static readonly HashSet<string> ImageInputs = new(Images.Concat(Raw), StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> Audio = Set("mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "aiff", "wma",
        "amr", "ape", "wv", "tta", "dsf", "dff", "dts", "ac3", "eac3", "mka", "caf", "au", "voc", "w64");
    public static readonly string[] AudioOutputs = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "alac", "wv", "tta", "ac3"];

    public static readonly HashSet<string> Video = Set("mp4", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpg", "mpeg", "3gp", "ts", "mts",
        "ogv", "vob", "m2ts", "mxf", "divx", "asf", "rm", "f4v");
    public static readonly string[] VideoOutputs = ["mp4", "hevc", "mov", "mkv", "webm", "gif", "mp3", "m4a", "avi", "apng"];

    public static readonly HashSet<string> Pdf = Set("pdf");

    /// <summary>Markup only pandoc reads (LaTeX, reStructuredText, Org, Jupyter, FictionBook…). Declared before the sets that use it.</summary>
    public static readonly string[] Markup = ["tex", "rst", "org", "adoc", "typ", "ipynb", "fb2", "textile", "wiki", "docbook", "opml", "djot"];

    public static readonly HashSet<string> Documents = Set(["docx", "doc", "odt", "rtf", "md", "txt", "html", "epub", .. Markup, "docm", "dotx", "dotm", "wps", "wpd", "pages", "fodt", "ott"]);
    /// <summary>What LibreOffice Writer opens: Word in all its flavours, WordPerfect, Works, Apple Pages, OpenDocument.</summary>
    public static readonly HashSet<string> OfficeDocuments = Set("doc", "docx", "odt", "rtf", "txt", "html", "docm", "dotx", "dotm", "wps", "wpd", "pages", "fodt", "ott");
    public static readonly HashSet<string> PandocInputs = Set(["docx", "odt", "rtf", "md", "txt", "html", "epub", .. Markup]);
    public static readonly string[] DocumentOutputs = ["docx", "pdf", "odt", "md", "html", "txt", "epub", "rtf", "tex", "rst", "typ", "adoc", "pptx"];

    public static readonly HashSet<string> Slides = Set("pptx", "ppt", "odp", "pptm", "potx", "potm", "ppsx", "pps", "otp", "fodp", "key");
    public static readonly string[] SlideOutputs = ["pdf", "png", "jpg", "pptx", "odp", "docx"];

    public static readonly HashSet<string> Spreadsheets = Set("xlsx", "xlsm", "xls", "ods", "csv", "tsv", "xlt", "xltx", "xltm", "ots", "fods", "numbers", "et");
    public static readonly string[] SpreadsheetOutputs = ["xlsx", "csv", "tsv", "ods", "pdf", "html"];

    public static readonly HashSet<string> Subtitles = Set("srt", "vtt", "ass", "ssa", "lrc", "smi", "ttml", "sub");
    /// <summary>"timed" is a .txt with a start time on every line.</summary>
    public static readonly string[] SubtitleOutputs = ["srt", "vtt", "ass", "lrc", "txt", "timed"];

    /// <summary>Letters and saved web pages (MHTML is a letter-shaped bundle too).</summary>
    public static readonly HashSet<string> Mail = Set("eml", "msg", "mht");

    public static readonly HashSet<string> Data = Set("json", "jsonl", "yaml", "toml", "xml");
    public static readonly string[] DataOutputs = ["json", "yaml", "toml", "xml", "jsonl", "csv", "xlsx"];

    public static readonly HashSet<string> Archives = Set("zip", "tar", "tar.gz", "gz", "tar.bz2", "bz2", "tar.xz", "xz", "rar", "7z");
    public static readonly string[] ArchiveOutputs = ["zip", "tar", "tar.gz"];

    /// <summary>Every format some operation reads.</summary>
    public static IReadOnlyList<string> All =>
        [.. new[] { ImageInputs, Audio, Video, Pdf, Documents, Slides, Spreadsheets, Subtitles, Data, Mail, Archives }.SelectMany(s => s).Append("vcf").Distinct(StringComparer.OrdinalIgnoreCase)];

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string ext)
    {
        ext = ext.TrimStart('.').ToLowerInvariant();
        return Aliases.TryGetValue(ext, out var n) ? n : ext;
    }

    public static string KeyOf(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        foreach (var d in DoubleExtensions)
        {
            if (name.EndsWith(d, StringComparison.Ordinal) && name.Length > d.Length)
                return d[1..];
        }
        return Normalize(Path.GetExtension(name));
    }

    public static string BaseNameOf(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (Directory.Exists(path))
            return name;
        foreach (var d in DoubleExtensions)
        {
            if (name.EndsWith(d, StringComparison.OrdinalIgnoreCase) && name.Length > d.Length)
                return name[..^d.Length];
        }
        return Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>Upper-case display label: "jpg" → "JPG", "tar.gz" → "TAR.GZ".</summary>
    public static string Display(string key) => key.ToUpperInvariant();
}
