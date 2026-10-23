using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Sweetch.App.UI;

/// <summary>
/// The building blocks of every window, drawn in code (cards, segmented choices, toggles, sliders,
/// pill buttons): flat surfaces, large type, and ink for whatever is active.
/// </summary>
internal static class Controls
{
    public const double CardRadius = 20;
    public const double TileRadius = 28;

    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, Brush? brush = null, bool wrap = true) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Theme.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    /// <summary>A display heading (page titles, tile titles).</summary>
    public static TextBlock Heading(string text, double size, Brush? brush = null) => new()
    {
        Text = text,
        FontFamily = Theme.DisplayFont,
        FontSize = size,
        FontWeight = FontWeights.ExtraBold,
        Foreground = brush ?? Theme.Text,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>An icon from the bundled icon font.</summary>
    public static TextBlock Icon(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = Theme.Icons,
        FontSize = size,
        Foreground = brush ?? Theme.Text,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 13.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.Muted,
        Margin = new Thickness(4, 28, 0, 10),
    };

    /// <summary>A rounded card with a title/description on the left and a control on the right.</summary>
    public static Border Card(string title, string? description, UIElement? control, UIElement? below = null, string? glyph = null)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Text(title, 15.5, FontWeights.SemiBold));
        if (!string.IsNullOrEmpty(description))
        {
            var d = Text(description, 13.5, brush: Theme.Muted);
            d.Margin = new Thickness(0, 3, 0, 0);
            left.Children.Add(d);
        }

        var row = new DockPanel { LastChildFill = true, MinHeight = 40 };
        if (glyph is not null)
        {
            var icon = Icon(glyph, 24);
            icon.Width = 28;
            icon.Margin = new Thickness(0, 0, 16, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
        }
        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(20, 0, 0, 0);
            DockPanel.SetDock(fe, Dock.Right);
            row.Children.Add(fe);
            RightOrUnder(row, fe, room: 260, beside: new Thickness(20, 0, 0, 0), under: new Thickness(0, 14, 0, 0));
        }
        row.Children.Add(left);

        var content = new StackPanel();
        content.Children.Add(row);
        if (below is not null)
            content.Children.Add(below);

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(CardRadius),
            Padding = new Thickness(22, 18, 22, 18),
            Margin = new Thickness(0, 0, 0, 8),
            Child = content,
        };
    }

    /// <summary>
    /// Keeps <paramref name="side"/> docked on the right while the rest of the row still gets <paramref name="room"/>
    /// pixels; in a narrower window it moves under the rest, aligned left.
    /// </summary>
    public static void RightOrUnder(DockPanel row, FrameworkElement side, double room, Thickness beside, Thickness under)
    {
        row.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged)
                return;
            side.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // Moving under makes the page taller, which can bring in the scroll bar and take back exactly the
            // width that decided it: going back to the right needs a margin, or the layout flips forever.
            var wasNarrow = DockPanel.GetDock(side) == Dock.Bottom;
            var narrow = e.NewSize.Width - side.DesiredSize.Width < room + (wasNarrow ? 24 : 0);
            DockPanel.SetDock(side, narrow ? Dock.Bottom : Dock.Right);
            side.Margin = narrow ? under : beside;
            side.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        };
    }

    /// <summary>A big colour tile: title and text on top, a footer (key, icon…) at the bottom. Text is always ink.</summary>
    public static Border Tile(Brush background, string title, string? text, UIElement? footer, double height = 0)
    {
        var top = new StackPanel();
        top.Children.Add(Heading(title, 20, Theme.OnKey));
        if (!string.IsNullOrEmpty(text))
        {
            var t = Text(text, 13.5, FontWeights.Medium, Theme.OnKey);
            t.Opacity = 0.72;
            t.Margin = new Thickness(0, 4, 0, 0);
            top.Children.Add(t);
        }
        var dock = new DockPanel { LastChildFill = false };
        if (footer is not null)
        {
            DockPanel.SetDock(footer, Dock.Bottom);
            dock.Children.Add(footer);
        }
        top.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        var tile = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(TileRadius),
            Padding = new Thickness(22),
            Child = dock,
        };
        // At least this tall; taller when the text wraps in a narrow window.
        if (height > 0)
            tile.MinHeight = height;
        return tile;
    }

    /// <summary>A row of mutually exclusive pills; the active one is filled with ink.</summary>
    public static Border Segmented<T>(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> changed)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        var pills = new List<(Border Pill, TextBlock Text, T Value)>();
        void Select(T value)
        {
            foreach (var (pill, text, v) in pills)
            {
                var on = EqualityComparer<T>.Default.Equals(v, value);
                pill.Background = on ? Theme.Ink : Brushes.Transparent;
                text.Foreground = on ? Theme.OnInk : Theme.Muted;
            }
        }
        foreach (var (label, value) in options)
        {
            var text = new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            Typo.TrimToCaps(text);
            var pill = new PillBorder
            {
                Padding = new Thickness(15, 12, 15, 12),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = text,
            };
            var v = value;
            pill.MouseLeftButtonUp += (_, _) =>
            {
                Select(v);
                changed(v);
            };
            pills.Add((pill, text, value));
            panel.Children.Add(pill);
        }
        Select(selected);
        return new PillBorder
        {
            Background = Theme.Track,
            Padding = new Thickness(4),
            Child = panel,
        };
    }

    /// <summary>
    /// An ink pill showing the current choice; clicking it opens a list of the options under it.
    /// Used on the colour tiles, where a segmented row would not fit.
    /// </summary>
    public static Border Dropdown<T>(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> changed)
    {
        var current = options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, selected)).Label ?? "";
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var caption = new TextBlock { Text = current.ToUpperInvariant(), FontSize = 14, FontWeight = FontWeights.ExtraBold, Foreground = Theme.TileStrongText, VerticalAlignment = VerticalAlignment.Center };
        Typo.TrimToCaps(caption);
        content.Children.Add(caption);
        var chevron = Icon(Sweetch.Core.Glyphs.ChevronDown, 20, Theme.TileStrongText);
        chevron.Margin = new Thickness(4, 0, 0, 0);
        Typo.TrimToEm(chevron);
        content.Children.Add(chevron);
        var pill = new PillBorder
        {
            Background = Theme.TileStrong,
            Padding = new Thickness(16, 9, 10, 9),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = content,
        };

        var list = new StackPanel();
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = pill,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 6,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.PanelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 14, 14),
                MinWidth = 170,
                Effect = Theme.Shadow(18),
                Child = list,
            },
        };
        foreach (var (label, value) in options)
        {
            var on = EqualityComparer<T>.Default.Equals(value, selected);
            var row = new DockPanel { LastChildFill = true };
            if (on)
            {
                var check = Icon(Sweetch.Core.Glyphs.Check, 18);
                DockPanel.SetDock(check, Dock.Right);
                row.Children.Add(check);
            }
            row.Children.Add(Text(label, 14.5, FontWeights.SemiBold, wrap: false));
            var item = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 10, 10), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row };
            item.MouseEnter += (_, _) => item.Background = Theme.Hover;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            var v = value;
            item.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                changed(v);
            };
            list.Children.Add(item);
        }
        pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            popup.IsOpen = !popup.IsOpen;
        };
        return pill;
    }

    /// <summary>A large pill switch: ink when on.</summary>
    public static FrameworkElement Toggle(bool value, Action<bool> changed)
    {
        var knob = new Ellipse { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border
        {
            Width = 54,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Cursor = Cursors.Hand,
            Child = knob,
        };
        var translate = new TranslateTransform();
        knob.RenderTransform = translate;
        var on = value;
        void Paint(bool animate)
        {
            track.Background = on ? Theme.Ink : Theme.Track;
            knob.Fill = on ? Theme.OnInk : Theme.Muted;
            var x = on ? 26.0 : 4.0;
            if (animate)
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else
                translate.X = x;
        }
        track.MouseLeftButtonUp += (_, _) =>
        {
            on = !on;
            Paint(true);
            changed(on);
        };
        Paint(false);
        return track;
    }

    /// <summary>A slider with an ink-filled track and a round ink thumb.</summary>
    public static FrameworkElement Slider(int min, int max, int value, Action<int> changed, Action committed)
    {
        const double width = 220;
        const double thumbSize = 22;
        var label = new TextBlock { Width = 48, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Theme.Text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Left };
        var thumb = new Ellipse
        {
            Width = thumbSize,
            Height = thumbSize,
            Fill = Theme.Ink,
            Stroke = Theme.Card,
            StrokeThickness = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var surface = new Grid { Width = width, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        surface.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Track });
        surface.Children.Add(fill);
        surface.Children.Add(thumb);

        var current = value;
        void Show()
        {
            var t = (current - min) / (double)(max - min);
            fill.Width = Math.Max(8, thumbSize / 2 + t * (width - thumbSize));
            thumb.Margin = new Thickness(t * (width - thumbSize), 0, 0, 0);
            label.Text = current.ToString();
        }
        void SetFrom(Point p)
        {
            var t = Math.Clamp((p.X - thumbSize / 2) / (width - thumbSize), 0, 1);
            var v = (int)Math.Round(min + t * (max - min));
            if (v == current)
                return;
            current = v;
            Show();
            changed(v);
        }
        surface.MouseLeftButtonDown += (_, e) =>
        {
            surface.CaptureMouse();
            SetFrom(e.GetPosition(surface));
        };
        surface.MouseMove += (_, e) =>
        {
            if (surface.IsMouseCaptured)
                SetFrom(e.GetPosition(surface));
        };
        surface.MouseLeftButtonUp += (_, _) =>
        {
            surface.ReleaseMouseCapture();
            committed();
        };
        Show();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(surface);
        row.Children.Add(label);
        return row;
    }

    /// <summary>
    /// A pill button. <paramref name="primary"/> ones are filled with ink; others sit on the track colour.
    /// <paramref name="onTile"/> buttons are meant for the bright colour tiles.
    /// </summary>
    public static Border Button(string text, Action click, bool primary = false, string? glyph = null, bool onTile = false)
    {
        var background = onTile
            ? primary ? Theme.TileStrong : Theme.TileSoft
            : primary ? Theme.Ink : Theme.Track;
        var foreground = onTile
            ? primary ? Theme.TileStrongText : Theme.OnKey
            : primary ? Theme.OnInk : Theme.Text;
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph is not null)
        {
            var icon = Icon(glyph, 18, foreground);
            icon.Margin = new Thickness(0, 0, 8, 0);
            Typo.TrimToEm(icon);
            content.Children.Add(icon);
        }
        var label = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center };
        Typo.TrimToCaps(label);
        content.Children.Add(label);
        var button = new PillBorder
        {
            Background = background,
            MinHeight = 40,
            Padding = new Thickness(glyph is null ? 18 : 15, 11, 18, 11),
            Cursor = Cursors.Hand,
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.MouseEnter += (_, _) => button.Opacity = 0.85;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    /// <summary>A single-line text box on the track colour.</summary>
    public static TextBox TextField(string text, int maxLength = 200) => new()
    {
        Text = text,
        MaxLength = maxLength,
        Padding = new Thickness(12, 10, 12, 10),
        FontSize = 14.5,
        Background = Theme.Track,
        Foreground = Theme.Text,
        CaretBrush = Theme.Text,
        BorderThickness = new Thickness(0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>A muted label above a field.</summary>
    public static TextBlock FieldLabel(string text, double top = 16)
    {
        var label = Text(text, 13.5, FontWeights.SemiBold, Theme.Muted);
        label.Margin = new Thickness(0, top, 0, 8);
        return label;
    }

    /// <summary>A small coloured status label, e.g. "found" / "missing".</summary>
    public static Border Badge(string text, bool good) => new PillBorder
    {
        Padding = new Thickness(11, 4, 11, 5),
        Background = good ? Theme.Mint : Theme.Pink,
        Child = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = Theme.OnKey },
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>
/// A border with fully round ends. WPF distorts a corner radius larger than half the height into an
/// oval, so the radius follows the actual height instead.
/// </summary>
internal sealed class PillBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        CornerRadius = new CornerRadius(sizeInfo.NewSize.Height / 2);
    }
}
