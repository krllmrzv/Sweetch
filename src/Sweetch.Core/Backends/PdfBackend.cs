using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>PDF rendering (PDFium), text extraction (PdfPig) and page operations (PDFsharp).</summary>
public static class PdfBackend
{
    public static int PageCount(string path)
    {
        using var stream = File.OpenRead(path);
        return PDFtoImage.Conversion.GetPageCount(stream);
    }

    /// <summary>
    /// Renders every page. A single page becomes "name.png"; more pages go into a folder "name (pages)".
    /// </summary>
    public static async Task<IEnumerable<string>> RenderAsync(FileItem item, string format, OperationContext ctx, IProgress<double> progress)
    {
        var bytes = await File.ReadAllBytesAsync(item.Path, ctx.CancellationToken).ConfigureAwait(false);
        var count = PDFtoImage.Conversion.GetPageCount(bytes);
        var options = new PDFtoImage.RenderOptions(Dpi: ctx.Settings.PdfDpi, BackgroundColor: SkiaSharp.SKColors.White, WithAnnotations: true, WithFormFill: true);
        var dir = ctx.OutputDirFor(item.Path);

        if (count == 1)
        {
            var single = await OutputPaths.ProduceAsync(dir, item.BaseName, format, tmp =>
                Task.Run(() =>
                {
                    using var bitmap = PDFtoImage.Conversion.ToImage(bytes, 0, options: options);
                    Save(bitmap, tmp, format, ctx.Settings.JpegQuality);
                }, ctx.CancellationToken)).ConfigureAwait(false);
            return [single];
        }

        var folder = OutputPaths.UniqueDirectory(dir, $"{item.BaseName} ({T("pages", "страницы")})");
        Directory.CreateDirectory(folder);
        var digits = count.ToString().Length;
        try
        {
            await Task.Run(() => RenderPages(bytes, options, ctx.CancellationToken, (bitmap, i) =>
            {
                Save(bitmap, Path.Combine(folder, $"{item.BaseName} - {(i + 1).ToString().PadLeft(digits, '0')}.{format}"), format, ctx.Settings.JpegQuality);
                progress.Report((i + 1.0) / count);
            }), ctx.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.TryDelete(folder);
            throw;
        }
        return [folder];
    }

    /// <summary>All pages in one file: a multi-page TIFF (LZW) or a comic book archive (CBZ: JPG pages in a ZIP).</summary>
    public static async Task<string> ToBundleAsync(FileItem item, string format, OperationContext ctx, IProgress<double> progress)
    {
        var bytes = await File.ReadAllBytesAsync(item.Path, ctx.CancellationToken).ConfigureAwait(false);
        var count = PDFtoImage.Conversion.GetPageCount(bytes);
        var options = new PDFtoImage.RenderOptions(Dpi: ctx.Settings.PdfDpi, BackgroundColor: SkiaSharp.SKColors.White, WithAnnotations: true, WithFormFill: true);
        return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp => Task.Run(() =>
        {
            if (format == "cbz")
            {
                using var zip = System.IO.Compression.ZipFile.Open(tmp, System.IO.Compression.ZipArchiveMode.Create);
                RenderPages(bytes, options, ctx.CancellationToken, (bitmap, i) =>
                {
                    // JPG is already compressed; readers sort pages by name.
                    using var entry = zip.CreateEntry($"{i + 1:0000}.jpg", System.IO.Compression.CompressionLevel.NoCompression).Open();
                    bitmap.Encode(entry, SkiaSharp.SKEncodedImageFormat.Jpeg, ctx.Settings.JpegQuality);
                    progress.Report((i + 1.0) / count);
                });
                return;
            }
            using var pages = new ImageMagick.MagickImageCollection();
            RenderPages(bytes, options, ctx.CancellationToken, (bitmap, i) =>
            {
                using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                var page = new ImageMagick.MagickImage(png.AsSpan());
                page.Settings.Compression = ImageMagick.CompressionMethod.LZW;
                pages.Add(page);
                progress.Report((i + 1.0) / count * 0.9);
            });
            pages.Write(tmp, ImageMagick.MagickFormat.Tiff);
        }, ctx.CancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// "1-3, 5, 8-" → zero-based page indexes in the order written. Pages past the end are an error, so a
    /// typo doesn't silently give a shorter file.
    /// </summary>
    public static List<int> ParsePages(string text, int count)
    {
        var pages = new List<int>();
        foreach (var raw in text.Replace(" ", "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            // "5", "1-3", "8-" (to the end), "-3" (from the start).
            var ends = raw.Replace('–', '-').Replace('—', '-').Split('-');
            var first = ends[0].Length == 0 ? "1" : ends[0];
            var last = ends.Length == 1 ? first : ends[1].Length == 0 ? count.ToString() : ends[1];
            if (ends.Length > 2 || !int.TryParse(first, out var from) || !int.TryParse(last, out var to))
                throw new ConversionException(T($"Not a page range: {raw}", $"Непонятный диапазон страниц: {raw}"));
            if (from < 1 || to > count || from > to)
                throw new ConversionException(T($"Pages {raw}: the document has {count}.", $"Страницы {raw}: в документе их {count}."));
            for (var p = from; p <= to; p++)
                pages.Add(p - 1);
        }
        if (pages.Count == 0)
            throw new ConversionException(T("Which pages? For example 1-3, 5.", "Какие страницы? Например, 1-3, 5."));
        return pages;
    }

    public static void ExtractPages(string input, string output, string pages)
    {
        using var source = Open(input, PdfDocumentOpenMode.Import);
        using var result = new PdfSharp.Pdf.PdfDocument();
        foreach (var i in ParsePages(pages, source.PageCount))
            result.AddPage(source.Pages[i]);
        result.Save(output);
    }

    /// <summary>Renders every page to "page-001.png"… in <paramref name="dir"/> (used for OCR).</summary>
    public static IReadOnlyList<string> RenderPagesToPng(string pdf, string dir, int dpi, CancellationToken ct)
    {
        var options = new PDFtoImage.RenderOptions(Dpi: dpi, BackgroundColor: SkiaSharp.SKColors.White, WithAnnotations: true, WithFormFill: true);
        var pages = new List<string>();
        RenderPages(File.ReadAllBytes(pdf), options, ct, (bitmap, i) =>
        {
            var path = Path.Combine(dir, $"page-{i + 1:000}.png");
            Save(bitmap, path, "png", 100);
            pages.Add(path);
        });
        return pages;
    }

    /// <summary>
    /// Every page in order from one load of the document: opening it again for each page costs more than
    /// drawing it on long PDFs (400 pages: 3.3 s → 0.24 s).
    /// </summary>
    private static void RenderPages(byte[] pdf, PDFtoImage.RenderOptions options, CancellationToken ct, Action<SkiaSharp.SKBitmap, int> page)
    {
        var i = 0;
        foreach (var bitmap in PDFtoImage.Conversion.ToImages(pdf, options: options))
        {
            using (bitmap)
            {
                ct.ThrowIfCancellationRequested();
                page(bitmap, i++);
            }
        }
    }

    private static void Save(SkiaSharp.SKBitmap bitmap, string path, string format, int quality)
    {
        using var file = File.Create(path);
        var encoded = format == "jpg" ? SkiaSharp.SKEncodedImageFormat.Jpeg
            : format == "webp" ? SkiaSharp.SKEncodedImageFormat.Webp
            : SkiaSharp.SKEncodedImageFormat.Png;
        bitmap.Encode(file, encoded, quality);
    }

    /// <summary>Plain text, pages separated by blank lines (or "---" rules for Markdown).</summary>
    public static string ExtractText(string path, bool markdown)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page).Trim();
            if (text.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(markdown ? "\n\n---\n\n" : "\n\n");
            sb.Append(markdown ? ToMarkdownParagraphs(text) : text);
        }
        if (sb.Length == 0)
            throw new ConversionException(T("The PDF contains no text (probably a scan). Try converting it to JPG/PNG.", "В PDF нет текста (вероятно, это скан). Попробуйте конвертацию в JPG/PNG."));
        return sb.ToString();
    }

    private static string ToMarkdownParagraphs(string text)
    {
        // Join wrapped lines into paragraphs; keep blank lines as paragraph breaks.
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var paragraph = new StringBuilder();
        void Flush()
        {
            if (paragraph.Length == 0)
                return;
            sb.Append(paragraph.ToString().Trim()).Append("\n\n");
            paragraph.Clear();
        }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            if (paragraph.Length > 0)
            {
                if (paragraph[^1] == '-' && paragraph.Length > 1 && char.IsLetter(paragraph[^2]))
                    paragraph.Length--; // de-hyphenate
                else
                    paragraph.Append(' ');
            }
            paragraph.Append(line);
        }
        Flush();
        return sb.ToString().TrimEnd();
    }

    public static Task<string> ToTextAsync(FileItem item, string format, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
            Task.Run(() => File.WriteAllText(tmp, ExtractText(item.Path, format == "md"), new UTF8Encoding(false)), ctx.CancellationToken));

    /// <summary>PDF → DOCX/HTML/EPUB/ODT: extract text to Markdown, then let pandoc build the document.</summary>
    public static async Task<string> ToDocumentAsync(FileItem item, string format, OperationContext ctx)
    {
        var tempDir = OutputPaths.TempDirectory();
        try
        {
            var md = Path.Combine(tempDir, item.BaseName + ".md");
            await File.WriteAllTextAsync(md, ExtractText(item.Path, markdown: true), new UTF8Encoding(false), ctx.CancellationToken).ConfigureAwait(false);
            return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
                PandocBackend.ConvertAsync(ctx, md, "md", tmp, format)).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(tempDir);
        }
    }

