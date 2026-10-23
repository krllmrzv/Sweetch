using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>Document conversions through pandoc.</summary>
public static class PandocBackend
{
    public static string ReaderFor(string format) => format switch
    {
        "md" or "txt" => "markdown",
        "html" => "html",
        "docx" => "docx",
        "odt" => "odt",
        "epub" => "epub",
        "rtf" => "rtf",
        "tex" => "latex",
        "adoc" => "asciidoc",
        "typ" => "typst",
        "wiki" => "mediawiki",
        "rst" or "org" or "ipynb" or "fb2" or "textile" or "docbook" or "opml" or "djot" => format,
        _ => throw new ConversionException(T($"Pandoc cannot read {format}.", $"Pandoc не поддерживает чтение {format}.")),
    };

    public static string WriterFor(string format) => format switch
    {
        "md" => "gfm",
        "txt" => "plain",
        "html" => "html5",
        "docx" => "docx",
        "odt" => "odt",
        "epub" => "epub3",
        "rtf" => "rtf",
        "tex" => "latex",
        "rst" => "rst",
        "typ" => "typst",
        "adoc" => "asciidoc",
        "pptx" => "pptx",
        _ => throw new ConversionException(T($"Pandoc cannot write {format}.", $"Pandoc не поддерживает запись {format}.")),
    };

    public static async Task ConvertAsync(OperationContext ctx, string input, string inputFormat, string output, string format)
    {
        var pandoc = ctx.Tools.Require(ExternalTool.Pandoc);
        // Pandoc runs with the input's folder as working directory (for relative
        // images), so relative paths would resolve twice.
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        var args = new List<string>
        {
            input,
            "-f", ReaderFor(inputFormat),
            "-t", WriterFor(format),
            "-o", output,
            // Pandoc encodes stderr with the ANSI code page on Windows; a warning that
            // mentions a Cyrillic file name crashes it on non-Cyrillic systems
            // ("hPutChar: invalid argument"). Warnings are not shown to users anyway.
            "--quiet",
        };
        if (format is "html" or "rtf" or "epub" or "tex" || inputFormat == "html")
            args.Add("--standalone");
        if (format == "html")
            args.Add("--embed-resources");
        if (format is "html" or "epub")
            args.AddRange(["--metadata", "title=" + Formats.BaseNameOf(input)]);
        if (format is "txt")
            args.Add("--wrap=none");
        var mediaDir = Path.GetDirectoryName(output)!;
        if (format == "md" && inputFormat is "docx" or "odt" or "epub" or "fb2" or "ipynb")
        {
            // Pictures from the document land next to the Markdown file.
            args.Add("--extract-media=" + Path.Combine(mediaDir, Formats.BaseNameOf(input) + " - media"));
        }
        var inputDir = Path.GetDirectoryName(Path.GetFullPath(input));
        if (inputDir is not null)
            args.Add("--resource-path=" + inputDir);
        await ProcessRunner.RunAsync(pandoc, args, ctx.CancellationToken, workingDirectory: inputDir).ConfigureAwait(false);
    }
}
