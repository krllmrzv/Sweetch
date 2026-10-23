using System.Runtime.InteropServices;
using System.Windows.Threading;
using Sweetch.App.Native;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Input;

public enum ModifierKey
{
    None,
    Shift,
    Ctrl,
    Alt,
}

/// <summary>
/// Low-level mouse and keyboard hooks on a dedicated thread. Detects "a modifier was tapped while the
/// left mouse button is dragging something" – the gesture that opens a wheel – and forwards events to
/// the UI dispatcher.
/// </summary>
internal sealed class GlobalInput : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Win32.HookProc _keyboardProc;
    private readonly Win32.HookProc _mouseProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    private bool _leftDown;
    private Win32.POINT _downAt;
    private volatile bool _dragging;
    private ModifierKey _pendingTap;
    private int _cursorX;
    private int _cursorY;

    public GlobalInput(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    /// <summary>A modifier was pressed and released on its own while dragging.</summary>
    public event Action<ModifierKey>? ModifierTapped;

    /// <summary>The left button was released (any drag ended).</summary>
    public event Action? LeftButtonUp;

    /// <summary>Any mouse button went down, with the physical screen position.</summary>
    public event Action<Win32.POINT>? MouseDown;

    public event Action? EscapePressed;

    /// <summary>The mouse wheel turned over an open wheel (see <see cref="WheelAt"/>); the scroll is kept from the window below.</summary>
    public event Action<int>? Scrolled;

    /// <summary>Tab was pressed while a drag wheel is open (see <see cref="TabFlips"/>); the key is kept from the window below.</summary>
    public event Action? TabPressed;

    /// <summary>Called on the hook thread: should Tab turn the wheel's page right now?</summary>
    public Func<bool>? TabFlips { get; set; }

    /// <summary>Called on the hook thread: is a Sweetch wheel open at this screen point?</summary>
    public Func<Win32.POINT, bool>? WheelAt { get; set; }

    /// <summary>Called on the hook thread to decide whether Alt taps should be neutralised.</summary>
    public Func<ModifierKey, bool>? IsWheelKey { get; set; }

    public bool IsDragging => _dragging;

    private bool _tabDown;

    public Win32.POINT Cursor => new() { X = Volatile.Read(ref _cursorX), Y = Volatile.Read(ref _cursorY) };

    public void Start()
    {
        if (_thread is not null)
            return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = Win32.GetCurrentThreadId();
            var module = Win32.GetModuleHandle(null);
            _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, module, 0);
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, module, 0);
            ready.Set();
            while (Win32.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
                // Hooks are serviced while this thread waits for messages.
            }
            Win32.UnhookWindowsHookEx(_keyboardHook);
            Win32.UnhookWindowsHookEx(_mouseHook);
        })
        {
            IsBackground = true,
            Name = "Sweetch input hooks",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait();
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            throw new InvalidOperationException(T("Could not install the global keyboard and mouse hooks.", "Не удалось установить глобальные обработчики клавиатуры и мыши."));
    }

    public void Dispose()
    {
        if (_thread is null)
            return;
        Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    private static ModifierKey ToModifier(uint vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => ModifierKey.Shift,
        0x11 or 0xA2 or 0xA3 => ModifierKey.Ctrl,
        0x12 or 0xA4 or 0xA5 => ModifierKey.Alt,
        _ => ModifierKey.None,
    };

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                // KBDLLHOOKSTRUCT: vkCode at 0, flags at 8. Read in place: this runs for every key in the system.
                var vk = (uint)Marshal.ReadInt32(lParam);
                var flags = (uint)Marshal.ReadInt32(lParam, 8);
                var message = (int)wParam;
                var injected = (flags & Win32.LLKHF_INJECTED) != 0;
                if (vk == 0x09 && !injected)
                {
                    var down = message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                    if (down && (_tabDown || (_dragging && TabFlips?.Invoke() == true)))
                    {
                        if (!_tabDown)
                            _dispatcher.BeginInvoke(() => TabPressed?.Invoke());
                        _tabDown = true;
                        // Shift+Tab is a combination: releasing Shift afterwards must not count as a tap.
                        _pendingTap = ModifierKey.None;
                        return 1;
                    }
                    if (!down && _tabDown)
                    {
                        // The release of a press we kept, even if the drop already ended the drag: swallowed too,
                        // or the window below would get half a key press, and the next drag's Tab would be lost.
                        _tabDown = false;
                        return 1;
                    }
                }
                // Ignore synthetic keys (ours and other tools'); a tap must come from the keyboard.
                if (!injected)
                    OnKey(vk, message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN);
            }
            catch
            {
                // Never let an exception escape into the system hook chain.
            }
        }
        return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void OnKey(uint vk, bool down)
    {
        var modifier = ToModifier(vk);
        if (down)
        {
            if (modifier == ModifierKey.None)
            {
                _pendingTap = ModifierKey.None;
                if (vk == 0x1B)
                    _dispatcher.BeginInvoke(() => EscapePressed?.Invoke());
                return;
            }
            if (_pendingTap == modifier)
                return; // auto-repeat
            _pendingTap = modifier;
            if (modifier == ModifierKey.Alt && _dragging && IsWheelKey?.Invoke(modifier) == true)
            {
                // A bare Alt press/release would activate the menu bar of the window under the drag.
                Win32.keybd_event(Win32.VK_DUMMY, 0, 0, UIntPtr.Zero);
                Win32.keybd_event(Win32.VK_DUMMY, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            return;
        }

        if (modifier != ModifierKey.None && modifier == _pendingTap)
        {
            _pendingTap = ModifierKey.None;
            if (_dragging)
                _dispatcher.BeginInvoke(() => ModifierTapped?.Invoke(modifier));
        }
        else if (modifier == ModifierKey.None)
        {
            _pendingTap = ModifierKey.None;
        }
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                // MSLLHOOKSTRUCT starts with the point. Read in place: this runs for every mouse move in the system.
                var pt = new Win32.POINT { X = Marshal.ReadInt32(lParam), Y = Marshal.ReadInt32(lParam, 4) };
                Volatile.Write(ref _cursorX, pt.X);
                Volatile.Write(ref _cursorY, pt.Y);
                switch ((int)wParam)
                {
                    case Win32.WM_LBUTTONDOWN:
                        _leftDown = true;
                        _dragging = false;
                        _downAt = pt;
                        Raise(pt);
                        break;
                    case Win32.WM_RBUTTONDOWN:
                    case Win32.WM_MBUTTONDOWN:
                        Raise(pt);
                        break;
                    case Win32.WM_MOUSEMOVE when _leftDown && !_dragging:
                        var dx = Math.Abs(pt.X - _downAt.X);
                        var dy = Math.Abs(pt.Y - _downAt.Y);
                        if (dx > Win32.GetSystemMetrics(Win32.SM_CXDRAG) * 2 || dy > Win32.GetSystemMetrics(Win32.SM_CYDRAG) * 2)
                            _dragging = true;
                        break;
                    case Win32.WM_MOUSEWHEEL when WheelAt?.Invoke(pt) == true:
                        // MSLLHOOKSTRUCT.mouseData: the wheel delta in the high word.
                        var delta = (short)(Marshal.ReadInt32(lParam, 8) >> 16);
                        _dispatcher.BeginInvoke(() => Scrolled?.Invoke(delta));
                        return 1;
                    case Win32.WM_LBUTTONUP:
                        _leftDown = false;
                        _dragging = false;
                        _dispatcher.BeginInvoke(() => LeftButtonUp?.Invoke());
                        break;
                }
            }
            catch
            {
                // Never let an exception escape into the system hook chain.
            }
        }
        return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Raise(Win32.POINT pt) => _dispatcher.BeginInvoke(() => MouseDown?.Invoke(pt));
}
