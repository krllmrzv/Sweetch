using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sweetch.App.Native;
using Sweetch.App.UI;
using Sweetch.Core;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Wheel;

internal enum WheelMode
{
    /// <summary>Opened mid-drag: the user drops the file on a slice. Never takes focus.</summary>
    Drag,

    /// <summary>Opened from the context menu or hotkey: click a slice or use the keyboard.</summary>
    Click,
}

/// <summary>
/// The radial menu. One instance per appearance.
/// <para>
/// Slices are solid blocks in the colour of the key that opened the wheel; the slice under the
/// pointer turns ink (light theme) or paper (dark theme). Nothing moves, glows or fades in one by one.
/// </para>
/// </summary>
internal sealed class WheelWindow : Window
{
    public delegate IReadOnlyList<Operation> OperationSource(WheelKind wheel, IReadOnlyList<FileItem> items);

    /// <summary>Radius of the dark (or white) disc behind the slices.</summary>
    private const double Disc = 212;
    private const double OuterRadius = 204;
    private const double InnerRadius = 76;
    private const double HubRadius = 72;
    private const double Gap = 8;
    private const double Corner = 14;

    /// <summary>Room around the disc for its shadow.</summary>
    private const double Pad = 18;
    private const double Size = 2 * (Disc + Pad);
    private static readonly WheelKind[] WheelOrder = [WheelKind.Convert, WheelKind.Tools, WheelKind.Actions];

    private readonly AppSettings _settings;
    private readonly OperationSource _source;
    private readonly Action<Operation, IReadOnlyList<FileItem>> _execute;
    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly List<SolidColorBrush> _fills = [];
    private readonly List<StackPanel> _labels = [];
    private Hub _hub = null!;

    private IReadOnlyList<FileItem> _items;
    private IReadOnlyList<Operation> _ops = [];
    private WheelKind _wheel;

    // Two sides: the usual slices and the rare ones; scrolling flips between them.
    private bool _back;
    private int _otherSide;
    private int _scrolled;
    private DateTime _flippedAt;
    private int _highlight = -1;
    private Win32.POINT _anchor;
    private bool _finished;
    private bool _repeat;
    private DispatcherTimer? _closeTimer;

