using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Sweetch.App.Input;
using Sweetch.App.Shell;
using Sweetch.Core;
using static Sweetch.App.UI.Controls;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>The settings pages shown in the sidebar.</summary>
internal enum SettingsPage
{
    General,
    Gestures,
    Wheels,
    Presets,
    WatchFolders,
    Quality,
    Helpers,
    About,
}

/// <summary>Settings with a sidebar of pages; each page is a column of rounded cards.</summary>
internal sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    /// <summary>Set by the app: the update checker and the operations the wheel page lists.</summary>
    public static Shell.Updater? Updater { get; set; }

    public static Func<IReadOnlyList<Operation>>? Operations { get; set; }

    /// <summary>Downloads FFmpeg or Pandoc in the app (set by the app).</summary>
    public static Func<ExternalTool, Task<bool>>? DownloadTool { get; set; }
    private readonly AppSettings _settings;
    private readonly ToolLocator _tools;
    private readonly Action _changed;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private SettingsPage _page = SettingsPage.General;

    internal SettingsWindow(AppSettings settings, ToolLocator tools, Action changed)
    {
        _settings = settings;
        _tools = tools;
        _changed = changed;
        Theme.Refresh();
        Title = T("Sweetch – settings", "Sweetch — настройки");
        Width = 1180;
        Height = 820;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Sweetch.ico"));
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Build();
        _scroll.SizeChanged += (_, _) =>
        {
            if (_page == SettingsPage.Wheels && WheelsFit != _wheelsWide)
                Rebuild();
        };
        Action refresh = () => Dispatcher.BeginInvoke(Rebuild);
        if (Updater is not null)
            Updater.Changed += refresh;
        Closed += (_, _) =>
        {
            _open = null;
            if (Updater is not null)
                Updater.Changed -= refresh;
        };
    }

    public static void ShowSingle(AppSettings settings, ToolLocator tools, Action changed, SettingsPage? page = null)
    {
        _open ??= new SettingsWindow(settings, tools, changed);
        if (page is { } p)
            _open.Page = p;
        _open.Show();
        _open.Activate();
    }

    /// <summary>Redraws the open settings window after the app changed a setting itself.</summary>
    public static void RefreshIfOpen() => _open?.Rebuild();

    /// <summary>The page on screen; setting it switches pages.</summary>
    public SettingsPage Page
    {
        get => _page;
        set
        {
            _page = value;
            Build();
            _scroll.ScrollToTop();
        }
    }

    /// <summary>Lets the self-test render a whole page instead of the visible part.</summary>
    internal void ExpandForPreview() => _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

    private void Save()
    {
        _settings.Save();
        _changed();
    }

    /// <summary>Rebuilds the page (used when one change affects others), keeping the scroll position.</summary>
    private void Rebuild()
    {
        var offset = _scroll.VerticalOffset;
        Build();
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Build()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = Sidebar();
        grid.Children.Add(sidebar);

        var page = new StackPanel { Margin = new Thickness(44, 40, 44, 44), MaxWidth = _page == SettingsPage.Wheels ? 1100 : 820, HorizontalAlignment = HorizontalAlignment.Left };
        switch (_page)
        {
            case SettingsPage.General:
                GeneralPage(page);
                break;
            case SettingsPage.Gestures:
                GesturesPage(page);
                break;
            case SettingsPage.Wheels:
                WheelsPage(page);
                break;
            case SettingsPage.Presets:
                PresetsPage(page);
                break;
            case SettingsPage.WatchFolders:
                WatchFoldersPage(page);
                break;
            case SettingsPage.Quality:
                QualityPage(page);
                break;
            case SettingsPage.Helpers:
                HelpersPage(page);
                break;
            default:
                AboutPage(page);
                break;
        }
        if (MissingToolsBanner() is { } banner)
            page.Children.Insert(0, banner);
        _scroll.Content = page;
        // The scroll viewer outlives each rebuild (it keeps the position); free it from the old layout.
        (_scroll.Parent as Grid)?.Children.Remove(_scroll);
        Grid.SetColumn(_scroll, 1);
        grid.Children.Add(_scroll);
        Content = grid;
    }

    /// <summary>
    /// A reddish strip on top of every page while a helper program is missing, with a shortcut to the page that
    /// installs it. Closing it hides it for good.
    /// </summary>
    private Border? MissingToolsBanner()
    {
        var missing = Enum.GetValues<ExternalTool>().Where(t => !_tools.Has(t)).ToList();
        if (missing.Count == 0 || _settings.HideMissingTools || _page == SettingsPage.Helpers)
            return null;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(T("Install the helper programs", "Установите доп. программы"), 15, FontWeights.Bold));
        var names = string.Join(", ", missing.Select(ToolLocator.DisplayName));
        var detail = Text(T($"Some features need {names}.", $"Без {names} часть функций не работает."), 13.5, brush: Theme.Muted);
        detail.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(detail);

        var close = Icon(Glyphs.Close, 18, Theme.Muted);
        close.Cursor = Cursors.Hand;
        close.ToolTip = T("Don't show again", "Больше не показывать");
        close.Margin = new Thickness(14, 0, 0, 0);
        close.VerticalAlignment = VerticalAlignment.Center;
        close.MouseLeftButtonUp += (_, _) =>
        {
            _settings.HideMissingTools = true;
            Save();
            Rebuild();
        };
        var open = Button(T("Helper programs", "Доп. программы"), () => Page = SettingsPage.Helpers, primary: true, glyph: Glyphs.Extension);

        var warning = Icon(Glyphs.Warning, 24, Theme.Error);
        warning.Margin = new Thickness(0, 0, 14, 0);
        warning.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        DockPanel.SetDock(warning, Dock.Left);
        row.Children.Add(warning);
        DockPanel.SetDock(open, Dock.Right);
        row.Children.Add(open);
        row.Children.Add(text);
        RightOrUnder(row, open, room: 300, beside: new Thickness(16, 0, 0, 0), under: new Thickness(0, 12, 0, 0));

        return new Border
        {
            Background = Theme.Alert,
            CornerRadius = new CornerRadius(CardRadius),
            Padding = new Thickness(20, 16, 18, 16),
            Margin = new Thickness(0, 0, 0, 28),
            Child = row,
        };
    }

    private static string PageName(SettingsPage page) => page switch
    {
        SettingsPage.General => T("General", "Основные"),
        SettingsPage.Gestures => T("Gestures & keys", "Жесты и клавиши"),
        SettingsPage.Wheels => T("Wheels", "Круговые меню"),
        SettingsPage.Presets => T("Presets", "Шаблоны"),
        SettingsPage.WatchFolders => T("Watched folders", "Отслеживаемые папки"),
        SettingsPage.Quality => T("Quality", "Качество"),
        SettingsPage.Helpers => T("Helper programs", "Доп. программы"),
        _ => T("About", "О приложении"),
    };

    private static string PageGlyph(SettingsPage page) => page switch
    {
        SettingsPage.General => Glyphs.Settings,
        SettingsPage.Gestures => Glyphs.Keyboard,
        SettingsPage.Wheels => Glyphs.Wheel,
        SettingsPage.Presets => Glyphs.Bookmarks,
        SettingsPage.WatchFolders => Glyphs.FolderOpen,
        SettingsPage.Quality => Glyphs.Quality,
        SettingsPage.Helpers => Glyphs.Extension,
        _ => Glyphs.Info,
    };

    // ------------------------------------------------------------------ sidebar

    internal static Image Logo(double size) => new()
    {
        Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Sweetch.png")),
        Width = size,
        Height = size,
    };

    private Border Sidebar()
    {
        var dock = new DockPanel { Margin = new Thickness(14, 22, 14, 18) };

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 22) };
        var logo = Logo(42);
        logo.Margin = new Thickness(0, 0, 12, 0);
        brand.Children.Add(logo);
        var name = Heading("Sweetch", 22);
        name.VerticalAlignment = VerticalAlignment.Center;
        Typo.TrimToCaps(name);
        brand.Children.Add(name);
        DockPanel.SetDock(brand, Dock.Top);
        dock.Children.Add(brand);

        var version = Text(T("Version ", "Версия ") + Shell.Updater.Current, 12.5, FontWeights.Medium, Theme.Muted);
        version.Margin = new Thickness(8, 12, 0, 0);
        DockPanel.SetDock(version, Dock.Bottom);
        dock.Children.Add(version);
        var support = SupportTile();
        DockPanel.SetDock(support, Dock.Bottom);
        dock.Children.Add(support);

        var nav = new StackPanel();
        foreach (var page in Enum.GetValues<SettingsPage>())
            nav.Children.Add(NavItem(page));
        // Scrolls (by wheel) when the window is too short for every page plus the support tile; a bar here would
        // squeeze the page names.
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = nav, Margin = new Thickness(0, 0, 0, 12) });

        return new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock,
        };
    }

    /// <summary>The lime "Support the fork" block at the bottom of the sidebar; opens the About page.</summary>
    private Border SupportTile()
    {
        var content = new StackPanel();
        content.Children.Add(Icon(Glyphs.Heart, 28, Theme.OnKey));
        var label = Heading(T("Support the fork", "Поддержать форк"), 16, Theme.OnKey);
        label.Margin = new Thickness(0, 18, 0, 0);
        content.Children.Add(label);
        var by = Text(T($"{Brand.ForkAuthor}: redesign and speed-ups", $"{Brand.ForkAuthor}: редизайн и ускорение"), 12.5, FontWeights.SemiBold, Theme.OnKey);
        by.Opacity = 0.7;
        by.Margin = new Thickness(0, 4, 0, 0);
        content.Children.Add(by);
        var tile = new Border
        {
            Background = Theme.KeyBrush(ModifierKey.Alt, WheelKind.Actions),
            CornerRadius = new CornerRadius(CardRadius),
            Padding = new Thickness(16),
            Cursor = Cursors.Hand,
            Child = content,
        };
        tile.MouseEnter += (_, _) => tile.Opacity = 0.9;
        tile.MouseLeave += (_, _) => tile.Opacity = 1;
        tile.MouseLeftButtonUp += (_, _) => Page = SettingsPage.About;
        return tile;
    }

    private Border NavItem(SettingsPage page)
    {
        var active = page == _page;
        var foreground = active ? Theme.OnInk : Theme.Text;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // Both trimmed to what is drawn (the icon's square, the text's capitals), so they centre on the same line.
        var icon = Icon(PageGlyph(page), 22, foreground);
        icon.Width = 24;
        icon.Margin = new Thickness(0, 0, 13, 0);
        Typo.TrimToEm(icon);
        row.Children.Add(icon);
        var label = Text(PageName(page), 15, FontWeights.SemiBold, foreground, wrap: false);
        label.FontFamily = Theme.UiFont;
        label.VerticalAlignment = VerticalAlignment.Center;
        Typo.TrimToCaps(label);
        row.Children.Add(label);
        var item = new Border
        {
            Background = active ? Theme.Ink : System.Windows.Media.Brushes.Transparent,
            CornerRadius = new CornerRadius(14),
            Height = 48,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Child = row,
        };
        if (!active)
        {
            item.MouseEnter += (_, _) => item.Background = Theme.Hover;
            item.MouseLeave += (_, _) => item.Background = System.Windows.Media.Brushes.Transparent;
        }
        item.MouseLeftButtonUp += (_, _) => Page = page;
        return item;
    }

    private static void PageHeader(Panel page, string title, string subtitle)
    {
        page.Children.Add(Heading(title, 42));
        var s = Text(subtitle, 15.5, FontWeights.Medium, Theme.Muted);
        s.Margin = new Thickness(0, 10, 0, 6);
        page.Children.Add(s);
    }

    // ------------------------------------------------------------------ pages

    private void GeneralPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.General), T("Language, where results go and starting with Windows.", "Язык, сохранение результатов и запуск вместе с Windows."));

        root.Children.Add(SectionHeader(T("Language", "Язык")));
        root.Children.Add(Card(T("App language", "Язык приложения"), null,
            Segmented<Core.Language?>(
                [(T("System", "Как в системе"), null), ("English", Core.Language.English), ("Русский", Core.Language.Russian)],
                _settings.Language,
                value =>
                {
                    _settings.Language = value;
                    Save();
                    // Rebuild the window in the new language.
                    Title = T("Sweetch – settings", "Sweetch — настройки");
                    Rebuild();
                }),
            glyph: Glyphs.Language));

        root.Children.Add(SectionHeader(T("Appearance", "Оформление")));
        root.Children.Add(Card(T("Color theme", "Цветовая тема"), null,
            Segmented<AppAppearance>(
                [(T("System", "Как в системе"), AppAppearance.System), (T("Dark", "Тёмная"), AppAppearance.Dark), (T("Light", "Светлая"), AppAppearance.Light)],
                _settings.Appearance, value =>
                {
                    _settings.Appearance = value;
                    Save();
                    Theme.Override = value switch { AppAppearance.Dark => true, AppAppearance.Light => false, _ => null };
                    Theme.Refresh();
                    Background = Theme.Page;
                    Rebuild();
                }), glyph: Glyphs.Palette));
        root.Children.Add(Card(T("Colour scheme", "Цветовая схема"),
            T("Low contrast swaps the bright colours for quiet tints.", "Низкий контраст заменяет яркие цвета спокойными оттенками."),
            Segmented(
                [(T("Bright", "Яркая"), false), (T("Low contrast", "Низкий контраст"), true)],
                _settings.LowContrast, value =>
                {
                    _settings.LowContrast = value;
                    Save();
                    Theme.LowContrast = value;
                    Rebuild();
                }), glyph: Glyphs.Contrast));

        root.Children.Add(SectionHeader(T("Results", "Результаты")));
        var folderLine = Text(_settings.OutputDirectory ?? "", 12, brush: Theme.Muted, wrap: false);
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        folderRow.Children.Add(Button(T("Choose folder…", "Выбрать папку…"), PickFolder, glyph: Glyphs.Folder));
        folderLine.Margin = new Thickness(12, 0, 0, 0);
        folderLine.VerticalAlignment = VerticalAlignment.Center;
        folderRow.Children.Add(folderLine);
        var toFolder = !string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        var below = new StackPanel();
        below.Children.Add(Wrap(Segmented(
            [(T("Next to the original", "Рядом с исходником"), false), (T("To a folder", "В папку"), true)],
            toFolder,
            value =>
            {
                if (value)
                {
                    PickFolder();
                }
                else
                {
                    _settings.OutputDirectory = null;
                    Save();
                    Rebuild();
                }
            })));
        if (toFolder)
            below.Children.Add(folderRow);
        root.Children.Add(Card(T("Save results", "Сохранять результаты"), T("The original file is never changed.", "Исходный файл никогда не изменяется."), null, below, Glyphs.Folder));
        root.Children.Add(Card(T("Show in folder when done", "Показать в папке по завершении"), null,
            Toggle(_settings.RevealResult, v =>
            {
                _settings.RevealResult = v;
                Save();
            }), glyph: Glyphs.FolderOpen));
        var apps = new TextBox { Text = string.Join(", ", _settings.DragApps), TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Center };
        apps.LostFocus += (_, _) =>
        {
            _settings.DragApps = [.. apps.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            Save();
        };
        var appsBelow = new StackPanel();
        appsBelow.Children.Add(apps);
        root.Children.Add(Card(T("Open the wheel when dragging from", "Открывать колесо при перетаскивании из"),
            T("Program names (as in Task Manager → Details, without .exe), comma-separated. Empty: from anywhere.", "Имена программ (как в Диспетчере задач → «Подробности», без .exe) через запятую. Пусто — откуда угодно."),
            null, appsBelow, Glyphs.FolderOpen));

        root.Children.Add(SectionHeader(T("System", "Система")));
        root.Children.Add(Card(T("Start when I sign in", "Запускать при входе в систему"), null,
            Toggle(ShellIntegration.IsAutostartEnabled, v => Apply(() => ShellIntegration.SetAutostart(v))), glyph: Glyphs.Power));
        root.Children.Add(Card(T("File Explorer context menu", "Контекстное меню Проводника"), T("A “Sweetch…” item on right-click", "Пункт «Sweetch…» при нажатии правой кнопкой"),
            Toggle(ShellIntegration.IsContextMenuEnabled, v => Apply(() => ShellIntegration.SetContextMenu(v))), glyph: Glyphs.OpenLink));
        root.Children.Add(Card(T("Check for updates", "Проверять обновления"), T("Asks GitHub for a new release every few hours and offers to install it", "Раз в несколько часов смотрит новый релиз на GitHub и предлагает установить"),
            Toggle(_settings.AutoUpdateCheck, v =>
            {
                _settings.AutoUpdateCheck = v;
                Save();
            }), glyph: Glyphs.Update));
    }

    private void GesturesPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Gestures), T("Which key opens which wheel, and a shortcut without the mouse.", "Клавиши открытия меню и сочетание для работы без мыши."));

        // One tile per wheel, in the colour of its key.
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-6, 12, -6, -6) };
        foreach (var (wheel, description) in new[]
                 {
                     (WheelKind.Convert, T("Convert to another format", "Конвертация в другой формат")),
                     (WheelKind.Tools, T("Compress, shrink, trim, merge…", "Сжатие, уменьшение, обрезка, объединение…")),
                     (WheelKind.Actions, T("Copy, move, path, terminal, share…", "Копирование, перемещение, путь, терминал, отправка…")),
                 })
        {
            var key = _settings.KeyFor(wheel);
            var footer = new DockPanel { LastChildFill = false };
            var chooser = KeyChooser(wheel);
            DockPanel.SetDock(chooser, Dock.Left);
            footer.Children.Add(chooser);
            var icon = Icon(WheelGlyph(wheel), 52, Theme.OnKey);
            icon.VerticalAlignment = VerticalAlignment.Bottom;
            Typo.TrimToEm(icon);
            DockPanel.SetDock(icon, Dock.Right);
            footer.Children.Add(icon);
            var tile = Tile(Theme.KeyBrush(key, wheel), AppSettings.WheelName(wheel), description, footer, height: 250);
            tile.Margin = new Thickness(6);
            if (key == ModifierKey.None)
                tile.Opacity = 0.55;
            tiles.Children.Add(tile);
        }
        // Three across when there is room, otherwise stacked, so the key picker and icon never clip.
        tiles.SizeChanged += (_, e) => tiles.Columns = e.NewSize.Width < 660 ? 1 : 3;
        root.Children.Add(tiles);

        root.Children.Add(SectionHeader(T("Gesture", "Жест")));
        root.Children.Add(Card(
            T("Grab a file and tap a key while dragging", "Возьмите файл и нажмите клавишу при перетаскивании"),
            T("A wheel opens; drop the file on a slice. Drop in the middle to cancel. The same key again (or R) repeats the last action.", "Откроется меню — отпустите файл над сектором. Отпустите в центре для отмены. Повторное нажатие той же клавиши (или R) повторит последнее действие."),
            null, glyph: Glyphs.Drag));
        root.Children.Add(Card(T("Pause gestures", "Приостановить жесты"), T("The wheel won’t open while dragging.", "Меню не открывается при перетаскивании."),
            Toggle(_settings.Paused, v =>
            {
                _settings.Paused = v;
                Save();
            }), glyph: Glyphs.Pause));

        root.Children.Add(SectionHeader(T("Keyboard", "Клавиатура")));
        root.Children.Add(Card(T("Shortcut for the File Explorer selection", "Сочетание для выбранных в Проводнике файлов"),
            T("Arrows pick a slice, Enter runs it, Tab switches wheels, Esc closes.", "Стрелки — выбор, Enter — запуск, Tab — смена меню, Esc — закрыть."),
            null,
            below: Wrap(Segmented(
                Enum.GetValues<HotkeyPreset>().Where(p => p != HotkeyPreset.None).Append(HotkeyPreset.None)
                    .Select(p => (AppSettings.HotkeyName(p), p)).ToList(),
                _settings.Hotkey,
                value =>
                {
                    _settings.Hotkey = value;
                    Save();
                })),
            glyph: Glyphs.Keyboard));
    }

    private static string WheelGlyph(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => Glyphs.Convert,
        WheelKind.Tools => Glyphs.Tools,
        _ => Glyphs.Actions,
    };

    private void QualityPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Quality), T("The balance between quality and file size.", "Баланс между качеством и размером файла."));
        root.Children.Add(SectionHeader(T("Images", "Изображения")));
        root.Children.Add(Card(T("JPG, WebP and AVIF", "JPG, WebP и AVIF"), T("Quality when converting images", "Качество конвертации изображений"),
            Slider(50, 100, _settings.Quality.JpegQuality, v => _settings.Quality.JpegQuality = v, Save), glyph: Glyphs.Photo));
        root.Children.Add(Card(T("Compress", "Сжать"), T("Quality of the Compress tool", "Качество инструмента «Сжать»"),
            Slider(30, 95, _settings.Quality.CompressQuality, v => _settings.Quality.CompressQuality = v, Save), glyph: Glyphs.Shrink));
        root.Children.Add(SectionHeader("PDF"));
        root.Children.Add(Card(T("PDF to images", "PDF в изображения"), T("Resolution in DPI", "Разрешение в DPI"),
            Slider(72, 600, _settings.Quality.PdfDpi, v => _settings.Quality.PdfDpi = v, Save), glyph: Glyphs.Page));
    }

    private void HelpersPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Helpers), T("Programs Sweetch uses for audio, video and documents.", "Программы для обработки аудио, видео и документов в Sweetch."));
        root.Children.Add(SectionHeader(T("Built in", "Встроено")));
        root.Children.Add(Card(T("Built in", "Встроено"),
            T("Images, PDFs, spreadsheets, subtitles and archives. Documents need Pandoc and audio and video need FFmpeg: both download the first time they're needed. Some Office conversions need LibreOffice.", "Изображения, PDF, таблицы, субтитры и архивы. Для документов нужен Pandoc, для аудио и видео — FFmpeg: оба скачиваются при первой необходимости. Для части операций Office — LibreOffice."),
            Button(T("Check again", "Проверить снова"), () =>
            {
                _tools.Refresh();
                Rebuild();
            }, glyph: Glyphs.Refresh),
            glyph: Glyphs.Extension));
        root.Children.Add(SectionHeader(T("Helper programs", "Вспомогательные программы")));

        // Which program a format needs: the cards below narrow down while typing.
        var search = TextField(_toolQuery, 40);
        search.Background = System.Windows.Media.Brushes.Transparent; // the rounded border behind it gives the colour
        var hint = Text(T("Find a format, e.g. mp4 or .docx", "Найти формат, например mp4 или .docx"), 14.5, brush: Theme.Muted);
        hint.Margin = new Thickness(14, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        var box = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        box.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Theme.Track, Child = search, ClipToBounds = true });
        box.Children.Add(hint);
        root.Children.Add(box);
        var list = new StackPanel();
        root.Children.Add(list);
        void Show()
        {
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            list.Children.Clear();
            var query = search.Text.Trim();
            var format = query.Length == 0 ? null : ToolCoverage.Normalize(query);
            var needed = format is null ? null : ToolCoverage.For(Coverage, format);
            if (format is not null)
            {
                var known = Formats.All.Contains(format);
                var answer = Text(needed!.Count > 0
                    ? T($"{Formats.Display(format)} works with: {string.Join(", ", needed.Select(ToolLocator.DisplayName))}", $"С {Formats.Display(format)} работают: {string.Join(", ", needed.Select(ToolLocator.DisplayName))}")
                    : known ? T($"{Formats.Display(format)} works without helper programs.", $"{Formats.Display(format)} работает без доп. программ.")
                    : T($"Sweetch doesn't know {Formats.Display(format)} yet.", $"Sweetch пока не знает формат {Formats.Display(format)}."), 14.5, FontWeights.SemiBold);
                answer.Margin = new Thickness(4, 0, 0, 12);
                list.Children.Add(answer);
            }
            foreach (var tool in Enum.GetValues<ExternalTool>())
            {
                if (needed is null || needed.Contains(tool))
                    list.Children.Add(ToolCard(tool, format));
            }
        }
        search.TextChanged += (_, _) =>
        {
            _toolQuery = search.Text;
            Show();
        };
        Show();
    }

    private string _toolQuery = "";
    private readonly HashSet<ExternalTool> _expandedTools = [];

    /// <summary>For the self-test screenshots: a search typed in and one program's list opened.</summary>
    internal void PreviewTools(string query, ExternalTool expand)
    {
        _toolQuery = query;
        _expandedTools.Add(expand);
        Rebuild();
    }

    // Worked out once per language (labels are translated): every operation on every format, with each program present and absent.
    private static readonly Dictionary<Language, IReadOnlyDictionary<ExternalTool, IReadOnlyList<ToolCoverage.Group>>> CoverageByLanguage = [];

    private static IReadOnlyDictionary<ExternalTool, IReadOnlyList<ToolCoverage.Group>> Coverage =>
        CoverageByLanguage.TryGetValue(Loc.Current, out var known) ? known
            : CoverageByLanguage[Loc.Current] = ToolCoverage.Build(Sweetch.Core.Operations.Catalog.Build());

    /// <summary>"Formats and features": per group of formats, what the program converts them to and which tools it adds.</summary>
    private FrameworkElement ToolDetails(ExternalTool tool, string? format)
    {
        var expanded = format is not null || _expandedTools.Contains(tool);
        var panel = new StackPanel { Margin = new Thickness(44, 12, 0, 0) };
        var toggle = new TextBlock
        {
            Text = (expanded ? "▴ " : "▾ ") + T("Formats and features", "Форматы и функции"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Muted,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        toggle.MouseLeftButtonUp += (_, _) =>
        {
            if (!_expandedTools.Remove(tool))
                _expandedTools.Add(tool);
            Rebuild();
        };
        if (format is null)
            panel.Children.Add(toggle);
        if (!expanded)
            return panel;
        foreach (var group in Coverage[tool].Where(g => format is null || g.Formats.Contains(format)))
        {
            var formats = Text(string.Join(", ", group.Formats.Select(Formats.Display)), 14, FontWeights.Bold);
            formats.Margin = new Thickness(0, 10, 0, 2);
            formats.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(formats);
            void Line(string label, IReadOnlyList<string> items)
            {
                if (items.Count == 0)
                    return;
                var line = Text($"{label}: {string.Join(", ", items)}", 13.5, brush: Theme.Muted);
                line.TextWrapping = TextWrapping.Wrap;
                panel.Children.Add(line);
            }
            Line(T("Convert to", "Конвертация в"), group.Converts);
            Line(T("Tools", "Инструменты"), group.Tools);
        }
        return panel;
    }

    // ------------------------------------------------------------------ helpers

    private static FrameworkElement Wrap(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.Margin = new Thickness(0, 12, 0, 0);
        return element;
    }

    private Border KeyChooser(WheelKind wheel) => Dropdown(
        [("Shift", ModifierKey.Shift), ("Ctrl", ModifierKey.Ctrl), ("Alt", ModifierKey.Alt), (T("Off", "Выкл."), ModifierKey.None)],
        _settings.KeyFor(wheel),
        key =>
        {
            var old = _settings.KeyFor(wheel);
            // Keep keys unique: the wheel that had this key gets the old one.
            foreach (var other in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
            {
                if (other != wheel && key != ModifierKey.None && _settings.KeyFor(other) == key)
                    SetKey(other, old);
            }
            SetKey(wheel, key);
            Save();
            Rebuild();
        });

    private void SetKey(WheelKind wheel, ModifierKey key)
    {
        switch (wheel)
        {
            case WheelKind.Convert:
                _settings.ConvertKey = key;
                break;
            case WheelKind.Tools:
                _settings.ToolsKey = key;
                break;
            default:
                _settings.ActionsKey = key;
                break;
        }
    }

    private void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("Output folder", "Папка результатов") };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.OutputDirectory = dialog.FolderName;
            Save();
        }
        Rebuild();
    }

    private void Apply(Action action)
    {
        try
        {
            action();
            Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Sweetch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Border ToolCard(ExternalTool tool, string? format = null)
    {
        var path = _tools.Find(tool);
        var description = path ?? tool switch
        {
            ExternalTool.Ffmpeg => T("Audio and video", "Аудио и видео"),
            ExternalTool.Pandoc => T("Documents (DOCX, Markdown, EPUB…)", "Документы (DOCX, Markdown, EPUB…)"),
            _ => T("DOC, PPTX, ODS and conversion to PDF", "DOC, PPTX, ODS и конвертация в PDF"),
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Badge(path is null ? T("missing", "отсутствует") : T("found", "найдено"), path is not null));
        if (path is null)
        {
            var id = tool switch
            {
                ExternalTool.Ffmpeg => "Gyan.FFmpeg",
                ExternalTool.Pandoc => "JohnMacFarlane.Pandoc",
                _ => "TheDocumentFoundation.LibreOffice",
            };
            var install = Shell.ToolInstaller.CanInstall(tool) && DownloadTool is not null
                ? Button(T($"Download ({Shell.ToolInstaller.DownloadSize(tool)})", $"Скачать ({Shell.ToolInstaller.DownloadSize(tool)})"), async () =>
                {
                    if (await DownloadTool(tool))
                        Rebuild();
                }, primary: true, glyph: Glyphs.Download)
                : Button(T("Install", "Установить"), () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe",
                        $"/c winget install -e --id {id} --accept-source-agreements --accept-package-agreements & echo. & echo {T("Done. Click Check again in Sweetch.", "Готово. Нажмите «Проверить снова» в Sweetch.")} & pause")
                    { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Sweetch", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }, primary: true, glyph: Glyphs.Download);
            install.Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(install);
        }
        var glyph = tool switch
        {
            ExternalTool.Ffmpeg => Glyphs.Video,
            ExternalTool.Pandoc => Glyphs.Document,
            _ => Glyphs.Table,
        };
        return Card(ToolLocator.DisplayName(tool), description, right, ToolDetails(tool, format), glyph: glyph);
    }
}
