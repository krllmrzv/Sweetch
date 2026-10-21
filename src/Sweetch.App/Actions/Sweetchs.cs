using System.Diagnostics;
using System.IO;
using System.Windows;
using Sweetch.App.Shell;
using Sweetch.App.UI;
using Sweetch.Core;
using Microsoft.VisualBasic.FileIO;
using static Sweetch.Core.Loc;

namespace Sweetch.App.Actions;

/// <summary>The Actions wheel: watch, copy, move, copy path, terminal, floating shelf and share.</summary>
internal static class Sweetchs
{
    public static IReadOnlyList<Operation> Build(AppSettings settings, Action save) =>
    [
        new AppAction("action.watch", T("Watch", "Смотреть"), Glyphs.Play, items =>
        {
            // One window per file; the player is not modal, so it can stay open next to other work.
            var tools = new ToolLocator();
            foreach (var item in items)
                new PlayerWindow(null, [item], tools).Show();
            return Task.CompletedTask;
        }, T("with sound", "со звуком"), items => items.Count is > 0 and <= 4 && items.All(i => Formats.Video.Contains(i.Format) || Formats.Audio.Contains(i.Format))),
        new AppAction("action.copy", T("Copy", "Копировать"), Glyphs.Copy, items => CopyOrMoveAsync(items, move: false, settings, save), T("to folder…", "в папку…")),
        new AppAction("action.move", T("Move", "Переместить"), Glyphs.Move, items => CopyOrMoveAsync(items, move: true, settings, save), T("to folder…", "в папку…")),
        new AppAction("action.path", T("Copy path", "Копировать путь"), Glyphs.Link, CopyPathAsync),
        new AppAction("action.terminal", T("Terminal", "Терминал"), Glyphs.Terminal, OpenTerminalAsync, T("open here", "открыть здесь")),
        new AppAction("action.float", T("Float", "На полку"), Glyphs.Pin, items =>
        {
            ShelfWindow.Open(items);
            return Task.CompletedTask;
        }, T("keep for later", "оставить на потом")),
        new AppAction("action.share", T("Share", "Поделиться"), Glyphs.Share, ShareHelper.ShareAsync, "Windows",
            items => items.Count > 0 && items.All(i => !i.IsDirectory)),
    ];

    private static async Task CopyOrMoveAsync(IReadOnlyList<FileItem> items, bool move, AppSettings settings, Action save)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = move ? T("Move to…", "Переместить в…") : T("Copy to…", "Копировать в…"),
            InitialDirectory = (move ? settings.LastMoveFolder : settings.LastCopyFolder)
                ?? Path.GetDirectoryName(items[0].Path.TrimEnd('\\', '/')) ?? "",
        };
        // We are a background app at this point; a foreground owner keeps the dialog from opening behind Explorer.
        var owner = ForegroundHelper.Host();
        try
        {
            if (dialog.ShowDialog(owner) != true)
                return;
        }
        finally
        {
            owner.Close();
        }
        var target = dialog.FolderName;
        if (move)
            settings.LastMoveFolder = target;
        else
            settings.LastCopyFolder = target;
        save();

        // The shell dialogs give the familiar progress window, conflict prompts and undo.
        await RunOnStaThread(() =>
        {
            foreach (var item in items)
            {
                var source = item.Path.TrimEnd('\\', '/');
                var destination = Path.Combine(target, item.Name);
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.IsDirectory)
                {
                    if (move)
                        FileSystem.MoveDirectory(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                    else
                        FileSystem.CopyDirectory(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else if (move)
                {
                    FileSystem.MoveFile(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else
                {
                    FileSystem.CopyFile(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
            }
        });
    }

    private static Task RunOnStaThread(Action action)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    private static Task CopyPathAsync(IReadOnlyList<FileItem> items)
    {
        var text = string.Join(Environment.NewLine, items.Select(i => i.Path));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                break;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
            {
                // Another app holds the clipboard open; try again shortly.
                Thread.Sleep(50);
            }
        }
        Notifications.Show(T("Path copied", "Путь скопирован"), items.Count == 1 ? items[0].Path : Items(items.Count));
        return Task.CompletedTask;
    }

    private static Task OpenTerminalAsync(IReadOnlyList<FileItem> items)
    {
        var first = items[0];
        var folder = first.IsDirectory ? first.Path : Path.GetDirectoryName(first.Path)!;
        try
        {
            // Windows Terminal, if installed (app execution alias).
            Process.Start(new ProcessStartInfo("wt.exe", $"-d {QuotePath(folder)}") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit") { WorkingDirectory = folder, UseShellExecute = true });
        }
        return Task.CompletedTask;
    }

    /// <summary>Quotes a folder for a command line; "C:\" would otherwise escape the closing quote.</summary>
    private static string QuotePath(string folder) =>
        "\"" + (folder.EndsWith('\\') ? folder + "." : folder) + "\"";
}
