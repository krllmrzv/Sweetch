using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Sweetch.App.Input;
using Sweetch.Core;

namespace Sweetch.App.UI;

/// <summary>
/// Colours and fonts shared by the wheel, the settings, the job cards and the shelf. Follows the
/// Windows light/dark app setting; <see cref="Refresh"/> re-reads it before a window is built.
/// <para>
/// The look: flat surfaces, no gradients or glows, ink (black or paper-white) for anything selected,
/// and one bright colour per modifier key so you can tell at a glance which wheel is open.
/// </para>
/// </summary>
internal static class Theme
{
    private static readonly Palette DarkPalette = new(
        Page: Rgb(0x0B0B0A),
        Card: Rgb(0x171716),
        Track: Rgb(0x232321),
        Line: Rgb(0x2C2C2A),
        Text: Rgb(0xF3F1EB),
        Muted: Rgb(0x8E8B84),
        Panel: Rgb(0x171716),
        Backdrop: Rgb(0x121211),
        Hub: Rgb(0x1F1F1D),
        Shadow: 0.55);

    private static readonly Palette LightPalette = new(
        Page: Rgb(0xEDECE8),
        Card: Rgb(0xFFFFFF),
        Track: Rgb(0xE3E2DD),
        Line: Rgb(0xD6D4CE),
        Text: Rgb(0x0F0F0E),
        Muted: Rgb(0x6E6C66),
        Panel: Rgb(0xFFFFFF),
        Backdrop: Rgb(0xFFFFFF),
        Hub: Rgb(0x0F0F0E),
        Shadow: 0.16);

    private sealed record Palette(
        Color Page,
        Color Card,
        Color Track,
        Color Line,
        Color Text,
        Color Muted,
        Color Panel,
        Color Backdrop,
        Color Hub,
        double Shadow);

    private static Palette _palette = DarkPalette;

    // ---- the three key colours (the same in both themes; text on them is always ink)
    public static readonly Color ShiftColor = Rgb(0xFF6B3D);
    public static readonly Color CtrlColor = Rgb(0xA99CFF);
    public static readonly Color AltColor = Rgb(0xE4EE5A);

    /// <summary>
    /// The "low contrast" colour scheme: every bright colour becomes a quiet tint of the page (dark
    /// browns, greys and olives on dark, pastels on light) and text on it is soft instead of black.
    /// </summary>
    public static bool LowContrast { get; set; }

    // ---- extra tile colours for the settings pages
    public static Brush Mint => Solid(Tint(Rgb(0xA6E6CB)));
    public static Brush Pink => Solid(Tint(Rgb(0xF5B5DE)));
    public static Brush Sky => Solid(Tint(Rgb(0x9CCBF2)));

    /// <summary>Near-black and paper, for surfaces that look the same in both themes.</summary>
    public static readonly SolidColorBrush InkFixed = Solid(Rgb(0x0F0F0E));
    public static readonly SolidColorBrush PaperFixed = Solid(Rgb(0xF3F1EB));
    public static readonly SolidColorBrush Error = Solid(Rgb(0xF0564A));
    public static readonly SolidColorBrush Success = Solid(Rgb(0x2FB36B));

    /// <summary>The colour of a modifier key; a wheel without a key keeps the colour of its usual one.</summary>
    public static Color KeyColor(ModifierKey key, WheelKind wheel) => Tint(BaseKeyColor(key, wheel));

    /// <summary>
    /// The key colour where it must stand out: the keycap, the open wheel's dot, the selected slice in
    /// low contrast. In the bright scheme it is the key colour itself.
    /// </summary>
    public static Color KeyStrongColor(ModifierKey key, WheelKind wheel) => LowContrast ? Mix(BaseKeyColor(key, wheel), TintBase, 0.55) : BaseKeyColor(key, wheel);

    public static SolidColorBrush KeyBrush(ModifierKey key, WheelKind wheel) => Solid(KeyColor(key, wheel));