    public WheelWindow(
        WheelMode mode,
        WheelKind wheel,
        IReadOnlyList<FileItem>? items,
        AppSettings settings,
        OperationSource source,
        Action<Operation, IReadOnlyList<FileItem>> execute)
    {
        Mode = mode;
        _wheel = wheel;
        _items = items ?? [];
        _settings = settings;
        _source = source;
        _execute = execute;

        Title = "Sweetch";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = mode == WheelMode.Click;
        AllowDrop = true;
        Width = Size;
        Height = Size;
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        FontFamily = Theme.UiFont;
        Focusable = true;
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            var flags = Win32.WS_EX_TOOLWINDOW | (mode == WheelMode.Drag ? Win32.WS_EX_NOACTIVATE : 0);
            Win32.AddExStyle(Handle, flags);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionAtAnchor);

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, e) =>
        {
            SetHighlight(-1);
            e.Handled = true;
        };
        Drop += OnDrop;
        MouseMove += (sender, e) =>
        {
            if (Mode == WheelMode.Click)
                SetHighlight(HitTest(e.GetPosition(_canvas), out bool _));
        };
        MouseLeftButtonUp += OnClick;
        MouseWheel += (_, e) =>
        {
            e.Handled = true;
            Scroll(e.Delta);
        };
        KeyDown += OnKeyDown;
        Deactivated += (_, _) =>
        {
            if (Mode == WheelMode.Click)
                Finish();
        };

        LoadOperations();
        Build();
    }

    public WheelMode Mode { get; }

    public WheelKind CurrentWheel => _wheel;

    /// <summary>The last operation if it fits these items (for the "repeat last" view).</summary>
    public Func<IReadOnlyList<FileItem>, Operation?>? LastOperation { get; set; }

    /// <summary>Called once the person has turned a wheel for the first time (the app saves the settings).</summary>
    public Action? Learned { get; set; }

    /// <summary>Runs the last operation again with the values it used last time.</summary>
    public Action<IReadOnlyList<FileItem>>? RunRepeat { get; set; }

    /// <summary>
    /// Creates the window off screen and invisible, so a later <see cref="ShowAt"/> skips the costly first
    /// show (window handle, layered surface, first layout). The controller keeps one such spare ready.
    /// </summary>
    public void Prepare() => Show();

    /// <summary>Points a prepared spare at the wheel and items it is opened for.</summary>
    public void Reset(WheelKind wheel, IReadOnlyList<FileItem>? items)
    {
        _wheel = wheel;
        _items = items ?? [];
        _repeat = false;
        _back = false;
        LoadOperations();
        Build();
    }

    public bool IsFinished => _finished;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    private Color KeyColor => Theme.KeyColor(_settings.KeyFor(_wheel), _wheel);

    private Color KeyStrong => Theme.KeyStrongColor(_settings.KeyFor(_wheel), _wheel);

    public void ShowAt(Win32.POINT screenPoint)
    {
        _anchor = screenPoint;
        if (!IsVisible)
            Show();
        PositionAtAnchor();
        // Appears at once: no fade or zoom, so the wheel is usable the moment the key is pressed.
        Opacity = 1;

        if (Mode == WheelMode.Click)
        {
            ForegroundHelper.Force(this);
            Keyboard.Focus(this);
            if (_ops.Count > 0)
                SetHighlight(0);
        }
    }

    private void PositionAtAnchor()
    {
        if (_finished || !Win32.GetWindowRect(Handle, out var rect))
            return;
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        var work = Win32.WorkAreaAt(_anchor);
        var x = Math.Clamp(_anchor.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        var y = Math.Clamp(_anchor.Y - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h));
        Win32.SetWindowPos(Handle, new IntPtr(-1), x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>True when a physical screen point lies on the wheel.</summary>
    public bool Contains(Win32.POINT p) => Contains(Handle, p);

    /// <summary>The same test from any thread, by window handle (the mouse hook asks it on its own thread).</summary>
    public static bool Contains(IntPtr handle, Win32.POINT p)
    {
        if (handle == IntPtr.Zero || !Win32.GetWindowRect(handle, out var rect))
            return false;
        var scale = (rect.Right - rect.Left) / Size;
        var cx = (rect.Left + rect.Right) / 2.0;
        var cy = (rect.Top + rect.Bottom) / 2.0;
        var radius = Disc * scale;
        return Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2) <= radius * radius;
    }

    public void SwitchTo(WheelKind wheel)
    {
        if (_finished)
            return;
        _wheel = wheel;
        _repeat = false;
        _back = false;
        LoadOperations();
        Build();
        if (Mode == WheelMode.Click && _ops.Count > 0)
            SetHighlight(0);
    }

    /// <summary>
    /// The mouse wheel turned (over the wheel, or anywhere over it while dragging). Touchpads send many
    /// small steps, so it flips once per notch and not more often than four times a second.
    /// </summary>
    public void Scroll(int delta)
    {
        _scrolled += delta;
        if (Math.Abs(_scrolled) < 120 || DateTime.UtcNow - _flippedAt < TimeSpan.FromMilliseconds(250))
            return;
        _scrolled = 0;
        _flippedAt = DateTime.UtcNow;
        Flip();
    }

    /// <summary>Shows the other side: rare slices, or back to the usual ones.</summary>
    public void Flip()
    {
        if (_finished || _repeat || _otherSide == 0)
            return;
        _back = !_back;
        if (!_settings.FlipLearned)
        {
            _settings.FlipLearned = true;
            Learned?.Invoke();
        }
        LoadOperations();
        Build();
        if (Mode == WheelMode.Click && _ops.Count > 0)
            SetHighlight(0);
    }

    /// <summary>Tapping the same key again shows a single slice that repeats the last action.</summary>
    public void ToggleRepeat()
    {
        if (_finished)
            return;
        if (_repeat)
        {
            SwitchTo(_wheel);
            return;
        }
        _repeat = true;
        LoadOperations();
        Build();
        if (Mode == WheelMode.Click && _ops.Count > 0)
            SetHighlight(0);
    }

    /// <summary>The drag ended somewhere: close unless the drop landed on us.</summary>
    public void OnDragEnded()
    {
        if (Mode != WheelMode.Drag || _finished)
            return;
        _closeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Finish(), Dispatcher);
        _closeTimer.Start();
    }

    public void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        _closeTimer?.Stop();
        Close();
    }

    /// <summary>Renders the wheel to a PNG without showing it (used by --self-test); <paramref name="clear"/> leaves the background transparent.</summary>
    public void RenderPreview(string file, int highlight, bool clear = false)
    {
        if (highlight >= 0 && highlight < _ops.Count)
            SetHighlight(highlight);
        Content = null;
        var root = new Grid { Background = clear ? Brushes.Transparent : SelfTest.Desktop(), Width = Size, Height = Size };
        root.Children.Add(_canvas);
        _canvas.HorizontalAlignment = HorizontalAlignment.Center;
        _canvas.VerticalAlignment = VerticalAlignment.Center;
        SelfTest.SaveElement(root, file, clear ? 3 : 1.5);
    }

    // ------------------------------------------------------------------ data

    private void LoadOperations()
    {
        IReadOnlyList<Operation> all = _items.Count == 0 ? []
            : _repeat ? (LastOperation?.Invoke(_items) is { } last ? [last] : [])
            : _source(_wheel, _items);
        var back = _repeat ? [] : all.Where(_settings.IsBackSide).ToList();
        var front = _repeat ? all.ToList() : all.Where(o => !_settings.IsBackSide(o)).ToList();
        // A side with nothing on it is never shown: files whose every slice is rare open on that side.
        _back = back.Count > 0 && (_back || front.Count == 0);
        _ops = _back ? back : front;
        _otherSide = _back ? front.Count : back.Count;
        _highlight = -1;
    }


    // ------------------------------------------------------------------ drag & drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_items.Count == 0)
        {
            var preview = DropContent.Read(e.Data, materialize: false);
            if (preview is null)
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            _items = preview.Items;
            LoadOperations();
            Build();
        }
        _closeTimer?.Stop();
        var index = HitTest(e.GetPosition(_canvas), out _);
        SetHighlight(index);
        e.Effects = index >= 0 ? SafeEffect(e.AllowedEffects) : DragDropEffects.None;
    }

    /// <summary>
    /// Copy keeps the original untouched. Never report Move: the drag source would delete the file.
    /// </summary>
    private static DragDropEffects SafeEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
        : (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link
        : DragDropEffects.None;

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = HitTest(e.GetPosition(_canvas), out _);
        var op = index >= 0 && index < _ops.Count ? _ops[index] : null;
        e.Effects = op is null ? DragDropEffects.None : SafeEffect(e.AllowedEffects);
        // Virtual files and images can only be read while the drop is in progress.
        var content = op is null ? null : DropContent.Read(e.Data, materialize: true);
        Finish();
        if (op is not null && content is { Items.Count: > 0 })
        {
            // Return to the drag source right away; do the work afterwards.
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (content.Prepare is not null)
                        await content.Prepare();
                }
                catch (Exception ex)
                {
                    Shell.Notifications.Show(T("Download failed", "Не удалось скачать"), ex.Message, error: true);
                    return;
                }
                Dispatch(op, content.Items);
            });
        }
    }

    // ------------------------------------------------------------------ mouse & keyboard (click mode)

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (Mode != WheelMode.Click || e.Handled)
            return;
        var index = HitTest(e.GetPosition(_canvas), out var inHub);
        if (index >= 0)
            Run(index);
        else if (inHub)
            Finish();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        switch (key)
        {
            case Key.Escape:
                Finish();
                break;
            case Key.Left or Key.Up:
                Move(-1);
                break;
            case Key.Right or Key.Down:
                Move(1);
                break;
            case Key.Enter or Key.Space:
                if (_highlight >= 0)
                    Run(_highlight);
                break;
            case Key.Tab when _otherSide > 0:
                Flip();
                break;
            case Key.Tab:
                // One page: Tab goes on to the next wheel, as before pages existed.
                var next = (Array.IndexOf(WheelOrder, _wheel) + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 2 : 1)) % WheelOrder.Length;
                SwitchTo(WheelOrder[next]);
                break;
            case Key.LeftShift or Key.RightShift:
                SwitchToKey(Input.ModifierKey.Shift);
                break;
            case Key.LeftCtrl or Key.RightCtrl:
                SwitchToKey(Input.ModifierKey.Ctrl);
                break;
            case Key.LeftAlt or Key.RightAlt:
                SwitchToKey(Input.ModifierKey.Alt);
                break;
            case Key.R:
                ToggleRepeat();
                break;
            case >= Key.D1 and <= Key.D9:
                if (key - Key.D1 < _ops.Count)
                    Run(key - Key.D1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void SwitchToKey(Input.ModifierKey key)
    {
        if (_settings.WheelFor(key) is { } wheel && wheel != _wheel)
            SwitchTo(wheel);
    }

    private void Move(int delta)
    {
        if (_ops.Count == 0)
            return;
        SetHighlight(_highlight < 0 ? 0 : ((_highlight + delta) % _ops.Count + _ops.Count) % _ops.Count);
    }

    private void Run(int index)
    {
        var op = _ops[index];
        var items = _items;
        Finish();
        Dispatch(op, items);
    }

    private void Dispatch(Operation op, IReadOnlyList<FileItem> items)
    {
        if (_repeat && RunRepeat is not null)
            RunRepeat(items);
        else
            _execute(op, items);
    }


    // ------------------------------------------------------------------ geometry

    private static Point Polar(double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(Size / 2 + radius * Math.Sin(rad), Size / 2 - radius * Math.Cos(rad));
    }

    private static double Deg(double radians) => radians * 180 / Math.PI;

    private int HitTest(Point p, out bool inHub)
    {
        var dx = p.X - Size / 2;
        var dy = p.Y - Size / 2;
        var r = Math.Sqrt(dx * dx + dy * dy);
        inHub = r < InnerRadius;
        if (inHub || r > Disc || _ops.Count == 0)
            return -1;
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var step = 360.0 / _ops.Count;
        return (int)(((angle + step / 2) % 360) / step) % _ops.Count;
    }

    /// <summary>
    /// A ring segment between <paramref name="a0"/> and <paramref name="a1"/>, shrunk by the corner radius on
    /// every side. Drawn with a round-joined stroke of twice that radius in the same colour, it grows back
    /// to full size with evenly rounded corners and a constant gap to its neighbours.
    /// </summary>
    private static Geometry Sector(double a0, double a1)
    {
        var d = Gap / 2 + Corner;
        var ro = OuterRadius - Corner;
        var ri = InnerRadius + Corner;
        double Off(double r) => Deg(Math.Asin(d / r));
        var outerStart = a0 + Off(ro);
        var outerEnd = a1 - Off(ro);
        var innerStart = a0 + Off(ri);
        var innerEnd = a1 - Off(ri);
        var figure = new PathFigure { StartPoint = Polar(ro, outerStart), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(ro, outerEnd), new System.Windows.Size(ro, ro), 0, outerEnd - outerStart > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(ri, innerEnd), true));
        figure.Segments.Add(new ArcSegment(Polar(ri, innerStart), new System.Windows.Size(ri, ri), 0, innerEnd - innerStart > 180, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A whole ring (one slice), drawn as a thick circle.</summary>
    private static Geometry Ring()
    {
        var r = (OuterRadius + InnerRadius) / 2;
        var ring = new EllipseGeometry(new Point(Size / 2, Size / 2), r, r);
        ring.Freeze();
        return ring;
    }

    /// <summary>
    /// Where a label goes: the centre of mass of its slice, which is where the eye puts the middle of a
    /// wedge, but never inside the middle of the ring (for 2–5 slices the centroid drifts towards the hub).
    /// </summary>
    private static double LabelRadius(int n)
    {
        var mid = (OuterRadius + InnerRadius) / 2;
        if (n <= 1)
            return mid;
        const double ro = OuterRadius;
        const double ri = InnerRadius;
        var half = Math.PI / n;
        var centroid = 2.0 / 3 * (ro * ro * ro - ri * ri * ri) / (ro * ro - ri * ri) * Math.Sin(half) / half;
        return Math.Max(mid, centroid);
    }

    /// <summary>Usable width across a slice at a given radius, leaving a margin inside the gap.</summary>
    private static double Chord(int n, double radius) =>
        n <= 2 ? 2 * radius : 2 * radius * Math.Sin(Math.PI / n) - Gap - 14;

    /// <summary>Every label in a wheel wraps at the same width, so twins on the left and right wrap the same way.</summary>
    private static double LabelWidth(int n, double radius) =>
        n <= 2 ? 132 : Math.Clamp(Chord(n, radius), 60, 132);

    /// <summary>The more slices, the smaller the icon; the text stays large enough to read.</summary>
    private static (double Icon, double Text, double Format) LabelSizes(int n) => n switch
    {
        <= 6 => (30, 14.5, 26),
        <= 8 => (26, 14, 24),
        <= 10 => (22, 14, 22),
        _ => (19, 13.5, 20),
    };

    /// <summary>
    /// True when an upright box of this size, centred at <paramref name="radius"/> on the slice's middle
    /// angle, lies inside the slice with a margin: points around its outline must clear the hub, the rim
    /// and both side gaps.
    /// </summary>
    private static bool FitsInSlice(int n, double angle, double radius, double width, double height)
    {
        const double margin = 7;
        var mid = angle * Math.PI / 180;
        var cx = radius * Math.Sin(mid);
        var cy = -radius * Math.Cos(mid);
        var half = Math.PI / n;
        var (cos0, sin0) = (Math.Cos(mid - half), Math.Sin(mid - half));
        var (cos1, sin1) = (Math.Cos(mid + half), Math.Sin(mid + half));
        for (var i = 0; i <= 4; i++)
        {
            var t = i / 4.0;
            foreach (var (fx, fy) in new[] { (t, 0.0), (t, 1.0), (0.0, t), (1.0, t) })
            {
                var x = cx + (fx - 0.5) * width;
                var y = cy + (fy - 0.5) * height;
                var r = Math.Sqrt(x * x + y * y);
                if (r < InnerRadius + margin + 2 || r > OuterRadius - margin)
                    return false;
                // Distance from each side of the wedge, measured towards the inside.
                if (n > 1 && (x * cos0 + y * sin0 < Gap / 2 + margin || -(x * cos1 + y * sin1) < Gap / 2 + margin))
                    return false;
            }
        }
        return true;
    }

    /// <summary>The largest scale (up to 1) at which a label still fits its slice.</summary>
    private static double FitScale(int n, double angle, double radius, System.Windows.Size size)
    {
        if (FitsInSlice(n, angle, radius, size.Width, size.Height))
            return 1;
        double low = 0.3, high = 1;
        for (var i = 0; i < 12; i++)
        {
            var s = (low + high) / 2;
            if (FitsInSlice(n, angle, radius, size.Width * s, size.Height * s))
                low = s;
            else
                high = s;
        }
        return low;
    }

    // ------------------------------------------------------------------ visuals

    private void Build()
    {
        _canvas.Children.Clear();
        _fills.Clear();
        _labels.Clear();
        Draw(_canvas, _ops, KeyColor, KeyStrong, null, _fills, _labels, _back);

        _hub = new Hub(Mode == WheelMode.Click ? SwitchTo : null, Mode == WheelMode.Click ? Flip : null);
        _hub.Place(_canvas);
        UpdateHub();
    }

    /// <summary>Disc, slices and the dark hub circle; shared by the live wheel and the settings preview.</summary>
    private static void Draw(Canvas canvas, IReadOnlyList<Operation> ops, Color key, Color strong, ISet<string>? highlight, List<SolidColorBrush>? fills, List<StackPanel>? labels, bool back = false)
    {
        var sliceColor = back ? Theme.BackSlice(key) : key;
        var textColor = back ? Theme.BackSliceText(key) : Theme.OnKey.Color;
        // The shadow sits in a cached parent, so its blur isn't redone each time a slice lights up; the disc
        // itself is drawn again on top, sharp at any DPI.
        var shadow = new Border
        {
            CacheMode = new BitmapCache(),
            Child = new Ellipse { Width = 2 * Disc, Height = 2 * Disc, Fill = Theme.Backdrop, Effect = Theme.Shadow(30) },
        };
        var backdrop = new Ellipse { Width = 2 * Disc, Height = 2 * Disc, Fill = Theme.Backdrop };
        foreach (var layer in new UIElement[] { shadow, backdrop })
        {
            Canvas.SetLeft(layer, Pad);
            Canvas.SetTop(layer, Pad);
            canvas.Children.Add(layer);
        }

        var n = ops.Count;
        var step = n == 0 ? 360 : 360.0 / n;
        var radius = LabelRadius(n);
        var width = LabelWidth(n, radius);
        var sizes = LabelSizes(n);
        var on = Theme.SliceOn(strong);
        var onText = Theme.SliceOnText(strong);
        for (var i = 0; i < n; i++)
        {
            var lit = highlight?.Contains(ops[i].Id) == true;
            var fill = new SolidColorBrush(lit ? on : sliceColor);
            var a0 = i * step - step / 2;
            canvas.Children.Add(new Path
            {
                Data = n == 1 ? Ring() : Sector(a0, a0 + step),
                Fill = n == 1 ? Brushes.Transparent : fill,
                Stroke = fill,
                StrokeThickness = n == 1 ? OuterRadius - InnerRadius : 2 * Corner,
                StrokeLineJoin = PenLineJoin.Round,
            });

            // Try a few wrap widths and nudges along the slice; keep the layout that needs the least shrinking
            // (ties go to the middle position and the wider wrap, so twins on both sides look alike).
            var angle = n == 1 ? 0 : i * step;
            var foreground = new SolidColorBrush(lit ? onText : textColor);
            StackPanel label = null!;
            var natural = default(System.Windows.Size);
            var scale = -1.0;
            var at = radius;
            foreach (var wrap in n <= 2 ? new[] { width } : new[] { width, width * 0.8, width * 0.64 })
            {
                var candidate = SliceLabel(ops[i], wrap, sizes, foreground);
                candidate.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                foreach (var nudge in new[] { 0.0, 6, -6, 12, -12 })
                {
                    var s = FitScale(n, angle, radius + nudge, candidate.DesiredSize);
                    if (s > scale + 0.01)
                    {
                        (label, natural, scale, at) = (candidate, candidate.DesiredSize, s, radius + nudge);
                    }
                }
                if (scale >= 1)
                    break;
            }
            var fit = new Viewbox
            {
                Child = label,
                Stretch = Stretch.Uniform,
                Width = natural.Width * scale,
                Height = natural.Height * scale,
                IsHitTestVisible = false,
            };
            var mid = Polar(at, angle);
            Canvas.SetLeft(fit, mid.X - fit.Width / 2);
            Canvas.SetTop(fit, mid.Y - fit.Height / 2);
            canvas.Children.Add(fit);
            fills?.Add(fill);
            labels?.Add(label);
        }
        if (n == 0)
        {
            canvas.Children.Add(new Path
            {
                Data = Ring(),
                Stroke = new SolidColorBrush(key) { Opacity = 0.35 },
                StrokeThickness = OuterRadius - InnerRadius,
            });
        }

        var hub = new Ellipse { Width = 2 * HubRadius, Height = 2 * HubRadius, Fill = Theme.Hub };
        Canvas.SetLeft(hub, Size / 2 - HubRadius);
        Canvas.SetTop(hub, Size / 2 - HubRadius);
        canvas.Children.Add(hub);
    }

    /// <summary>
    /// A still picture of a wheel with these slices, drawn exactly like the real one (used by the
    /// wheel page in Settings). <paramref name="highlight"/> slices are painted as if selected.
    /// </summary>
    internal static FrameworkElement Preview(IReadOnlyList<Operation> ops, Input.ModifierKey key, WheelKind wheel, string keyName, string title, string detail, ISet<string>? highlight = null, bool back = false, int pages = 1)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        var strong = Theme.KeyStrongColor(key, wheel);
        Draw(canvas, ops, Theme.KeyColor(key, wheel), strong, highlight, null, null, back);
        var hub = new Hub(null);
        hub.Place(canvas);
        hub.Show(keyName, strong, title, detail, [], -1, back ? 2 : 1, pages);
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    private static StackPanel SliceLabel(Operation op, double width, (double Icon, double Text, double Format) sizes, SolidColorBrush foreground)
    {
        var isFormat = op.Wheel == WheelKind.Convert && op.Label.Length <= 6 && op.Label.ToUpperInvariant() == op.Label;
        var weight = isFormat ? FontWeights.ExtraBold : FontWeights.Bold;
        var (size, longest) = FitWords(op.Label, isFormat ? sizes.Format : sizes.Text, weight, width);
        // Slices that first download a tool are shown a little faded. A word that can't fit even at the
        // smallest size widens the label instead, and the whole label is scaled down to fit the slice.
        var panel = new StackPanel { MaxWidth = Math.Max(width, longest + 2), IsHitTestVisible = false, Opacity = op is Shell.NeedsToolOperation ? 0.55 : 1 };
        TextElement.SetForeground(panel, foreground);
        if (!isFormat)
        {
            var icon = new TextBlock
            {
                Text = op.Glyph,
                FontFamily = Theme.Icons,
                FontSize = sizes.Icon,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Typo.TrimToEm(icon, below: Math.Round(sizes.Icon * 0.3));
            panel.Children.Add(icon);
        }
        var text = new TextBlock
        {
            Text = op.Label,
            FontFamily = Theme.DisplayFont,
            FontSize = size,
            FontWeight = weight,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Typo.TrimToCaps(text);
        panel.Children.Add(text);
        return panel;
    }

    /// <summary>
    /// The largest size (down to 11 px) at which the longest word still fits on one line, so labels wrap
    /// between words and never split one in the middle; also returns that word's width at the size.
    /// </summary>
    private static (double Size, double Longest) FitWords(string label, double size, FontWeight weight, double width)
    {
        var typeface = new Typeface(Theme.DisplayFont, FontStyles.Normal, weight, FontStretches.Normal);
        var longest = label.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new FormattedText(word, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 1, Brushes.Black, 1).WidthIncludingTrailingWhitespace)
            .DefaultIfEmpty(0)
            .Max();
        if (longest <= 0)
            return (size, 0);
        var fitted = Math.Max(Math.Min(11, size), Math.Min(size, (width - 2) / longest));
        return (fitted, longest * fitted);
    }

    private void SetHighlight(int index)
    {
        if (index == _highlight)
            return;
        if (_highlight >= 0 && _highlight < _fills.Count)
            Paint(_highlight, false);
        _highlight = index;
        if (_highlight >= 0 && _highlight < _fills.Count)
            Paint(_highlight, true);
        UpdateHub();
    }

    /// <summary>Swaps the slice to ink/paper and back, instantly: colour only, no movement, glow or fade.</summary>
    private void Paint(int index, bool on)
    {
        _fills[index].Color = on ? Theme.SliceOn(KeyStrong) : _back ? Theme.BackSlice(KeyColor) : KeyColor;
        ((SolidColorBrush)TextElement.GetForeground(_labels[index])).Color = on ? Theme.SliceOnText(KeyStrong) : _back ? Theme.BackSliceText(KeyColor) : Theme.OnKey.Color;
    }

    private void UpdateHub()
    {
        var key = _settings.KeyFor(_wheel);
        var keyName = key == Input.ModifierKey.None ? AppSettings.WheelName(_wheel) : AppSettings.KeyName(key);
        var wheels = WheelOrder
            .Where(w => w == _wheel || _settings.KeyFor(w) != Input.ModifierKey.None)
            .Select(w => (w, Theme.KeyStrongColor(_settings.KeyFor(w), w)))
            .ToList();
        var current = wheels.FindIndex(w => w.w == _wheel);
        var (title, detail) = HubText();
        var pages = _otherSide > 0 ? 2 : 1;
        _hub.Show(keyName, KeyStrong, title, detail, wheels, current, _back ? 2 : 1, pages, Teach);
    }

    /// <summary>Until the person has turned a wheel once, a wheel with two pages says how, in the key colour.</summary>
    private bool Teach => _otherSide > 0 && !_settings.FlipLearned && !_repeat;

    private (string Title, string Detail) HubText()
    {
        if (_repeat)
        {
            var last = _ops.Count > 0 ? _ops[0] : null;
            var title = _highlight >= 0 && last is not null ? last.Label : T("Repeat", "Повторить");
            var detail = last is null
                ? T("Last action doesn't fit", "Последнее действие не подходит")
                : _highlight >= 0 ? T("last action", "последнее действие") : last.Label;
            return (title, detail);
        }
        if (_highlight >= 0 && _highlight < _ops.Count)
        {
            var op = _ops[_highlight];
            return (op.Label, op.Subtitle ?? DescribeItems());
        }
        var name = AppSettings.WheelName(_wheel);
        if (Teach)
            return (name, T("Scroll or Tab: more", "Колёсико или Tab"));
        if (_items.Count == 0)
            return (name, T("Drag a file here", "Перетащите файл сюда"));
        if (_ops.Count == 0)
            return (name, T("Nothing for this selection", "Нет действий для выбранных файлов"));
        return (name, DescribeItems());
    }

    private string DescribeItems() => _items.Count == 1 ? _items[0].Name : Items(_items.Count);

    /// <summary>
    /// The middle of the wheel: the key as a coloured keycap, what will happen, which file, and a dot per
    /// wheel (the open one stretched into a pill). In click mode the dots switch wheels.
    /// </summary>
    private sealed class Hub
    {
        private const double ContentWidth = 2 * HubRadius - 22;
        private readonly StackPanel _panel = new() { Width = ContentWidth };
        private readonly Border _keycap = new() { CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(11, 8, 11, 8) };
        private readonly TextBlock _key = new() { FontSize = 17, FontWeight = FontWeights.ExtraBold };
        private readonly TextBlock _title = Line(15, FontWeights.Bold, 0.95);
        private readonly TextBlock _detail = Line(11.5, FontWeights.Medium, 0.55);
        private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        private readonly Action<WheelKind>? _switch;
        // "Pages": a mouse, one bar per page with the current one filled, and "1 / 2".
        private readonly Border _flipHint = new() { CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(7, 4, 9, 4), Margin = new Thickness(0, 10, 0, 0) };
        private readonly TextBlock _flipText = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _flipIcon = new() { FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        private readonly StackPanel _pager = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        private Canvas? _canvas;

        public Hub(Action<WheelKind>? switchTo, Action? flip = null)
        {
            _switch = switchTo;
            _flipIcon.FontFamily = Theme.Icons;
            _flipIcon.Text = Sweetch.Core.Glyphs.Mouse;
            var hint = new StackPanel { Orientation = Orientation.Horizontal };
            hint.Children.Add(_flipIcon);
            hint.Children.Add(_pager);
            hint.Children.Add(_flipText);
            _flipHint.Child = hint;
            if (flip is not null)
            {
                _flipHint.Cursor = Cursors.Hand;
                _flipHint.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    flip();
                };
            }
            _key.FontFamily = Theme.DisplayFont;
            Typo.TrimToCaps(_key);
            _keycap.Child = _key;
            _title.Margin = new Thickness(0, 12, 0, 0);
            _detail.Margin = new Thickness(0, 3, 0, 0);
            _panel.Children.Add(_keycap);
            _panel.Children.Add(_title);
            _panel.Children.Add(_detail);
            _panel.Children.Add(_dots);
            _panel.Children.Add(_flipHint);
        }

        // The hub is dark (or soft grey in light low contrast); its text follows.
        private static TextBlock Line(double size, FontWeight weight, double opacity) => new()
        {
            FontSize = size,
            FontWeight = weight,
            Foreground = Theme.HubText,
            Opacity = opacity,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = ContentWidth,
        };

        /// <param name="page">Which page is up, of <paramref name="pages"/>; with one page there is no pager.</param>
        /// <param name="teach">First time: the pager is drawn in the key colour so it can't be missed.</param>
        public void Show(string keyName, Color key, string title, string detail, IReadOnlyList<(WheelKind Wheel, Color Color)> wheels, int current, int page = 1, int pages = 1, bool teach = false)
        {
            // A tab of the hub's own colour hanging off it; in the key colour on page 2 and while teaching.
            var lit = teach || page > 1;
            var ink = lit ? Theme.OnKey : Theme.HubText;
            _flipHint.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            _flipHint.Background = lit ? new SolidColorBrush(key) : Theme.Hub;
            _flipText.Text = $"{page} / {pages}";
            _flipText.Foreground = _flipIcon.Foreground = ink;
            _pager.Children.Clear();
            for (var i = 1; i <= pages; i++)
            {
                _pager.Children.Add(new Border
                {
                    Width = 4,
                    Height = 6,
                    CornerRadius = new CornerRadius(2),
                    Background = ink,
                    Opacity = i == page ? 1 : 0.35,
                    Margin = new Thickness(0, i == 1 ? 0 : 2, 0, 0),
                });
            }
            _key.Text = keyName.ToUpperInvariant();
            _keycap.Background = new SolidColorBrush(key);
            _key.Foreground = Theme.OnKey;
            _title.Text = title;
            _detail.Text = detail;
            _dots.Visibility = wheels.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            _dots.Children.Clear();
            for (var i = 0; i < wheels.Count; i++)
            {
                var (wheel, color) = wheels[i];
                var dot = new Border
                {
                    Height = 8,
                    Width = i == current ? 24 : 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(color),
                    Opacity = i == current ? 1 : 0.4,
                    Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                    Cursor = _switch is null ? null : Cursors.Hand,
                    ToolTip = _switch is null ? null : AppSettings.WheelName(wheel),
                };
                if (_switch is not null && i != current)
                {
                    var target = wheel;
                    dot.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        _switch(target);
                    };
                }
                _dots.Children.Add(dot);
            }
            if (_canvas is not null)
                Center();
        }

        public void Place(Canvas canvas)
        {
            _canvas = canvas;
            canvas.Children.Add(_panel);
            Center();
        }

        private void Center()
        {
            _panel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_panel, Size / 2 - ContentWidth / 2);
            Canvas.SetTop(_panel, Size / 2 - _panel.DesiredSize.Height / 2);
        }
    }
}
