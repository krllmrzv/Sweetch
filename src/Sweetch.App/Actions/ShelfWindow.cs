using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sweetch.App.Native;
using Sweetch.App.Shell;
using Sweetch.App.UI;
using Sweetch.Core;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Actions;

/// <summary>
/// "Float": a small always-on-top shelf that holds files so you can drag them somewhere else later,
/// e.g. after switching windows.
/// </summary>
internal sealed class ShelfWindow : Window
{
    private readonly ObservableCollection<string> _paths = [];
    private readonly ListBox _list;
    private readonly TextBlock _header;
    private Point? _dragStart;

    private ShelfWindow()
    {
        Theme.Refresh();
        Title = T("Floating shelf", "Плавающая полка");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowDrop = true;
        Width = 290;
        SizeToContent = SizeToContent.Height;
        FontFamily = Theme.UiFont;

        _header = new TextBlock { Foreground = Theme.Text, FontFamily = Theme.DisplayFont, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var close = IconButton(Glyphs.Close, T("Close", "Закрыть"));
        close.Click += (_, _) => Close();
        var header = new DockPanel { Margin = new Thickness(14, 10, 8, 8), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = Glyphs.Pin, FontFamily = Theme.Icons, Foreground = Theme.Ink, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_header);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        _list = new ListBox
        {
            ItemsSource = _paths,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Text,
            MaxHeight = 360,
            SelectionMode = SelectionMode.Extended,
            ItemTemplate = BuildTemplate(),
            ItemContainerStyle = ItemStyle(),
            Margin = new Thickness(6, 0, 6, 4),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        _list.PreviewMouseMove += OnListMouseMove;
        _list.MouseDoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is string path)
                Open(path);
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete)
            {
                foreach (var p in _list.SelectedItems.Cast<string>().ToList())
                    _paths.Remove(p);
                UpdateHeader();
            }
        };

        var hint = new TextBlock
        {
            Text = T("Drag items out, or drop more here.", "Перетащите файлы отсюда или добавьте новые сюда."),
            Foreground = Theme.Muted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 12, 10),
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_list);
        stack.Children.Add(hint);
        Content = new Border
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = stack,
            Margin = new Thickness(14),
            Effect = Theme.Shadow(24),
        };

        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                Add(dropped);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
    }

    public static void Open(IReadOnlyList<FileItem> items)
    {
        var shelf = new ShelfWindow();
        shelf.Add(items.Select(i => i.Path));
        Win32.GetCursorPos(out var cursor);
        shelf.Show();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(shelf).Handle;
        Win32.SetWindowPos(hwnd, new IntPtr(-1), cursor.X + 24, cursor.Y - 40, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void Add(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!_paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                _paths.Add(p);
        }
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var n = _paths.Count;
        _header.Text = n == 0 ? T("Empty shelf", "Пустая полка") : Items(n);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is null)
            return;
        var delta = e.GetPosition(this) - _dragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;

        var selected = _list.SelectedItems.Cast<string>().ToList();
        if (selected.Count == 0 && _list.InputHitTest(e.GetPosition(_list)) is FrameworkElement { DataContext: string hovered })
            selected.Add(hovered);
        selected = selected.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (selected.Count == 0)
            return;

        var data = new DataObject(DataFormats.FileDrop, selected.ToArray());
        var effect = DragDrop.DoDragDrop(_list, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        if (effect == DragDropEffects.Move)
        {
            foreach (var p in selected.Where(p => !File.Exists(p) && !Directory.Exists(p)))
                _paths.Remove(p);
            UpdateHeader();
            if (_paths.Count == 0)
                Close();
        }
    }

    private static void Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>Rounded rows that highlight on hover and selection instead of the classic blue bar.</summary>
    private static Style ItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Row");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Hover, "Row"));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Selected, "Row"));
        template.Triggers.Add(hover);
        template.Triggers.Add(selected);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 1, 0, 1)));
        return style;
    }

    private static DataTemplate BuildTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(MarginProperty, new Thickness(2, 3, 2, 3));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(WidthProperty, 24.0);
        image.SetValue(HeightProperty, 24.0);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding { Converter = new IconConverter() });
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new NameConverter() });
        text.SetValue(TextBlock.ForegroundProperty, Theme.Text);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(image);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private static Button IconButton(string glyph, string tip) => new()
    {
        Content = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 11, Foreground = Theme.Muted },
        ToolTip = tip,
        Width = 26,
        Height = 26,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Cursor = Cursors.Hand,
    };

    private sealed class IconConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is string path ? FileIcons.Get(path) : null;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class NameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is string path ? Path.GetFileName(path.TrimEnd('\\', '/')) : "";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
