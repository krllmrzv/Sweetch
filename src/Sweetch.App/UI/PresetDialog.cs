using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Sweetch.Core;
using static Sweetch.App.UI.Controls;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>Creates or edits a preset: name, kind, format and the few settings that matter for it.</summary>
internal sealed class PresetDialog : Window
{
    private readonly Preset _preset;
    private readonly TextBox _name;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public PresetDialog(Preset? existing)
    {
        _preset = existing is null ? new Preset { Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920 } : Copy(existing);
        _name = TextField(_preset.Name, 40);
        _name.TextChanged += (_, _) => _preset.Name = _name.Text;
        Theme.Refresh();
        Title = existing is null ? T("New preset", "Новый шаблон") : T("Edit preset", "Изменить шаблон");
        Width = 620;
        Height = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Sweetch.ico"));
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;
        Content = _scroll;
        Build();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        };
        Loaded += (_, _) => Keyboard.Focus(_name);
    }

    /// <summary>The preset as edited; valid after the dialog returned true.</summary>
    public Preset Result => _preset;

    private static Preset Copy(Preset p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Kind = p.Kind,
        Format = p.Format,
        Quality = p.Quality,
        MaxSide = p.MaxSide,
        Percent = p.Percent,
        Grayscale = p.Grayscale,
        StripMetadata = p.StripMetadata,
        Height = p.Height,
        VideoQuality = p.VideoQuality,
        Mute = p.Mute,
        Bitrate = p.Bitrate,
    };

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        var title = Text(Title, 24, FontWeights.SemiBold);
        title.FontFamily = Theme.DisplayFont;
        root.Children.Add(title);
        var intro = Text(T("A slice of your own with fixed settings. Format changes appear on the Convert wheel, same-format edits on the Tools wheel.", "Свой сектор с фиксированными настройками. Смена формата появится в меню конвертации, правки без смены формата — в инструментах."),
            13, brush: Theme.Muted);
        intro.Margin = new Thickness(0, 4, 0, 4);
        root.Children.Add(intro);

        // The text box survives rebuilds (kind or format changes), so it must leave its old parent first.
        (_name.Parent as Panel)?.Children.Remove(_name);
        root.Children.Add(FieldLabel(T("Name on the wheel", "Название в меню")));
        root.Children.Add(_name);
        root.Children.Add(Hint(T("Empty = the description is used, e.g. “JPG · quality 80”.", "Пусто = описание, например «JPG · качество 80».")));

        root.Children.Add(FieldLabel(T("For", "Для")));
        root.Children.Add(AlignLeft(Segmented(
            [(T("Images", "Изображения"), PresetKind.Image), ("Video", PresetKind.Video), (T("Audio", "Аудио"), PresetKind.Audio)],
            _preset.Kind,
            kind =>
            {
                if (kind == _preset.Kind)
                    return;
                _preset.Kind = kind;
                _preset.Format = PresetOperations.FormatsFor(kind)[kind == PresetKind.Image ? 1 : 0];
                Rebuild();
            })));

        root.Children.Add(FieldLabel(T("Output format", "Выходной формат")));
        root.Children.Add(AlignLeft(Segmented(
            [.. PresetOperations.FormatsFor(_preset.Kind).Select(f => (f == Preset.SameFormat ? T("Same", "Тот же") : Formats.Display(f), f))],
            _preset.Format,
            format =>
            {
                _preset.Format = format;
                Rebuild();
            })));

        switch (_preset.Kind)
        {
            case PresetKind.Image:
                ImageOptions(root);
                break;
            case PresetKind.Video:
                VideoOptions(root);
                break;
            default:
                AudioOptions(root);
                break;
        }

        var wheel = _preset.Format == Preset.SameFormat ? AppSettings.WheelName(WheelKind.Tools) : AppSettings.WheelName(WheelKind.Convert);
        var summary = Text(T($"{wheel} wheel · {PresetOperations.Summary(_preset)}", $"Меню «{wheel}» · {PresetOperations.Summary(_preset)}"), 13, brush: Theme.Muted);
        summary.Margin = new Thickness(0, 22, 0, 0);
        root.Children.Add(summary);

        var save = Button(T("Save", "Сохранить"), () => DialogResult = true, primary: true, glyph: Glyphs.Save);
        var cancel = Button(T("Cancel", "Отмена"), () => DialogResult = false);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        _scroll.Content = root;
    }

    private void Rebuild()
    {
        var offset = _scroll.VerticalOffset;
        Build();
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void ImageOptions(Panel root)
    {
        if (!PresetOperations.IsLossless(_preset.Format))
        {
            root.Children.Add(FieldLabel(T("Quality", "Качество")));
            root.Children.Add(AlignLeft(Slider(30, 100, _preset.Quality, v => _preset.Quality = v, Rebuild)));
        }
        root.Children.Add(FieldLabel(T("Size", "Размер")));
        var size = _preset.MaxSide is { } m ? "max" + m : _preset.Percent is { } p ? p + "%" : "keep";
        root.Children.Add(AlignLeft(Segmented(
            [(T("Original", "Исходный"), "keep"), ("50 %", "50%"), ("Max 3840", "max3840"), ("Max 1920", "max1920"), ("Max 1280", "max1280"), ("Max 800", "max800")],
            size,
            value =>
            {
                _preset.MaxSide = value.StartsWith("max", StringComparison.Ordinal) ? int.Parse(value[3..], System.Globalization.CultureInfo.InvariantCulture) : null;
                _preset.Percent = value.EndsWith('%') ? int.Parse(value[..^1], System.Globalization.CultureInfo.InvariantCulture) : null;
                Rebuild();
            })));
        root.Children.Add(Hint(T("Max = longer side in pixels; smaller images are not enlarged.", "Макс. = длинная сторона в пикселях; маленькие изображения не увеличиваются.")));
        root.Children.Add(OptionRow(T("Grayscale", "Оттенки серого"), _preset.Grayscale, v => _preset.Grayscale = v));
        root.Children.Add(OptionRow(T("No metadata (GPS, EXIF)", "Без метаданных (GPS, EXIF)"), _preset.StripMetadata, v => _preset.StripMetadata = v));
    }

    private void VideoOptions(Panel root)
    {
        root.Children.Add(FieldLabel(T("Resolution", "Разрешение")));
        root.Children.Add(AlignLeft(Segmented<int?>(
            [(T("Original", "Исходный"), null), ("1080p", 1080), ("720p", 720), ("480p", 480)],
            _preset.Height,
            h =>
            {
                _preset.Height = h;
                Rebuild();
            })));
        root.Children.Add(FieldLabel(T("Quality", "Качество")));
        root.Children.Add(AlignLeft(Segmented(
            [(T("High", "Высокое"), VideoQuality.High), (T("Medium", "Среднее"), VideoQuality.Medium), (T("Small file", "Маленький файл"), VideoQuality.Small)],
            _preset.VideoQuality,
            q =>
            {
                _preset.VideoQuality = q;
                Rebuild();
            })));
        root.Children.Add(OptionRow(T("Mute", "Без звука"), _preset.Mute, v => _preset.Mute = v));
    }

    private void AudioOptions(Panel root)
    {
        if (PresetOperations.IsLossless(_preset.Format))
        {
            root.Children.Add(Hint(T("FLAC is lossless; there is nothing else to set.", "FLAC — формат без потерь; дополнительные настройки не нужны.")));
            return;
        }
        root.Children.Add(FieldLabel(T("Bitrate", "Битрейт")));
        root.Children.Add(AlignLeft(Segmented(
            [("96k", 96), ("128k", 128), ("192k", 192), ("256k", 256), ("320k", 320)],
            _preset.Bitrate,
            b =>
            {
                _preset.Bitrate = b;
                Rebuild();
            })));
        root.Children.Add(Hint(T("Works on video too: it extracts the audio.", "Работает и с видео: извлекает звуковую дорожку.")));
    }

    private FrameworkElement OptionRow(string label, bool value, Action<bool> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var toggle = Toggle(value, v =>
        {
            changed(v);
            Dispatcher.BeginInvoke(Rebuild, System.Windows.Threading.DispatcherPriority.Background);
        });
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        var text = Text(label, 14);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    private static FrameworkElement AlignLeft(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        return element;
    }

    private static TextBlock Hint(string text)
    {
        var hint = Text(text, 12, brush: Theme.Muted);
        hint.Margin = new Thickness(0, 6, 0, 0);
        return hint;
    }
}
