using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sweetch.App.Input;
using Sweetch.App.Wheel;
using Sweetch.Core;
using static Sweetch.App.UI.Controls;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>
/// First start (and "How it works" in the tray): four short steps with the real wheels drawn as they
/// will open — the gesture, the three keys, the second page, and the ways in without the mouse.
/// </summary>
internal sealed class TutorialWindow : Window
{
    private const int Steps = 4;
    private static TutorialWindow? _open;
    private readonly AppSettings _settings;
    private readonly Action _openSettings;
    private readonly IReadOnlyList<Operation> _ops;
    private int _step;

    private TutorialWindow(AppSettings settings, Action openSettings)
    {
        _settings = settings;
        _openSettings = openSettings;
        _ops = SettingsWindow.Operations?.Invoke() ?? WheelController.BuildOperations();
        Theme.Refresh();
        Title = T("How Sweetch works", "Как работает Sweetch");
        Width = 900;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Sweetch.ico"));
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Closed += (_, _) => _open = null;
        Build();
    }

    public static void ShowSingle(AppSettings settings, Action openSettings)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new TutorialWindow(settings, openSettings);
        _open.Show();
        _open.Activate();
    }

    /// <summary>For the self-test screenshots.</summary>
    internal static TutorialWindow ForPreview(AppSettings settings, int step)
    {
        var window = new TutorialWindow(settings, () => { });
        window.ShowStep(step);
        return window;
    }

    internal void ShowStep(int step)
    {
        _step = step;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(40, 34, 40, 28) };
        var (title, text, visual) = _step switch
        {
            0 => Gesture(),
            1 => ThreeKeys(),
            2 => Pages(),
            _ => OtherWays(),
        };
        var counter = Text(T($"Step {_step + 1} of {Steps}", $"Шаг {_step + 1} из {Steps}"), 13, FontWeights.SemiBold, Theme.Muted);
        root.Children.Add(counter);
        var heading = Heading(title, 34);
        heading.Margin = new Thickness(0, 6, 0, 10);
        root.Children.Add(heading);
        var body = Text(text, 15.5, brush: Theme.Muted);
        body.Margin = new Thickness(0, 0, 0, 22);
        root.Children.Add(body);
        root.Children.Add(new Border { Background = Theme.Card, CornerRadius = new CornerRadius(CardRadius), Padding = new Thickness(22), Height = 360, Child = visual });
        root.Children.Add(Footer());
        Content = root;
    }

    private UIElement Footer()
    {
        var row = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < Steps; i++)
        {
            dots.Children.Add(new Border
            {
                Width = i == _step ? 26 : 9,
                Height = 9,
                CornerRadius = new CornerRadius(4.5),
                Background = Theme.Ink,
                Opacity = i == _step ? 1 : 0.25,
                Margin = new Thickness(0, 0, 6, 0),
            });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (_step > 0)
            buttons.Children.Add(Spaced(Button(T("Back", "Назад"), () => ShowStep(_step - 1))));
        if (_step < Steps - 1)
        {
            buttons.Children.Add(Spaced(Button(T("Skip", "Пропустить"), Close)));
            buttons.Children.Add(Button(T("Next", "Далее"), () => ShowStep(_step + 1), primary: true, glyph: Glyphs.ChevronRight));
        }
        else
        {
            buttons.Children.Add(Spaced(Button(T("Open settings", "Открыть настройки"), () =>
            {
                Close();
                _openSettings();
            }, glyph: Glyphs.Settings)));
            buttons.Children.Add(Button(T("Let's go", "Начать"), Close, primary: true, glyph: Glyphs.Check));
        }
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(dots);
        return row;
    }

    private static FrameworkElement Spaced(FrameworkElement button)
    {
        button.Margin = new Thickness(0, 0, 10, 0);
        return button;
    }

    // ------------------------------------------------------------------ steps

    private (string, string, UIElement) Gesture()
    {
        var key = AppSettings.KeyName(_settings.ConvertKey);
        return (T($"Drag a file, tap {key}", $"Тащите файл, нажмите {key}"),
            T($"Grab a file with the mouse — in Explorer, on the desktop, a picture in the browser. While it's moving, tap {key}: the wheel opens at the pointer. Drop the file on a format. Dropping in the middle cancels.",
              $"Возьмите файл мышью — в Проводнике, на рабочем столе, картинку в браузере. Не отпуская, нажмите и отпустите {key}: у курсора появится колесо. Отпустите файл на нужном формате. В центре — отмена."),
            Row(Wheel(WheelKind.Convert, "photo.jpg", highlight: "image.to.webp", back: false),
                Steps3([
                    (Glyphs.Drag, T("Grab a file", "Возьмите файл")),
                    (Glyphs.Keyboard, T($"Tap {key}", $"Нажмите {key}")),
                    (Glyphs.Check, T("Drop on a format", "Отпустите на формате")),
                ])));
    }

    private (string, string, UIElement) ThreeKeys()
    {
        var wheels = new UniformGrid3();
        foreach (var wheel in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
            wheels.Add(Wheel(wheel, "photo.jpg", highlight: null, back: false), $"{AppSettings.KeyName(_settings.KeyFor(wheel))} — {AppSettings.WheelName(wheel)}");
        return (T("Three keys, three wheels", "Три клавиши — три колеса"),
            T("Convert to another format, tools (compress, shrink, trim…), actions (copy, move, share…). Tap another key to switch wheels while it's open; tap the same key again to repeat the last action.",
              "Конвертация в другой формат, инструменты (сжать, уменьшить, обрезать…), действия (копировать, переместить, поделиться…). Пока колесо открыто, другая клавиша переключает его, а та же — повторяет последнее действие."),
            wheels.Element);
    }

    private (string, string, UIElement) Pages() =>
        (T("Scroll or Tab for page two", "Колёсико или Tab — вторая страница"),
         T("Rare formats wait on page two, so the first stays roomy. Turn the mouse wheel or press Tab while the wheel is open — also while dragging. The tab under the middle shows the page: 1 / 2. Files with nothing rare have one page.",
           "Редкие форматы ждут на второй странице, чтобы на первой было просторно. Покрутите колёсико мыши или нажмите Tab, пока меню открыто, — и во время перетаскивания тоже. Язычок под центром показывает страницу: 1 / 2. Если редкого для файла нет, страница одна."),
         Row(Wheel(WheelKind.Convert, "photo.jpg", highlight: null, back: false, pages: 2),
             Flip(),
             Wheel(WheelKind.Convert, "photo.jpg", highlight: null, back: true, pages: 2)));

    private (string, string, UIElement) OtherWays()
    {
        var grid = new UniformGrid3(columns: 2);
        grid.Add(Way(Glyphs.OpenLink, T("Right-click a file", "Правая кнопка по файлу"), T("“Sweetch…” in Explorer's menu opens the wheel.", "Пункт «Sweetch…» в меню Проводника открывает колесо.")), null);
        grid.Add(Way(Glyphs.Keyboard, AppSettings.HotkeyName(_settings.Hotkey), T("Select files (or copy a screenshot) and press it.", "Выделите файлы (или скопируйте снимок экрана) и нажмите.")), null);
        grid.Add(Way(Glyphs.Download, T("Helper programs", "Доп. программы"), T("FFmpeg and Pandoc download by themselves the first time they're needed.", "FFmpeg и Pandoc скачаются сами, когда понадобятся.")), null);
        grid.Add(Way(Glyphs.Settings, T("Settings", "Настройки"), T("Keys, favourite slices, presets — in the tray icon's menu.", "Клавиши, избранные секторы, шаблоны — в меню значка в трее.")), null);
        return (T("And without the mouse", "А ещё без мыши"),
            T("Results are saved next to the original; the original is never changed.", "Результат сохраняется рядом с исходным файлом, исходник не меняется."),
            grid.Element);
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>A wheel drawn exactly as it opens for <paramref name="sample"/>: its page, its key colour.</summary>
    private FrameworkElement Wheel(WheelKind wheel, string sample, string? highlight, bool back, int pages = 1)
    {
        IReadOnlyList<FileItem> items = [new FileItem(sample)];
        var ops = WheelLayout.Arrange(_ops.Where(o => o.Wheel == wheel && o.AppliesTo(items) && (wheel == WheelKind.Actions || _settings.IsBackSide(o) == back)),
            _settings.HiddenOperations, _settings.FavoriteOperations);
        var key = _settings.KeyFor(wheel);
        var title = highlight is not null ? ops.FirstOrDefault(o => o.Id == highlight)?.Label ?? AppSettings.WheelName(wheel) : AppSettings.WheelName(wheel);
        var preview = WheelWindow.Preview(ops, key, wheel, AppSettings.KeyName(key), title, sample, highlight is null ? null : new HashSet<string> { highlight }, back, pages);
        preview.Width = 280;
        preview.Height = 280;
        return preview;
    }

    private static UIElement Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
        {
            if (child is FrameworkElement fe)
                fe.Margin = new Thickness(14, 0, 14, 0);
            row.Children.Add(child);
        }
        return row;
    }

    private static UIElement Steps3(IReadOnlyList<(string Glyph, string Text)> steps)
    {
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 300 };
        for (var i = 0; i < steps.Count; i++)
        {
            var line = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 18, 0, 0) };
            var number = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = Theme.KeyBrush(ModifierKey.Shift, WheelKind.Convert),
                Child = Icon(steps[i].Glyph, 20, Theme.OnKey),
                Margin = new Thickness(0, 0, 14, 0),
            };
            ((TextBlock)number.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)number.Child).VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(number, Dock.Left);
            line.Children.Add(number);
            var label = Text($"{i + 1}. {steps[i].Text}", 17, FontWeights.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(label);
            list.Children.Add(line);
        }
        return list;
    }

    /// <summary>The mouse with an arrow down: "turn the wheel" between the two pages.</summary>
    private static UIElement Flip()
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var mouse = Icon(Glyphs.Mouse, 54, Theme.Ink);
        mouse.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(mouse);
        var arrow = Icon(Glyphs.ChevronRight, 34, Theme.Muted);
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(arrow);
        return panel;
    }

    private static UIElement Way(string glyph, string title, string text)
    {
        var panel = new DockPanel { Margin = new Thickness(6, 8, 6, 8) };
        var icon = Icon(glyph, 26, Theme.Ink);
        icon.Margin = new Thickness(0, 2, 14, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);
        var words = new StackPanel();
        words.Children.Add(Text(title, 16.5, FontWeights.Bold));
        var detail = Text(text, 14, brush: Theme.Muted);
        detail.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(detail);
        panel.Children.Add(words);
        return panel;
    }

    /// <summary>Equal columns of pieces, each with an optional caption underneath.</summary>
    private sealed class UniformGrid3(int columns = 3)
    {
        private readonly System.Windows.Controls.Primitives.UniformGrid _grid = new() { Columns = columns, VerticalAlignment = VerticalAlignment.Center };

        public UIElement Element => _grid;

        public void Add(UIElement piece, string? caption)
        {
            // Wheels with captions sit centred; text pieces line up on the left.
            var cell = new StackPanel { HorizontalAlignment = caption is null ? HorizontalAlignment.Stretch : HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (piece is FrameworkElement fe && caption is not null)
            {
                fe.Width = 220;
                fe.Height = 220;
            }
            cell.Children.Add(piece);
            if (caption is not null)
            {
                var label = Text(caption, 14.5, FontWeights.SemiBold);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.Margin = new Thickness(0, 10, 0, 0);
                cell.Children.Add(label);
            }
            _grid.Children.Add(cell);
        }
    }
}