    private static Color BaseKeyColor(ModifierKey key, WheelKind wheel) => key switch
    {
        ModifierKey.Shift => ShiftColor,
        ModifierKey.Ctrl => CtrlColor,
        ModifierKey.Alt => AltColor,
        _ => wheel switch
        {
            WheelKind.Convert => ShiftColor,
            WheelKind.Tools => CtrlColor,
            _ => AltColor,
        },
    };

    /// <summary>What a bright colour turns into in the low-contrast scheme.</summary>
    private static Color Tint(Color color) => LowContrast ? Mix(color, TintBase, IsDark ? 0.28 : 0.34) : color;

    private static Color TintBase => IsDark ? Rgb(0x1A1A19) : Rgb(0xF4F3EF);

    /// <summary>Background of a warning strip: the error red, softened into the card colour.</summary>
    public static Brush Alert => Solid(Mix(Error.Color, _palette.Card, IsDark ? 0.24 : 0.16));

    private static Color Mix(Color color, Color with, double amount) => Color.FromRgb(
        (byte)Math.Round(color.R * amount + with.R * (1 - amount)),
        (byte)Math.Round(color.G * amount + with.G * (1 - amount)),
        (byte)Math.Round(color.B * amount + with.B * (1 - amount)));

    /// <summary>Text and icons on the key colours and tiles: ink, or soft paper/graphite in low contrast.</summary>
    public static SolidColorBrush OnKey => !LowContrast ? InkFixed : Solid(IsDark ? Rgb(0xE2DFD8) : Rgb(0x2B2A27));

    /// <summary>The main button on a tile (and the key picker): ink, or a faint wash in low contrast.</summary>
    public static Brush TileStrong => LowContrast ? Solid(Color.FromArgb(0x26, OnKey.Color.R, OnKey.Color.G, OnKey.Color.B)) : InkFixed;

    public static Brush TileStrongText => LowContrast ? OnKey : PaperFixed;

    /// <summary>A secondary button or field on a tile.</summary>
    public static Brush TileSoft => Solid(Color.FromArgb(LowContrast ? (byte)0x14 : (byte)0x17, OnKey.Color.R, OnKey.Color.G, OnKey.Color.B));

    // ---- fonts, bundled so the app looks the same on every PC
    private static readonly Uri FontBase = PackBase();
    public static readonly FontFamily UiFont = new(FontBase, "./Assets/Fonts/#Onest, Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = UiFont;
    public static readonly FontFamily Icons = new(FontBase, "./Assets/Fonts/#Sweetch Icons");

    public static bool IsDark { get; private set; } = true;

    public static Brush Page => Solid(_palette.Page);
    public static Brush Card => Solid(_palette.Card);
    public static Brush Track => Solid(_palette.Track);
    public static Brush Text => Solid(_palette.Text);
    public static Brush Muted => Solid(_palette.Muted);
    public static Brush Panel => Solid(_palette.Panel);
    public static Brush PanelBorder => Solid(_palette.Line);
    public static Brush Backdrop => Solid(_palette.Backdrop);
    /// <summary>The circle in the middle of the wheel: ink, or a soft grey on a light low-contrast wheel.</summary>
    public static Brush Hub => Solid(LowContrast && !IsDark ? _palette.Track : _palette.Hub);

    public static SolidColorBrush HubText => LowContrast && !IsDark ? Solid(_palette.Text) : PaperFixed;

    /// <summary>Hover over a list row.</summary>
    public static Brush Hover => Solid(_palette.Track);

    /// <summary>A selected list row.</summary>
    public static Brush Selected => Solid(_palette.Track);

    /// <summary>Fill for anything active (selected tab, switch that is on, main button): ink on light, paper on dark.</summary>
    public static Brush Ink => Solid(LowContrast ? Mix(_palette.Text, _palette.Card, IsDark ? 0.72 : 0.8) : _palette.Text);

    /// <summary>Text on <see cref="Ink"/>.</summary>
    public static Brush OnInk => Solid(_palette.Page);

    /// <summary>
    /// The highlighted slice: ink on a light wheel, paper on a dark one; in low contrast, a stronger
    /// tint of the key (<paramref name="strong"/>).
    /// </summary>
    public static Color SliceOn(Color strong) => LowContrast ? strong : _palette.Text;

    /// <summary>A slice on the wheel's other side: the backdrop with a hint of the key, so the two sides never look alike.</summary>
    public static Color BackSlice(Color key) => Mix(key, _palette.Backdrop, IsDark ? 0.2 : 0.28);

    /// <summary>Label on a back-side slice: the key colour on the dark wheel, ink on the light one.</summary>
    public static Color BackSliceText(Color key) => IsDark ? key : _palette.Text;

    /// <summary>Label on the highlighted slice: the key colour on ink, ink on paper, soft text in low contrast.</summary>
    public static Color SliceOnText(Color key) => LowContrast ? OnKey.Color : IsDark ? InkFixed.Color : key;

    /// <summary>Soft drop shadow under floating surfaces (job cards, the shelf).</summary>
    public static System.Windows.Media.Effects.DropShadowEffect Shadow(double blur = 24) =>
        new() { BlurRadius = blur, ShadowDepth = 4, Direction = 270, Opacity = _palette.Shadow, Color = Colors.Black };

    /// <summary>Forces dark (true) or light (false) instead of following Windows; used by the self-test.</summary>
    public static bool? Override { get; set; }

    /// <summary>Re-reads the Windows "app mode" (light/dark).</summary>
    public static void Refresh() => Use(Override ?? ReadSystemDark());

    private static void Use(bool dark)
    {
        IsDark = dark;
        _palette = dark ? DarkPalette : LightPalette;
        if (Application.Current is { } app)
        {
            app.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] ??= ScrollBarStyle.Value;
            app.Resources["Sweetch.Thumb"] = Solid(Color.FromArgb(0x70, _palette.Muted.R, _palette.Muted.G, _palette.Muted.B));
            app.Resources["Sweetch.ThumbHover"] = Solid(_palette.Muted);
        }
    }

