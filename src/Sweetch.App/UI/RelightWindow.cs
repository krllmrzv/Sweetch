using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sweetch.Core;
using Sweetch.Core.Backends;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>
/// Light & color with a live preview. The preview is a small frame run through the same ffmpeg
/// filter chain as the final render, so what you see is what you get.
/// </summary>
internal sealed class RelightWindow : Window, IParameterWindow
{
    private readonly string _path;
    private readonly string _ffmpeg;
    private readonly bool _video;
    private readonly ToolLocator _tools;
    private readonly Dictionary<string, int> _values;
    private readonly StackPanel _knobs = new();
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = Controls.Text("", 12, brush: Theme.Muted, wrap: false);
    private readonly string _temp = OutputPaths.TempDirectory();
    private readonly SemaphoreSlim _gate = new(1);

    private ImageSource? _original;
    private ImageSource? _edited;
    private string? _base;
    private double _at;
    private int _wanted;

    public RelightWindow(Operation op, IReadOnlyList<FileItem> items, ToolLocator tools)
    {
        Title = op.Label;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Theme.Refresh();
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;

        _path = System.IO.Path.GetFullPath(items[0].Path);
        _ffmpeg = tools.Require(ExternalTool.Ffmpeg);
        _tools = tools;
        _video = Formats.Video.Contains(items[0].Format);
        _values = op.Parameters.ToDictionary(p => p.Key, p => int.TryParse(p.DefaultValue, out var v) ? v : 0);

        var left = new StackPanel { Width = 640 };
        left.Children.Add(new TextBlock
        {
            Text = items.Count == 1 ? items[0].Name : Items(items.Count),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        left.Children.Add(new Border { Height = 400, CornerRadius = new CornerRadius(8), Background = Brushes.Black, Child = _preview });
        if (_video)
        {
            // Which frame to preview; the whole video is processed either way.
            var time = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), Focusable = false };
            time.ValueChanged += (_, e) =>
            {
                _at = e.NewValue;
                _base = null;
                Render();
            };
            left.Children.Add(time);
            Loaded += async (_, _) =>
            {
                if ((await FfmpegBackend.ProbeAsync(_ffmpeg, _path)).Duration is > 0 and var d)
                    time.Maximum = d;
            };
        }
        _status.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(_status);

        var right = new StackPanel { Margin = new Thickness(28, 30, 0, 0) };
        right.Children.Add(_knobs);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var reset = Controls.Button(T("Reset", "Сбросить"), () =>
        {
            foreach (var key in _values.Keys.ToList())
                _values[key] = 0;
            BuildKnobs();
            Render();
        });
        reset.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(reset);
        // Hold to see the original, like photo editors.
        var compare = Controls.Button(T("Hold to compare", "Сравнить (удерживать)"), () => { });
        compare.PreviewMouseLeftButtonDown += (_, _) => _preview.Source = _original;
        compare.PreviewMouseLeftButtonUp += (_, _) => _preview.Source = _edited;
        compare.MouseLeave += (_, _) => _preview.Source = _edited;
        actions.Children.Add(compare);
        right.Children.Add(actions);

        var ok = Controls.Button(T("Run", "Выполнить"), () => DialogResult = true, primary: true);
        ok.Margin = new Thickness(0, 0, 8, 0);
        var cancel = Controls.Button(T("Cancel", "Отмена"), () => DialogResult = false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        columns.Children.Add(left);
        columns.Children.Add(right);
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        stack.Children.Add(columns);
        stack.Children.Add(buttons);
        Content = stack;

        BuildKnobs();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                DialogResult = true;
            else if (e.Key == Key.Escape)
                DialogResult = false;
        };
        Loaded += (_, _) =>
        {
            ForegroundHelper.Force(this);
            Render();
        };
        Closed += (_, _) => OutputPaths.TryDelete(_temp);
    }

    public IReadOnlyDictionary<string, string> Values => _values.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(CultureInfo.InvariantCulture));

    // For the self-test.
    internal bool Rendered => _edited is not null && _wanted > 0 && _gate.CurrentCount == 1;

    internal void Set(string key, int value)
    {
        _values[key] = value;
        BuildKnobs();
        Render();
    }

    private void BuildKnobs()
    {
        _knobs.Children.Clear();
        foreach (var knob in Relight.Knobs())
        {
            var label = Controls.Text(knob.Label, 13, wrap: false);
            label.Margin = new Thickness(0, 6, 0, 0);
            _knobs.Children.Add(label);
            _knobs.Children.Add(Controls.Slider(-100, 100, _values.GetValueOrDefault(knob.Key), v =>
            {
                _values[knob.Key] = v;
                Render();
            }, () => { }));
        }
    }

    /// <summary>Re-renders the preview; while one render runs, only the newest request is kept.</summary>
    private async void Render()
    {
        var wanted = ++_wanted;
        await _gate.WaitAsync();
        try
        {
            if (wanted != _wanted)
                return;
            if (_base is null)
            {
                _status.Text = T("Loading…", "Загрузка…");
                var file = System.IO.Path.Combine(_temp, $"base-{wanted}.png");
                if (_video)
                {
                    await ProcessRunner.RunAsync(_ffmpeg, ["-hide_banner", "-nostdin", "-y", "-ss", _at.ToString("0.###", CultureInfo.InvariantCulture),
                        "-i", _path, "-frames:v", "1", "-vf", "scale=-2:'min(800,ih)'", file], default);
                }
                else
                {
                    // Same decoding as the final photo (RAW, orientation), just smaller.
                    await ImageBackend.FilterAsync(_path, file, "png", "null", 100, new OperationContext { Tools = _tools }, maxSide: 1280);
                }
                _base = file;
                _original = Controls.Bitmap(file);
                if (wanted != _wanted)
                    return;
            }
            var output = System.IO.Path.Combine(_temp, $"{wanted}.bmp");
            await ProcessRunner.RunAsync(_ffmpeg, ["-hide_banner", "-nostdin", "-y", "-i", _base, "-vf", Relight.Filter(Values) + ",format=bgr24",
                "-frames:v", "1", "-c:v", "bmp", output], default);
            _edited = Controls.Bitmap(output);
            OutputPaths.TryDelete(output);
            if (wanted == _wanted)
                _preview.Source = _edited;
            _status.Text = "";
        }
        catch (ConversionException ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _gate.Release();
        }
    }
}
