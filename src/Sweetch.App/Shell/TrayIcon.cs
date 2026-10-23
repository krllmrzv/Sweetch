using System.Windows;
using Forms = System.Windows.Forms;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Shell;

/// <summary>The notification-area icon with the app menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;
    private Forms.ToolStripMenuItem? _update;
    private Action? _balloonClick;

    public TrayIcon(Action openSettings, Action openClipboard, Action openShelf, Action showHelp, Action sendFeedback, Action<bool> setPaused, bool paused, Action exit)
    {
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Sweetch.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("Wheel for the clipboard", "Меню для буфера обмена"), null, (_, _) => openClipboard());
        menu.Items.Add(T("Settings…", "Настройки…"), null, (_, _) => openSettings());
        menu.Items.Add(T("Empty floating shelf", "Открыть пустую полку"), null, (_, _) => openShelf());
        menu.Items.Add(T("How it works", "Как это работает"), null, (_, _) => showHelp());
        menu.Items.Add(T("Send feedback…", "Отправить отзыв…"), null, (_, _) => sendFeedback());
        _pause = new Forms.ToolStripMenuItem(T("Pause gestures", "Приостановить жесты")) { Checked = paused, CheckOnClick = true };
        _pause.CheckedChanged += (_, _) => setPaused(_pause.Checked);
        menu.Items.Add(_pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("Exit", "Выход"), null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            // The small frame drawn for the tray, not the 32 px one scaled down.
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Sweetch",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonClick;
            _balloonClick = null;
            action?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonClick = null;
        Notifications.Sink = Show;
    }

    public bool Paused
    {
        set => _pause.Checked = value;
    }

    /// <summary>Adds "Update to x.y.z" at the top of the menu and announces it once.</summary>
    public void OfferUpdate(Version version, Action install)
    {
        var text = T($"Update to version {version}", $"Обновить до версии {version}");
        if (_update is null)
        {
            _update = new Forms.ToolStripMenuItem(text) { Font = new System.Drawing.Font(_icon.ContextMenuStrip!.Font, System.Drawing.FontStyle.Bold) };
            _update.Click += (_, _) => install();
            _icon.ContextMenuStrip.Items.Insert(0, _update);
            _icon.ContextMenuStrip.Items.Insert(1, new Forms.ToolStripSeparator());
        }
        Show(T("A new version is available", "Доступна новая версия"),
            T($"Sweetch {version} – click here to install.", $"Sweetch {version} — нажмите здесь для установки."), false);
        _balloonClick = install;
    }

    public void Show(string title, string text, bool error)
    {
        _balloonClick = null;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = string.IsNullOrWhiteSpace(text) ? " " : text;
        _icon.BalloonTipIcon = error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        Notifications.Sink = null;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
