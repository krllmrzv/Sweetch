using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sweetch.App.Native;
using Sweetch.App.UI;
using Sweetch.Core;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Jobs;

/// <summary>Progress cards in the bottom-right corner. Never steals focus.</summary>
internal sealed class JobsWindow : Window
{
    private readonly StackPanel _cards = new();

    public JobsWindow()
    {
        Title = T("Sweetch – progress", "Sweetch — ход выполнения");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        FontFamily = Theme.UiFont;
        Content = _cards;
        SourceInitialized += (_, _) =>
            Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        SizeChanged += (_, _) => PlaceInCorner();
    }

    public JobCard Add(string title, string detail, Action cancel, string? iconPath = null)
    {
        Theme.Refresh();
        var card = new JobCard(title, detail, iconPath, cancel, Remove);
        _cards.Children.Insert(0, card);
        if (!IsVisible)
            Show();
        PlaceInCorner();
        return card;
    }

    private void Remove(JobCard card)
    {
        _cards.Children.Remove(card);
        if (_cards.Children.Count == 0)
            Hide();
    }

    private void PlaceInCorner()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 8;
        Top = work.Bottom - ActualHeight - 8;
    }
}

internal sealed class JobCard : Grid
{
    /// <summary>Off in the self-test, where offscreen rendering doesn't advance animations.</summary>
    public static bool Animate { get; set; } = true;

