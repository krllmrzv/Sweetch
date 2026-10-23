using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sweetch.App.UI;

/// <summary>
/// Optical centring for text. A TextBlock is as tall as the font's line (ascender to descender), so
/// capitals sit high and a label centred by its box looks off. These helpers pull the box in with
/// negative margins, like CSS <c>text-box: trim-both cap alphabetic</c>, so the visible letters are
/// what gets centred.
/// </summary>
internal static class Typo
{
    /// <summary>Box from the cap height of the first line to the baseline of the last.</summary>
    public static void TrimToCaps(TextBlock text, double below = 0)
    {
        var (baseline, line, caps) = Metrics(text);
        Trim(text, (baseline - caps) * text.FontSize, (line - baseline) * text.FontSize - below);
    }

    /// <summary>Box of one em above the baseline: the icon font draws every icon in that square.</summary>
    public static void TrimToEm(TextBlock icon, double below = 0)
    {
        var (baseline, line, _) = Metrics(icon);
        Trim(icon, (baseline - 1) * icon.FontSize, (line - baseline) * icon.FontSize - below);
    }

    private static void Trim(FrameworkElement element, double top, double bottom)
    {
        var m = element.Margin;
        element.Margin = new Thickness(m.Left, m.Top - top, m.Right, m.Bottom - bottom);
    }

    /// <summary>Baseline, line height and cap height in ems, as WPF lays the text out.</summary>
    private static (double Baseline, double Line, double Caps) Metrics(TextBlock text)
    {
        // Not in a window yet, so an inherited font isn't known: pin the one it will get.
        if (text.ReadLocalValue(TextBlock.FontFamilyProperty) == DependencyProperty.UnsetValue)
            text.FontFamily = Theme.UiFont;
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        if (typeface.TryGetGlyphTypeface(out var glyphs))
            return (text.FontFamily.Baseline, text.FontFamily.LineSpacing, glyphs.CapsHeight);
        return (text.FontFamily.Baseline, text.FontFamily.LineSpacing, 0.7);
    }
}
