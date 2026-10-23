using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sweetch.Core;
using static Sweetch.App.UI.Controls;
using static Sweetch.Core.Loc;

namespace Sweetch.App.UI;

/// <summary>The Presets and Watched folders pages.</summary>
internal sealed partial class SettingsWindow
{
    /// <summary>Ready-made presets offered while the list is empty.</summary>
    private static Preset[] PresetTemplates =>
    [
        new() { Name = T("Web 1920", "Для интернета 1920"), Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920, StripMetadata = true },
        new() { Name = "WebP 1280", Kind = PresetKind.Image, Format = "webp", Quality = 75, MaxSide = 1280 },
        new() { Name = T("Half size", "Половина размера"), Kind = PresetKind.Image, Format = Preset.SameFormat, Quality = 90, Percent = 50 },
        new() { Name = T("MP4 small", "MP4 компактный"), Kind = PresetKind.Video, Format = "mp4", Height = 720, VideoQuality = VideoQuality.Small },
        new() { Name = "MP3 128k", Kind = PresetKind.Audio, Format = "mp3", Bitrate = 128 },
    ];

    // ------------------------------------------------------------------ presets

    private void PresetsPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Presets),
            T("Slices of your own with fixed settings, like “JPG 70 % max 1920 px” or “MP4 720p”. They appear on the wheels and in watched folders.", "Свои секторы с фиксированными настройками, например «JPG 70 % макс. 1920 px» или «MP4 720p». Они появятся в меню и отслеживаемых папках."));
        var add = Button(T("New preset", "Новый шаблон"), () => EditPreset(null), primary: true, glyph: Glyphs.Add);
        add.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(add);

        if (_settings.Presets.Count == 0)
        {
            root.Children.Add(SectionHeader(T("Quick start", "Быстрый старт")));
            foreach (var template in PresetTemplates)
            {
                var t = template;
                root.Children.Add(Card(t.Name, PresetOperations.Summary(t),
                    Button(T("Add", "Добавить"), () => AddPreset(t), glyph: Glyphs.Add), glyph: PresetGlyph(t)));
            }
            return;
        }

        root.Children.Add(SectionHeader(T("Your presets", "Ваши шаблоны")));
        foreach (var preset in _settings.Presets.ToList())
        {
            var p = preset;
            var wheel = p.Format == Preset.SameFormat ? WheelKind.Tools : WheelKind.Convert;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Button(T("Edit", "Изменить"), () => EditPreset(p), glyph: Glyphs.Edit));
            var delete = Button(T("Delete", "Удалить"), () => DeletePreset(p), glyph: Glyphs.Delete);
            delete.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(delete);
            var name = string.IsNullOrWhiteSpace(p.Name) ? PresetOperations.Summary(p) : p.Name;
            root.Children.Add(Card(name, $"{AppSettings.WheelName(wheel)} · {PresetOperations.Summary(p)}", actions, glyph: PresetGlyph(p)));
        }
    }

    private static string PresetGlyph(Preset p) => p.Kind switch
    {
        PresetKind.Image => Glyphs.Photo,
        PresetKind.Video => Glyphs.Video,
        _ => Glyphs.Audio,
    };

    private void AddPreset(Preset preset)
    {
        _settings.Presets.Add(preset);
        Save();
        Rebuild();
    }

    private void EditPreset(Preset? existing)
    {
        var dialog = new PresetDialog(existing) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var index = existing is null ? -1 : _settings.Presets.IndexOf(existing);
        if (index >= 0)
            _settings.Presets[index] = dialog.Result;
        else
            _settings.Presets.Add(dialog.Result);
        Save();
        Rebuild();
    }

    private void DeletePreset(Preset preset)
    {
        var id = "preset." + preset.Id;
        var used = _settings.WatchFolders.Where(f => f.OperationId == id).ToList();
        var question = used.Count == 0
            ? T($"Delete the preset “{preset.Name}”?", $"Удалить шаблон «{preset.Name}»?")
            : T($"The preset “{preset.Name}” is used by {used.Count} watched folder(s); they will be turned off. Delete?", $"Шаблон «{preset.Name}» используется в отслеживаемых папках ({used.Count}). Отслеживание будет отключено. Удалить?");
        if (MessageBox.Show(this, question, "Sweetch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _settings.Presets.Remove(preset);
        _settings.FavoriteOperations.Remove(id);
        _settings.HiddenOperations.Remove(id);
        foreach (var folder in used)
            folder.Enabled = false;
        Save();
        Rebuild();
    }

    // ------------------------------------------------------------------ watched folders

    private void WatchFoldersPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.WatchFolders),
            T("Whatever lands in a chosen folder is processed on its own. For example screenshots to WebP, or phone photos from HEIC to JPG.", "Новые файлы в выбранной папке обрабатываются автоматически. Например, снимки экрана → WebP или фото с телефона HEIC → JPG."));
        var add = Button(T("Add folder", "Добавить папку"), () => EditWatchFolder(null), primary: true, glyph: Glyphs.Add);
        add.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(add);

        if (_settings.WatchFolders.Count > 0)
        {
            root.Children.Add(SectionHeader(T("Watched folders", "Отслеживаемые папки")));
            var operations = Operations?.Invoke() ?? [];
            foreach (var folder in _settings.WatchFolders.ToList())
            {
                var f = folder;
                var op = operations.FirstOrDefault(o => o.Id == f.OperationId);
                var what = op is null ? T("action missing", "действие отсутствует") : op.Label + (string.IsNullOrEmpty(op.Subtitle) ? "" : $" ({op.Subtitle})");
                var exists = Directory.Exists(f.Path);
                var description = $"{f.Path} → {what}"
                    + (f.IncludeSubfolders ? T(" · with subfolders", " · с подпапками") : "")
                    + (exists ? "" : T(" · folder not found", " · папка не найдена"));
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var toggle = Toggle(f.Enabled, v =>
                {
                    f.Enabled = v;
                    Save();
                });
                toggle.VerticalAlignment = VerticalAlignment.Center;
                toggle.Margin = new Thickness(0, 0, 14, 0);
                actions.Children.Add(toggle);
                actions.Children.Add(Button(T("Edit", "Изменить"), () => EditWatchFolder(f), glyph: Glyphs.Edit));
                var delete = Button(T("Delete", "Удалить"), () =>
                {
                    _settings.WatchFolders.Remove(f);
                    Save();
                    Rebuild();
                }, glyph: Glyphs.Delete);
                delete.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(delete);
                var name = Path.GetFileName(f.Path.TrimEnd('\\', '/'));
                root.Children.Add(Card(string.IsNullOrEmpty(name) ? f.Path : name, description, actions, glyph: Glyphs.Folder));
            }
        }

        root.Children.Add(SectionHeader(T("How it works", "Как это работает")));
        root.Children.Add(Card(T("Only new files", "Только новые файлы"),
            T("A file is processed once it has finished downloading or copying. What was already in the folder stays as it is. Sweetch's own results are not processed again.", "Файл обрабатывается после завершения загрузки или копирования. Уже находившиеся в папке файлы не меняются. Результаты Sweetch не обрабатываются повторно."),
            null, glyph: Glyphs.Info));
        root.Children.Add(Card(T("Where results go", "Куда сохранять результаты"),
            T("As set in General → Save results. Originals are never changed. Folders are watched only while Sweetch is running.", "Согласно разделу «Основные → Сохранять результаты». Исходники не изменяются. Папки отслеживаются, пока запущен Sweetch."),
            null, glyph: Glyphs.Folder));
    }

    private void EditWatchFolder(WatchFolder? existing)
    {
        var dialog = new WatchFolderDialog(existing, Operations?.Invoke() ?? []) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var index = existing is null ? -1 : _settings.WatchFolders.IndexOf(existing);
        if (index >= 0)
            _settings.WatchFolders[index] = dialog.Result;
        else
            _settings.WatchFolders.Add(dialog.Result);
        Save();
        Rebuild();
    }
}