    private readonly Border _fill = new() { CornerRadius = new CornerRadius(2), Background = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Grid _bar = new() { Height = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _status;
    private readonly WrapPanel _buttons;
    private readonly TextBlock _badge;
    private readonly Action<JobCard> _remove;
    private readonly Action _cancel;
    private double _value;
    private bool _done;

    public JobCard(string title, string detail, string? iconPath, Action cancel, Action<JobCard> remove)
    {
        _cancel = cancel;
        _remove = remove;
        Margin = new Thickness(12, 6, 12, 6);
        // The shadow is its own cached layer, so progress updates redraw the card but not the blur.
        Children.Add(new Border
        {
            CacheMode = new BitmapCache(),
            Child = new Border { Background = Theme.Panel, CornerRadius = new CornerRadius(14), Effect = Theme.Shadow(22) },
        });
        var surface = new Border
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 12, 14),
        };
        Children.Add(surface);

        var icon = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Theme.Track });
        var image = iconPath is null ? null : Sweetch.App.Shell.FileIcons.Get(iconPath);
        if (image is not null)
            icon.Children.Add(new Image { Source = image, Width = 28, Height = 28 });
        _badge = new TextBlock
        {
            FontFamily = Theme.Icons,
            FontSize = 11,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Grid { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -4, -4), Visibility = Visibility.Collapsed };
        badge.Children.Add(new Ellipse { Fill = Theme.Success, Stroke = Theme.Panel, StrokeThickness = 2 });
        badge.Children.Add(_badge);
        icon.Children.Add(badge);
        _badgeHost = badge;

        var titleText = new TextBlock { Text = title, Foreground = Theme.Text, FontFamily = Theme.DisplayFont, FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        var detailText = new TextBlock { Text = detail, Foreground = Theme.Muted, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        _bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Theme.Track });
        _bar.Children.Add(_fill);
        _bar.SizeChanged += (_, _) => _fill.Width = _bar.ActualWidth * _value;
        StartPulse();
        _status = new TextBlock { Foreground = Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        // Wraps: "Open" and "Show in folder" don't fit side by side on the narrow card.
        _buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, -8) };
        AddButton(UI.Controls.Button(T("Cancel", "Отмена"), () => _cancel()));

        var close = new TextBlock
        {
            Text = Glyphs.Close,
            FontFamily = Theme.Icons,
            FontSize = 10,
            Foreground = Theme.Muted,
            Padding = new Thickness(6),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = T("Close", "Закрыть"),
        };
        close.MouseLeftButtonUp += (_, _) =>
        {
            if (!_done)
                _cancel();
            _remove(this);
        };

        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(titleText);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(detailText);
        body.Children.Add(_bar);
        body.Children.Add(_status);
        body.Children.Add(_buttons);

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(body);
        surface.Child = row;

        if (!Animate)
            return;

        // Slide in from the right.
        var slide = new TranslateTransform(40, 0);
        RenderTransform = slide;
        Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private readonly Grid _badgeHost;

    private void StartPulse()
    {
        if (!Animate)
        {
            _fill.Width = 48;
            return;
        }
        // Indeterminate state: a short segment breathing until real progress arrives.
        _fill.Width = 48;
        // A slow breath needs few frames; 60 per second would redraw the window (and its shadow) all job long.
        var pulse = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(pulse, 12);
        _fill.BeginAnimation(OpacityProperty, pulse);
    }

    public void Report(double value)
    {
        // Downloads report every few kilobytes; a redraw per half percent is plenty.
        if (_done || value <= 0 || Math.Abs(value - _value) < 0.005)
            return;
        if (_value <= 0)
        {
            _fill.BeginAnimation(OpacityProperty, null);
            _fill.Opacity = 1;
        }
        _value = Math.Clamp(value, 0, 1);
        _fill.Width = _bar.ActualWidth * _value;
    }

    public void Complete(IReadOnlyList<string> outputs, string? warning, string? note = null)
    {
        _done = true;
        SetFull(warning is null ? Theme.Success : Theme.Error);
        ShowBadge(warning is null ? Glyphs.Check : Glyphs.Warning, warning is null ? Theme.Success : Theme.Error);
        _status.Visibility = Visibility.Visible;
        _status.Foreground = warning is null ? Theme.Muted : Theme.Error;
        _status.Text = warning ?? outputs.Count switch
        {
            0 => note ?? T("Done", "Готово"),
            1 => T("Done: ", "Готово: ") + System.IO.Path.GetFileName(outputs[0]),
            _ => T($"Done: {outputs.Count} outputs", $"Готово: файлов — {outputs.Count}"),
        };
        if (warning is null && note is not null && outputs.Count > 0)
            _status.Text += Environment.NewLine + note;
        _buttons.Children.Clear();
        if (outputs.Count > 0)
        {
            var first = outputs[0];
            if (File.Exists(first) && outputs.Count == 1)
                AddButton(UI.Controls.Button(T("Open", "Открыть"), () => Shell(first), primary: true));
            AddButton(UI.Controls.Button(T("Show in folder", "Показать в папке"), () => Reveal(first), glyph: Glyphs.Folder));
        }
        if (warning is null)
            CloseLater(TimeSpan.FromSeconds(8));
    }

    public void Fail(string message)
    {
        _done = true;
        SetFull(Theme.Error);
        ShowBadge(Glyphs.Close, Theme.Error);
        _status.Visibility = Visibility.Visible;
        _status.Foreground = Theme.Error;
        _status.Text = message;
        _buttons.Children.Clear();
        AddButton(UI.Controls.Button(T("Copy error", "Копировать ошибку"), () =>
        {
            try
            {
                Clipboard.SetText(message);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        }, glyph: Glyphs.Copy));
    }

    public void Cancelled()
    {
        _done = true;
        _remove(this);
    }

    private void SetFull(Brush brush)
    {
        _fill.BeginAnimation(OpacityProperty, null);
        _fill.Opacity = 1;
        _value = 1;
        _fill.Background = brush;
        _fill.Width = _bar.ActualWidth > 0 ? _bar.ActualWidth : 1000;
        _fill.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private void ShowBadge(string glyph, Brush color)
    {
        _badge.Text = glyph;
        ((Ellipse)_badgeHost.Children[0]).Fill = color;
        _badgeHost.Visibility = Visibility.Visible;
    }

    private void AddButton(Border button)
    {
        button.Margin = new Thickness(0, 0, 8, 8);
        _buttons.Children.Add(button);
    }

    private void CloseLater(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsMouseOver)
                _remove(this);
            else
                CloseLater(TimeSpan.FromSeconds(3));
        };
        timer.Start();
    }

    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void Shell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