    /// <summary>
    /// Every vertical scroll bar in the app: a slim rounded thumb that widens under the mouse, no arrows or track.
    /// Clicking above or below the thumb still pages.
    /// </summary>
    private static readonly Lazy<Style> ScrollBarStyle = new(() => (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Style.Triggers>
            <Trigger Property="Orientation" Value="Vertical">
              <Setter Property="Width" Value="14" />
              <Setter Property="MinWidth" Value="14" />
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Track x:Name="PART_Track" IsDirectionReversed="True">
                      <Track.DecreaseRepeatButton>
                        <RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False" Template="{x:Null}" Background="Transparent" />
                      </Track.DecreaseRepeatButton>
                      <Track.IncreaseRepeatButton>
                        <RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False" Template="{x:Null}" Background="Transparent" />
                      </Track.IncreaseRepeatButton>
                      <Track.Thumb>
                        <Thumb>
                          <Thumb.Template>
                            <ControlTemplate TargetType="Thumb">
                              <Grid Background="Transparent">
                                <Border x:Name="Bar" Width="6" MinHeight="36" Margin="0,6" CornerRadius="3" HorizontalAlignment="Center" Background="{DynamicResource Sweetch.Thumb}" />
                              </Grid>
                              <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                  <Setter TargetName="Bar" Property="Width" Value="8" />
                                  <Setter TargetName="Bar" Property="CornerRadius" Value="4" />
                                  <Setter TargetName="Bar" Property="Background" Value="{DynamicResource Sweetch.ThumbHover}" />
                                </Trigger>
                                <Trigger Property="IsDragging" Value="True">
                                  <Setter TargetName="Bar" Property="Width" Value="8" />
                                  <Setter TargetName="Bar" Property="CornerRadius" Value="4" />
                                  <Setter TargetName="Bar" Property="Background" Value="{DynamicResource Sweetch.ThumbHover}" />
                                </Trigger>
                              </ControlTemplate.Triggers>
                            </ControlTemplate>
                          </Thumb.Template>
                        </Thumb>
                      </Track.Thumb>
                    </Track>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Trigger>
          </Style.Triggers>
        </Style>
        """));

    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    private static Uri PackBase()
    {
        // Touching the scheme registers pack:// even before the Application object exists.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        return new Uri("pack://application:,,,/");
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