    public static string Merge(IReadOnlyList<string> inputs, string output)
    {
        using var result = new PdfSharp.Pdf.PdfDocument();
        foreach (var input in inputs)
        {
            using var source = Open(input, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages)
                result.AddPage(page);
        }
        result.Save(output);
        return output;
    }

    public static async Task<IEnumerable<string>> SplitAsync(FileItem item, OperationContext ctx, IProgress<double> progress)
    {
        var dir = ctx.OutputDirFor(item.Path);
        var folder = OutputPaths.UniqueDirectory(dir, $"{item.BaseName} ({T("pages", "страницы")})");
        Directory.CreateDirectory(folder);
        try
        {
            await Task.Run(() =>
            {
                using var source = Open(item.Path, PdfDocumentOpenMode.Import);
                var count = source.PageCount;
                var digits = count.ToString().Length;
                for (var i = 0; i < count; i++)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    using var single = new PdfSharp.Pdf.PdfDocument();
                    single.AddPage(source.Pages[i]);
                    single.Save(Path.Combine(folder, $"{item.BaseName} - {(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
                    progress.Report((i + 1.0) / count);
                }
            }, ctx.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.TryDelete(folder);
            throw;
        }
        return [folder];
    }

    public static void Rotate(string input, string output, int degrees)
    {
        using var document = Open(input, PdfDocumentOpenMode.Modify);
        foreach (var page in document.Pages)
            page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
        document.Save(output);
    }

    /// <summary>Re-saves with compressed streams and without unused objects.</summary>
    public static void Optimize(string input, string output)
    {
        using var document = Open(input, PdfDocumentOpenMode.Modify);
        document.Options.CompressContentStreams = true;
        document.Options.NoCompression = false;
        document.Options.EnableCcittCompressionForBilevelImages = true;
        document.Save(output);
    }

    private static PdfSharp.Pdf.PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (PdfReaderException ex)
        {
            throw new ConversionException(T($"Cannot open PDF {Path.GetFileName(path)}: {ex.Message}", $"Не удалось открыть PDF {Path.GetFileName(path)}: {ex.Message}"), ex);
        }
    }
}
