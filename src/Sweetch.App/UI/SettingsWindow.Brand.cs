using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sweetch.Core;
using static Sweetch.App.UI.Controls;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>
/// The About page: a grid of colour blocks with the wordmark, a wide block for this fork's support and smaller
/// ones for the original author, feedback and updates.
/// </summary>
internal sealed partial class SettingsWindow
{
    private void AboutPage(Panel root)
    {
        var grid = new Grid { Margin = new Thickness(-6, 0, -6, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(grid, Wordmark(), 0, 0, span: 2);
        Place(grid, SupportTile(
            T("Support the fork", "Поддержать форк"),
            T($"{Brand.ForkAuthor} rebuilt Sweetch: the new design, an instant wheel, video on the graphics card, a smaller download. If it saves you time, support further work.",
              $"{Brand.ForkAuthor} переделал Sweetch: новый дизайн, мгновенное колесо, видео на видеокарте, лёгкая загрузка. Если Sweetch экономит вам время, поддержите дальнейшую работу."),
            Theme.KeyBrush(Input.ModifierKey.Alt, WheelKind.Actions), Brand.ForkTrc20, Brand.ForkDonatePay), 1, 0, span: 2);
        Place(grid, SupportTile(
            T("Support the author", "Поддержать автора"),
            T($"{Brand.Author} created Sweetch.", $"{Brand.Author} — создатель Sweetch."),
            Theme.Pink, Brand.Trc20, Brand.DonatePay), 2, 0);

        var telegram = Button("Telegram", () => Shell.Updater.Open(Brand.Channel), primary: true, glyph: Glyphs.Send, onTile: true);
        Place(grid, Tile(Theme.Mint, T("Ideas and questions", "Идеи и вопросы"),
            T("News and feedback on the project's Telegram channel.", "Новости и обратная связь в Telegram-канале проекта."), telegram, height: 200), 2, 1);
        Place(grid, UpdatesTile(), 3, 0, span: 2);
        root.Children.Add(grid);

        var footer = Text(T("Local processing. Your files stay on your computer.", "Локальная обработка. Ваши файлы остаются на компьютере."), 13, FontWeights.Medium, Theme.Muted);
        footer.Margin = new Thickness(4, 20, 0, 0);
        root.Children.Add(footer);
    }

    /// <summary>What the updater knows right now, and the one button that fits: update, or check again.</summary>
    private Border UpdatesTile()
    {
        var updater = Updater;
        var text = updater switch
        {
            { Installing: true } => T($"Downloading version {updater.Available?.Version}… {updater.Progress:P0}", $"Скачиваю версию {updater.Available?.Version}… {updater.Progress:P0}"),
            { Available: { } u } => T($"Version {u.Version} is out. Sweetch will install it and start again.", $"Вышла версия {u.Version}. Sweetch установит её и перезапустится."),
            { LastError: { } e } => T($"Couldn't check: {e}", $"Не удалось проверить: {e}"),
            { LastChecked: { } at } => T($"You have the latest version. Checked at {at:HH:mm}.", $"У вас последняя версия. Проверено в {at:HH:mm}."),
            _ => T("Sweetch looks for new versions on GitHub by itself.", "Sweetch сам проверяет новые версии на GitHub."),
        };
        FrameworkElement? button = updater switch
        {
            null or { Installing: true } => null,
            { Available: { } u } => Button(T($"Update to {u.Version}", $"Обновить до {u.Version}"), () => _ = updater.InstallAsync(u), primary: true, glyph: Glyphs.Download, onTile: true),
            _ => Button(T("Check now", "Проверить"), () => _ = updater.CheckAsync(), glyph: Glyphs.Refresh, onTile: true),
        };
        return Tile(Theme.Sky, T("Updates", "Обновления"), text, button ?? TileIcon(Glyphs.Update), height: 140);
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column, int span = 1)
    {
        element.Margin = new Thickness(6);
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        grid.Children.Add(element);
    }

    private static TextBlock TileIcon(string glyph)
    {
        var icon = Icon(glyph, 40, Theme.OnKey);
        Typo.TrimToEm(icon);
        return icon;
    }

    /// <summary>The orange block with the big wordmark.</summary>
    private static Border Wordmark()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new DockPanel { LastChildFill = true };
        var logo = Logo(56);
        logo.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(logo, Dock.Right);
        top.Children.Add(logo);
        var heading = new StackPanel();
        heading.Children.Add(Heading(T("A new shape for your files", "Новая форма ваших файлов"), 20, Theme.OnKey));
        var line = Text(T("Local processing. Your files stay on your computer.", "Локальная обработка. Файлы остаются на компьютере."), 13.5, FontWeights.Medium, Theme.OnKey);
        line.Opacity = 0.72;
        line.Margin = new Thickness(0, 4, 0, 0);
        heading.Children.Add(line);
        top.Children.Add(heading);
        grid.Children.Add(top);

        var bottom = new DockPanel { LastChildFill = true };
        var version = Heading(Shell.Updater.Current.ToString(2), 30, Theme.OnKey);
        Typo.TrimToCaps(version);
        version.VerticalAlignment = VerticalAlignment.Bottom;
        version.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(version, Dock.Right);
        bottom.Children.Add(version);
        var name = Heading("Sweetch", 104, Theme.OnKey);
        name.TextWrapping = TextWrapping.NoWrap;
        Typo.TrimToCaps(name);
        // The wordmark shrinks in a narrow window instead of being cut off.
        bottom.Children.Add(new Viewbox { Child = name, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);

        return new Border
        {
            Background = Theme.KeyBrush(Input.ModifierKey.Shift, WheelKind.Convert),
            CornerRadius = new CornerRadius(TileRadius),
            Padding = new Thickness(24),
            Height = 240,
            Child = grid,
        };
    }

    /// <summary>A support block: who, a USDT TRC20 address to copy and a DonatePay button.</summary>
    private static Border SupportTile(string title, string text, Brush background, string address, string donatePay)
    {
        var body = new StackPanel();
        var network = Text("USDT · TRC20", 12.5, FontWeights.Bold, Theme.OnKey);
        network.Opacity = 0.6;
        network.Margin = new Thickness(0, 0, 0, 6);
        body.Children.Add(network);
        var field = new TextBox
        {
            Text = address,
            IsReadOnly = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12.5,
            Background = Brushes.Transparent,
            Foreground = Theme.OnKey,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.NoWrap,
        };
        var copied = Text("", 12.5, FontWeights.SemiBold, Theme.OnKey);
        copied.Margin = new Thickness(2, 8, 0, 0);
        var copy = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = Theme.TileStrong,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = T("Copy address", "Копировать адрес"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Glyphs.Copy, FontFamily = Theme.Icons, FontSize = 18, Foreground = Theme.TileStrongText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        copy.MouseEnter += (_, _) => copy.Opacity = 0.85;
        copy.MouseLeave += (_, _) => copy.Opacity = 1;
        copy.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Clipboard.SetText(address);
                copied.Text = T("Address copied", "Адрес скопирован");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                field.Focus();
                field.SelectAll();
            }
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(copy, Dock.Right);
        row.Children.Add(copy);
        field.VerticalAlignment = VerticalAlignment.Center;
        field.Margin = new Thickness(4, 0, 8, 0);
        // The address shrinks rather than being cut off in a narrow window.
        row.Children.Add(new Viewbox { Child = field, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        body.Children.Add(new Border
        {
            Background = Theme.TileSoft,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8),
            Child = row,
        });
        body.Children.Add(copied);
        var donate = Button("DonatePay", () => Shell.Updater.Open(donatePay), primary: true, glyph: Glyphs.Heart, onTile: true);
        donate.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(donate);
        body.Margin = new Thickness(0, 22, 0, 0);
        return Tile(background, title, text, body);
    }
}
