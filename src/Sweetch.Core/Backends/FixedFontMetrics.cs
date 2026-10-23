using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Graphics;

namespace Sweetch.Core.Backends;

/// <summary>
/// Font metrics for ClosedXML without reading fonts. Its default engine scans every system font on first use,
/// which takes seconds and fails when a folder under C:\Windows\Fonts can't be read. Calibri-like numbers are
/// close enough: column widths are kept as stored and we size new columns from the text length.
/// </summary>
internal sealed class FixedFontMetrics : IXLGraphicEngine
{
    // Font loading in the default engine is lazy, so pictures still work through it.
    private readonly DefaultGraphicEngine _pictures = new("Calibri");

    private static double Em(IXLFontBase font, double dpi) => font.FontSize * dpi / 72;

    public double GetMaxDigitWidth(IXLFontBase font, double dpi) => Math.Round(Em(font, dpi) * 0.477); // 7 px for 11 pt at 96 DPI

    public double GetDescent(IXLFontBase font, double dpi) => Math.Round(Em(font, dpi) * 0.25);

    public double GetTextHeight(IXLFontBase font, double dpi) => Math.Round(Em(font, dpi) * 1.22);

    public double GetTextWidth(string text, IXLFontBase font, double dpi) => text.Length * GetMaxDigitWidth(font, dpi);

    public GlyphBox GetGlyphBox(ReadOnlySpan<int> graphemeCluster, IXLFontBase font, Dpi dpi) =>
        new((ushort)GetMaxDigitWidth(font, dpi.X), (ushort)Em(font, dpi.Y), (ushort)GetDescent(font, dpi.Y));

    public XLPictureInfo GetPictureInfo(Stream imageStream, XLPictureFormat expectedFormat) =>
        _pictures.GetPictureInfo(imageStream, expectedFormat);
}
