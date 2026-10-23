using ImageMagick;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>Image conversions and edits through Magick.NET (ImageMagick, runs in-process).</summary>
public static class ImageBackend
{
    private static MagickImage Load(string path)
    {
        var settings = new MagickReadSettings();
        var key = Formats.KeyOf(path);
        if (key is "svg" or "svgz" or "emf" or "wmf")
        {
            settings.Density = new Density(192);
            settings.BackgroundColor = MagickColors.Transparent;
        }
        if (Formats.Raw.Contains(key))
        {
            // Develop like the camera would: its white balance, sRGB output.
            settings.SetDefine("dng:use-camera-wb", "true");
            settings.SetDefine("dng:output-color", "1");
        }
        // Only the first frame for animated GIF/WebP/multi-page TIFF.
        settings.FrameIndex = 0;
        settings.FrameCount = 1;
        var image = new MagickImage(path, settings);
        image.AutoOrient();
        return image;
    }

    public static MagickFormat ToMagickFormat(string format) => format switch
    {
        "jpg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "heic" => MagickFormat.Heic,
        "avif" => MagickFormat.Avif,
        "tiff" => MagickFormat.Tiff,
        "bmp" => MagickFormat.Bmp,
        "gif" => MagickFormat.Gif,
        "ico" => MagickFormat.Ico,
        "pdf" => MagickFormat.Pdf,
        "jxl" => MagickFormat.Jxl,
        // Edits keep the source format; most names match ImageMagick's (psd, tga, qoi, dds, jp2, exr…).
        _ when Enum.TryParse<MagickFormat>(format, ignoreCase: true, out var named) && named != MagickFormat.Unknown => named,
        _ => throw new ConversionException(T($"Unsupported image format: {format}", $"Неподдерживаемый формат изображения: {format}")),
    };

    private static void PrepareFor(MagickImage image, string format, int quality)
    {
        image.Quality = (uint)Math.Clamp(quality, 1, 100);
        if (format is "jpg" or "bmp" or "pdf" && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
        if (format == "ico" && (image.Width > 256 || image.Height > 256))
            image.Resize(new MagickGeometry(256, 256));
        if (format == "ico")
            image.Settings.SetDefine(MagickFormat.Ico, "auto-resize", "256,128,64,48,32,16");
    }

    public static Task ConvertAsync(string input, string output, string format, Settings settings, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var image = Load(input);
            ct.ThrowIfCancellationRequested();
            PrepareFor(image, format, settings.JpegQuality);
            image.Write(output, ToMagickFormat(format));
        }, ct);

    /// <summary>Applies <paramref name="edit"/> and saves in the source format.</summary>
    public static Task EditAsync(string input, string output, Action<MagickImage> edit, int? quality, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var image = Load(input);
            var format = OutputFormatFor(Formats.KeyOf(input));
            edit(image);
            ct.ThrowIfCancellationRequested();
            PrepareFor(image, format, quality ?? 92);
            image.Write(output, ToMagickFormat(format));
        }, ct);

    /// <summary>Formats we can read but not write (vector, GIMP, DICOM) are saved as PNG.</summary>
    public static string OutputFormatFor(string inputFormat) => inputFormat switch
    {
        "svg" or "svgz" or "emf" or "wmf" or "xcf" or "dicom" => "png",
        _ when Formats.Raw.Contains(inputFormat) => "jpg",
        _ => inputFormat,
    };

    public static Task CompressAsync(string input, string output, int quality, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var image = Load(input);
            var format = OutputFormatFor(Formats.KeyOf(input));
            image.Strip();
            image.Quality = (uint)Math.Clamp(quality, 1, 100);
            if (format == "png")
            {
                // Lossy palette reduction gives the biggest win for PNG.
                if (image.TotalColors > 256)
                    image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
            }
            else if (format == "jpg")
            {
                image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                image.Settings.Interlace = Interlace.Jpeg;
            }
            PrepareFor(image, format, quality);
            image.Write(output, ToMagickFormat(format));
        }, ct);

    /// <summary>A user preset: optional resize, grayscale and metadata removal, saved in <paramref name="format"/>.</summary>
    public static Task PresetAsync(string input, string output, string format, int quality, int? maxSide, int? percent,
        bool grayscale, bool strip, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var image = Load(input);
            if (maxSide is > 0)
                ResizeMax(image, (uint)maxSide.Value);
            else if (percent is > 0 and < 100)
                ResizePercent(image, percent.Value);
            if (grayscale)
                Grayscale(image);
            if (strip)
                image.Strip();
            ct.ThrowIfCancellationRequested();
            PrepareFor(image, format, quality);
            image.Write(output, ToMagickFormat(format));
        }, ct);

    public static Task ImagesToPdfAsync(IReadOnlyList<string> inputs, string output, int quality, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var collection = new MagickImageCollection();
            foreach (var input in inputs)
            {
                ct.ThrowIfCancellationRequested();
                var image = Load(input);
                PrepareFor(image, "pdf", quality);
                collection.Add(image);
            }
            collection.Write(output, MagickFormat.Pdf);
        }, ct);

    public static void ResizePercent(MagickImage image, double percent) =>
        image.Resize(new Percentage(percent));

    public static void ResizeMax(MagickImage image, uint maxSide)
    {
        if (image.Width <= maxSide && image.Height <= maxSide)
            return;
        image.Resize(new MagickGeometry(maxSide, maxSide));
    }

    public static void CropSquare(MagickImage image)
    {
        var side = Math.Min(image.Width, image.Height);
        image.Crop(side, side, Gravity.Center);
        image.ResetPage();
    }

    public static void Grayscale(MagickImage image) => image.Grayscale(PixelIntensityMethod.Rec709Luminance);
}
