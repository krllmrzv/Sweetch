using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sweetch.Core;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>A dialog that collects an operation's parameter values.</summary>
internal interface IParameterWindow
{
    IReadOnlyDictionary<string, string> Values { get; }
}

/// <summary>Asks for the values an operation needs (e.g. trim start and end).</summary>
internal sealed class ParameterDialog : Window, IParameterWindow
{
    /// <summary>The richer window for operations that have one (player for trim), else this plain form.</summary>
    public static Window For(Operation op, IReadOnlyList<FileItem> items, ToolLocator tools) => op.Id switch
    {
        "video.trim" or "audio.trim" or "video.thumbnail" => new PlayerWindow(op, items, tools),
        "video.relight" or "image.relight" => new RelightWindow(op, items, tools),
        _ => new ParameterDialog(op, items),
    };

    private readonly Dictionary<string, TextBox> _boxes = new();
    private readonly Dictionary<string, string> _picked = new();

    public ParameterDialog(Operation op, IReadOnlyList<FileItem> items)
    {
        Title = op.Label;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        MinWidth = 360;
        Theme.Refresh();
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        stack.Children.Add(new TextBlock
        {
            Text = items.Count == 1 ? items[0].Name : Items(items.Count),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        foreach (var p in op.Parameters)
        {
            stack.Children.Add(new TextBlock { Text = p.Label, Margin = new Thickness(0, 6, 0, 4) });
            if (p.Choices is { } choices)
            {
                var key = p.Key;
                _picked[key] = p.DefaultValue;
                var segmented = Controls.Segmented(choices.Select(c => (c, c)).ToList(), p.DefaultValue, v => _picked[key] = v);
                if (p.OwnValue)
                {
                    var own = Controls.TextField("", 20);
                    own.Width = 90;
                    own.ToolTip = T("Your own value", "Своё значение");
                    // A filled box wins; the pills fade to show they no longer apply.
                    own.TextChanged += (_, _) => segmented.Opacity = string.IsNullOrWhiteSpace(own.Text) ? 1 : 0.4;
                    _boxes[key] = own;
                    var or = Controls.Text(T("or", "или"), 13, brush: Theme.Muted, wrap: false);
                    or.Margin = new Thickness(10, 0, 10, 0);
                    or.VerticalAlignment = VerticalAlignment.Center;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(segmented);
                    row.Children.Add(or);
                    row.Children.Add(own);
                    stack.Children.Add(row);
                }
                else
                {
                    stack.Children.Add(segmented);
                }
            }
            else
            {
                var box = new TextBox
                {
                    Text = p.DefaultValue,
                    MinWidth = 300,
                    Padding = new Thickness(8, 6, 8, 6),
                    FontSize = 14,
                    Background = Theme.Card,
                    Foreground = Theme.Text,
                    CaretBrush = Theme.Text,
                    BorderBrush = Theme.PanelBorder,
                };
                _boxes[p.Key] = box;
                stack.Children.Add(box);
            }
            if (p.Hint is not null)
                stack.Children.Add(new TextBlock { Text = p.Hint, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left });
        }

        var ok = Controls.Button(T("Run", "Выполнить"), () => DialogResult = true, primary: true);
        ok.Margin = new Thickness(0, 0, 8, 0);
        var cancel = Controls.Button(T("Cancel", "Отмена"), () => DialogResult = false);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                DialogResult = true;
            else if (e.Key == Key.Escape)
                DialogResult = false;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) =>
        {
            ForegroundHelper.Force(this);
            if (_boxes.Values.FirstOrDefault() is { } first)
            {
                first.Focus();
                first.SelectAll();
                Keyboard.Focus(first);
            }
        };
    }

    /// <summary>Typed values, and picked ones unless an own value was typed for them.</summary>
    public IReadOnlyDictionary<string, string> Values
    {
        get
        {
            var values = new Dictionary<string, string>(_picked);
            foreach (var (key, box) in _boxes)
            {
                if (!values.ContainsKey(key) || !string.IsNullOrWhiteSpace(box.Text))
                    values[key] = box.Text;
            }
            return values;
        }
    }
}
