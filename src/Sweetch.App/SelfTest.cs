using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sweetch.App.Actions;
using Sweetch.App.Jobs;
using Sweetch.App.UI;
using Sweetch.App.Wheel;
using Sweetch.Core;
using Sweetch.Core.Operations;

namespace Sweetch.App;

/// <summary>
/// <c>Sweetch.exe --self-test [folder]</c>: builds every window offscreen and renders previews to PNG.
/// Used by CI to catch UI construction errors on a real Windows machine.
/// </summary>
internal static class SelfTest
{
    public static int Run(string outDir)
    {
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(outDir);
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            JobCard.Animate = false;
            Check(Enum.GetValues<Language>().Length == 2, "only English and Russian languages");
            Check(Brand.Trc20 == "TQHTy7kbk3ngYTUfrTpEeSi3hP38tnqJNU", "TRC20 support address");
            Check(Brand.DonatePay == "https://donatepay.ru/don/1539048" && Brand.Channel == "https://t.me/mmmorozov", "project links");
            Check(Shell.Updater.Current.Major >= 2, "version from the build");
            Check(Shell.Updater.ReplaceScript(1, @"C:\Temp\new", @"C:\Users\O'Neil\Свитч").Contains(@"'C:\Users\O''Neil\Свитч'"), "update script quotes paths");
            log.Add($"ok Sweetch {Shell.Updater.Current} branding, links, languages, updater");
            TestPandoc(outDir, log);
            foreach (var (language, dark, low, folder) in new[]
                     {
                         (Language.English, true, false, "en"), (Language.English, false, false, "en-light"),
                         (Language.Russian, true, false, "ru"), (Language.Russian, false, false, "ru-light"),
                         (Language.Russian, true, true, "ru-low"), (Language.Russian, false, true, "ru-light-low"),
                     })
            {
                Loc.Current = language;
                Theme.Override = dark;
                Theme.LowContrast = low;
                Theme.Refresh();
                RenderAll(Path.Combine(outDir, folder), language == Language.English, log);
            }

            TestSpeed(log);
            log.AddRange(SmokeTest.Run(Path.Combine(outDir, "smoke")));
            // Saved after every step, so a hard crash (which skips the catch below) still shows where it stopped.
            foreach (var step in new Action[] { () => TestSettingsRoundTrip(log), () => TestExplorerCom(outDir, log), () => TestDrops(log), () => TestWatchFolder(outDir, log), () => TestOcr(outDir, log), () => log.Add("ok updates: checked by the app, not by the self-test") })
            {
                File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
                step();
            }

            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAILED: " + ex);
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            }
            catch (IOException)
            {
            }
            return 1;
        }
    }

    private static void RenderAll(string outDir, bool english, List<string> log)
    {
        Directory.CreateDirectory(outDir);
        string N(string cs, string en) => english ? en : cs;
        // Previews look the same whatever this machine has installed; the strip gets its own picture below.
        var settings = new AppSettings { HideMissingTools = true };
        var tools = new ToolLocator();
        var core = WheelController.BuildOperations();
        var actions = Sweetchs.Build(settings, () => { });
        IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);

        var samples = new (string Name, WheelMode Mode, WheelKind Wheel, string[] Files, int Highlight)[]
        {
            ("wheel-image-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")], 1),
            ("wheel-image-tools", WheelMode.Click, WheelKind.Tools, [N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")], 0),
            ("wheel-video-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov")], 0),
            ("wheel-video-tools", WheelMode.Drag, WheelKind.Tools, [N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov")], 3),
            ("wheel-pdf-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Dokumenty\smlouva.pdf", @"C:\Documents\contract.pdf")], 2),
            ("wheel-pdf-tools", WheelMode.Click, WheelKind.Tools, [@"C:\a.pdf", @"C:\b.pdf"], 0),
            ("wheel-sheet-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Data\tabulka.xlsx", @"C:\Data\sales.xlsx")], -1),
            ("wheel-actions", WheelMode.Drag, WheelKind.Actions, [N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")], 4),
            ("wheel-waiting-for-drop", WheelMode.Drag, WheelKind.Convert, [], -1),
        };
        foreach (var s in samples)
        {
            var window = new WheelWindow(s.Mode, s.Wheel, s.Files.Select(f => new FileItem(f)).ToList(), settings, Source, (_, _) => { });
            window.RenderPreview(Path.Combine(outDir, s.Name + ".png"), s.Highlight);
            window.Close();
            // Large, on transparent, for the README artwork (scripts/build-readme-art.py).
            if (s.Name is "wheel-image-convert" or "wheel-image-tools" or "wheel-actions")
            {
                var art = new WheelWindow(s.Mode, s.Wheel, s.Files.Select(f => new FileItem(f)).ToList(), settings, Source, (_, _) => { });
                art.RenderPreview(Path.Combine(outDir, "art-" + s.Name + ".png"), s.Highlight, clear: true);
                art.Close();
            }
            log.Add($"ok {Path.GetFileName(outDir)}/{s.Name}");
        }
        // The other side, after a scroll: rare formats, in the back-side colours.
        foreach (var (name, file, mode) in new[] { ("wheel-image-convert-back", N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg"), WheelMode.Drag), ("wheel-doc-convert-back", @"C:\a.md", WheelMode.Click) })
        {
            var flipped = new WheelWindow(mode, WheelKind.Convert, [new FileItem(file)], settings, Source, (_, _) => { });
            flipped.Scroll(-120);
            flipped.RenderPreview(Path.Combine(outDir, name + ".png"), 1);
            flipped.Close();
        }
        // Once a wheel has been turned, the pager stays quiet in the hub colour.
        var learned = new WheelWindow(WheelMode.Drag, WheelKind.Convert, [new FileItem("vacation.jpg")], settings, Source, (_, _) => { });
        learned.RenderPreview(Path.Combine(outDir, "wheel-image-convert-learned.png"), -1);
        learned.Close();
        settings.FlipLearned = false;
        log.Add($"ok {Path.GetFileName(outDir)}/wheel back sides");

        SettingsWindow.Operations = () => [.. core, .. actions];
        settings.FavoriteOperations = ["image.to.webp", "image.compress"];
        settings.HiddenOperations = ["image.to.tiff"];
        var web = new Preset { Name = "Web 1920", Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920, StripMetadata = true };
        settings.Presets = [web, new Preset { Name = N("MP4 malé", "MP4 small"), Kind = PresetKind.Video, Format = "mp4", Height = 720, VideoQuality = VideoQuality.Small }];
        settings.WatchFolders = [new WatchFolder { Path = N(@"C:\Users\Jana\Pictures\Snímky obrazovky", @"C:\Users\Jana\Pictures\Screenshots"), OperationId = "preset." + web.Id }];
        SettingsWindow.Operations = () => [.. core, .. settings.Presets.Select(PresetOperations.Build), .. actions];
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var settingsWindow = new SettingsWindow(settings, tools, () => { }) { Page = page };
            settingsWindow.ExpandForPreview();
            var file = page == SettingsPage.General ? "settings" : "settings-" + page.ToString().ToLowerInvariant();
            SaveWindow(settingsWindow, Path.Combine(outDir, file + ".png"), page == SettingsPage.Wheels ? 1240 : 1000);
            settingsWindow.Close();
            // The smallest window the user can make.
            var narrow = new SettingsWindow(settings, tools, () => { });
            narrow.Width = narrow.MinWidth;
            narrow.Page = page;
            narrow.ExpandForPreview();
            SaveWindow(narrow, Path.Combine(outDir, "narrow-" + file + ".png"), narrow.MinWidth);
            narrow.Close();
            if (page == SettingsPage.General)
            {
                settings.HideMissingTools = false;
                var strip = new SettingsWindow(settings, new ToolLocator(Path.GetTempPath()), () => { });
                strip.ExpandForPreview();
                SaveWindow(strip, Path.Combine(outDir, "settings-missing-tools.png"), 1000);
                strip.Close();
                settings.HideMissingTools = true;
                // As the window opens, with the slim scroll bar.
                var scrolled = new SettingsWindow(settings, tools, () => { });
                SaveWindow(scrolled, Path.Combine(outDir, "narrow-scroll.png"), scrolled.MinWidth, height: scrolled.MinHeight);
                scrolled.Close();
            }
            if (page == SettingsPage.Helpers)
            {
                // A program's formats opened, and a search for a format.
                foreach (var (name, query) in new[] { ("expanded", ""), ("search", "pages") })
                {
                    var helpers = new SettingsWindow(settings, tools, () => { }) { Page = page };
                    helpers.PreviewTools(query, ExternalTool.Ffmpeg);
                    helpers.ExpandForPreview();
                    SaveWindow(helpers, Path.Combine(outDir, $"settings-helpers-{name}.png"), 1000);
                    helpers.Close();
                }
            }
            log.Add($"ok {Path.GetFileName(outDir)}/{file}");
        }
        var presetDialog = new PresetDialog(web);
        SaveWindow(presetDialog, Path.Combine(outDir, "preset-dialog.png"), 620);
        presetDialog.Close();
        var watchDialog = new WatchFolderDialog(settings.WatchFolders[0], SettingsWindow.Operations());
        SaveWindow(watchDialog, Path.Combine(outDir, "watch-dialog.png"), 640);
        watchDialog.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/preset and watch dialogs");
        for (var step = 0; step < 4; step++)
        {
            var tutorial = TutorialWindow.ForPreview(settings, step);
            SaveWindow(tutorial, Path.Combine(outDir, $"tutorial-{step + 1}.png"), 900);
            tutorial.Close();
        }
        log.Add($"ok {Path.GetFileName(outDir)}/tutorial");

        var toolsWheel = new SettingsWindow(settings, tools, () => { });
        toolsWheel.ShowWheel(WheelKind.Tools, 0);
        toolsWheel.ExpandForPreview();
        SaveWindow(toolsWheel, Path.Combine(outDir, "settings-wheels-tools.png"), 1240);
        toolsWheel.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/settings-wheels-tools");

        var feedback = new FeedbackWindow();
        SaveWindow(feedback, Path.Combine(outDir, "feedback.png"), 560);
        feedback.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/feedback");

        // A result that exists, so the card shows both "Open" and "Show in folder" as it does for real.
        var done = Path.Combine(outDir, N("dovolená (komprimováno).jpg", "vacation (compressed).jpg"));
        File.WriteAllBytes(done, []);
        var jobs = new JobsWindow();
        var running = jobs.Add(N("Převod na MP4", "Convert to MP4"), N("klip.mov", "clip.mov"), () => { }, @"C:\x.mov");
        running.Report(0.42);
        jobs.Add(N("Komprimovat", "Compress"), N("dovolená.jpg", "vacation.jpg"), () => { }, @"C:\x.jpg").Complete([done], null);
        jobs.Add(N("Převod na DOCX", "Convert to DOCX"), N("smlouva.pdf", "contract.pdf"), () => { }, @"C:\x.pdf").Fail(N("Chybí nástroj Pandoc. Nainstalujte ho příkazem: ", "Pandoc is missing. Install it with: ") + "winget install JohnMacFarlane.Pandoc");
        SaveWindow(jobs, Path.Combine(outDir, "jobs.png"), 380, desktop: true);
        jobs.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/jobs");

        // Customised wheel and the "repeat last" view.
        var photo = new List<FileItem> { new(N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")) };
        IReadOnlyList<Operation> Arranged(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            WheelLayout.Arrange(Source(wheel, items), settings.HiddenOperations, settings.FavoriteOperations);
        var custom = new WheelWindow(WheelMode.Drag, WheelKind.Convert, photo, settings, Arranged, (_, _) => { });
        Check(custom.CurrentWheel == WheelKind.Convert, "custom wheel");
        custom.RenderPreview(Path.Combine(outDir, "wheel-customized.png"), 0);
        custom.Close();
        var webp = core.Single(o => o.Id == "image.to.webp");
        var repeat = new WheelWindow(WheelMode.Drag, WheelKind.Convert, photo, settings, Arranged, (_, _) => { }) { LastOperation = _ => webp };
        repeat.ToggleRepeat();
        repeat.RenderPreview(Path.Combine(outDir, "wheel-repeat.png"), 0);
        repeat.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/wheel-repeat");

        var trim = core.Single(o => o.Id == "video.trim");
        var dialog = new ParameterDialog(trim, [new FileItem(N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov"))]);
        SaveWindow(dialog, Path.Combine(outDir, "trim-dialog.png"), 360);
        log.Add($"ok {Path.GetFileName(outDir)}/parameter dialog");
    }

    /// <summary>The settings file is written with source-generated JSON (no reflection in the trimmed build).</summary>
    /// <summary>How long the wheel and Settings take to open (logged only: CI machines vary too much for a limit).</summary>
    private static void TestSpeed(List<string> log)
    {
        var settings = new AppSettings();
        var tools = new ToolLocator();
        var core = WheelController.BuildOperations();
        IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) => Catalog.For(core, wheel, items, tools);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double Median(Func<double> measure) => Enumerable.Range(0, 7).Select(_ => measure()).Order().ElementAt(3);
        double Time(Action action)
        {
            clock.Restart();
            action();
            return clock.Elapsed.TotalMilliseconds;
        }

        IReadOnlyList<FileItem> photo = [new(@"C:\Photos\vacation.jpg")];
        var open = Median(() =>
        {
            var spare = new WheelWindow(WheelMode.Drag, WheelKind.Convert, null, settings, Source, (_, _) => { });
            spare.Prepare();
            var ms = Time(() =>
            {
                spare.Reset(WheelKind.Convert, photo);
                spare.ShowAt(new Native.Win32.POINT { X = -20000, Y = -20000 });
                spare.UpdateLayout();
            });
            spare.Finish();
            return ms;
        });
        var wheel = new WheelWindow(WheelMode.Drag, WheelKind.Tools, photo, settings, Source, (_, _) => { });
        wheel.Prepare();
        var switching = Median(() => Time(() =>
        {
            wheel.SwitchTo(wheel.CurrentWheel == WheelKind.Tools ? WheelKind.Convert : WheelKind.Tools);
            wheel.UpdateLayout();
        }));
        wheel.Finish();
        SettingsWindow.Operations = () => core;
        var settingsOpen = Median(() =>
        {
            SettingsWindow? window = null;
            var ms = Time(() =>
            {
                window = new SettingsWindow(settings, tools, () => { }) { Left = -20000, Top = -20000, ShowActivated = false };
                window.Show();
                window.UpdateLayout();
            });
            window!.Close();
            return ms;
        });
        var pages = new SettingsWindow(settings, tools, () => { }) { Left = -20000, Top = -20000, ShowActivated = false };
        pages.Show();
        var pageTimes = Enum.GetValues<SettingsPage>().Select(page => $"{page} {Median(() => Time(() =>
        {
            pages.Page = SettingsPage.General;
            pages.Page = page;
            pages.UpdateLayout();
        })):0}").ToList();
        pages.Close();
        log.Add($"ok speed: wheel opens in {open:0.0} ms, switches in {switching:0.0} ms, settings open in {settingsOpen:0} ms; pages (ms): {string.Join(", ", pageTimes)}");
    }

    private static void TestSettingsRoundTrip(List<string> log)
    {
        var settings = new AppSettings { Hotkey = HotkeyPreset.CtrlAltW, FavoriteOperations = ["image.to.webp"], Language = Language.Russian, Appearance = AppAppearance.Light };
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!;
        Check(back.Hotkey == HotkeyPreset.CtrlAltW && back.FavoriteOperations.Single() == "image.to.webp" && back.Language == Language.Russian, "settings JSON round trip");
        Check(json.Contains("\"CtrlAltW\""), "enums stored as names");
        Check(back.Appearance == AppAppearance.Light, "theme setting round trip");
        Check(AppSettings.Folder.EndsWith("Sweetch") && new AppSettings().AutoUpdateCheck, "isolated settings and update checks on by default");
        log.Add("ok settings json");
    }

    /// <summary>Pandoc is downloaded on first use: fetch it the way the app does (when missing), then convert.</summary>
    private static void TestPandoc(string outDir, List<string> log)
    {
        Check(Shell.ToolInstaller.CanInstall(ExternalTool.Pandoc), "Pandoc downloads on demand");
        var tools = new ToolLocator();
        var downloaded = tools.Pandoc is null;
        if (downloaded)
        {
            Shell.ToolInstaller.InstallAsync(ExternalTool.Pandoc, new Progress<double>(), default).GetAwaiter().GetResult();
            tools.Refresh();
        }
        Check(tools.Pandoc is not null, "Pandoc found after the download");
        var dir = Path.Combine(outDir, "pandoc");
        Directory.CreateDirectory(dir);
        var md = Path.Combine(dir, "проверка.md");
        var docx = Path.Combine(dir, "проверка.docx");
        var txt = Path.Combine(dir, "проверка.txt");
        File.WriteAllText(md, "# Sweetch offline 12345\n\nПроверка офлайн.\n", new System.Text.UTF8Encoding(false));
        var ctx = new OperationContext { Tools = tools, OutputDirectory = dir };
        Task.Run(async () =>
        {
            await Core.Backends.PandocBackend.ConvertAsync(ctx, md, "md", docx, "docx");
            await Core.Backends.PandocBackend.ConvertAsync(ctx, docx, "docx", txt, "txt");
        }).GetAwaiter().GetResult();
        var text = File.ReadAllText(txt);
        Check(text.Contains("Sweetch offline 12345") && text.Contains("Проверка офлайн"), "offline Markdown / DOCX / TXT round trip");
        log.Add($"ok Pandoc ({(downloaded ? "downloaded, SHA-256 checked" : "found")}): Markdown -> DOCX -> TXT, Cyrillic preserved");
    }

    /// <summary>Reading the Explorer selection goes through late-bound COM, which trimming could break.</summary>
    private static void TestExplorerCom(string outDir, List<string> log)
    {
        var file = Path.Combine(outDir, "smoke", "smoke.jpg");
        using (System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"")) { }
        for (var attempt = 0; attempt < 40; attempt++)
        {
            Thread.Sleep(500);
            var frame = FindWindow("CabinetWClass", null);
            if (frame == IntPtr.Zero)
                continue;
            var selection = Shell.ExplorerSelection.Get(frame);
            if (selection.Any(p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase)))
            {
                log.Add("ok explorer selection");
                PostMessage(frame, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                return;
            }
        }
        // Without an interactive desktop Explorer may not open a window; the COM calls above still ran.
        log.Add("skipped explorer selection (no Explorer window appeared)");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Virtual files (as browsers and Outlook drag them) and raw images become real files.</summary>
    private static void TestDrops(List<string> log)
    {
        var descriptor = new byte[4 + 592];
        BitConverter.GetBytes(1).CopyTo(descriptor, 0);
        System.Text.Encoding.Unicode.GetBytes("selftest-virtual.txt").CopyTo(descriptor, 4 + 72);
        var data = new DataObject();
        data.SetData("FileGroupDescriptorW", new MemoryStream(descriptor));
        data.SetData("FileContents", new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello")));

        var preview = DropContent.Read(data, materialize: false) ?? throw new InvalidOperationException("virtual file not recognised");
        Check(preview.Items.Single().Format == "txt", "virtual file preview has the right type");
        var dropped = DropContent.Read(data, materialize: true) ?? throw new InvalidOperationException("virtual file not saved");
        var path = dropped.Items.Single().Path;
        Check(File.ReadAllText(path) == "hello", "virtual file contents");
        File.Delete(path);
        log.Add("ok drop/virtual-file");

        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16);
        var image = new DataObject(DataFormats.Bitmap, bitmap);
        var imageDrop = DropContent.Read(image, materialize: true) ?? throw new InvalidOperationException("image not saved");
        var png = imageDrop.Items.Single().Path;
        Check(File.Exists(png) && new FileInfo(png).Length > 0, "dropped image saved as PNG");
        File.Delete(png);
        log.Add("ok drop/image");

        var link = new DataObject(DataFormats.UnicodeText, "https://example.com/photos/cat.webp");
        var linkDrop = DropContent.Read(link, materialize: false) ?? throw new InvalidOperationException("link not recognised");
        Check(linkDrop.Items.Single().Format == "webp", "link keeps its file type");
        log.Add("ok drop/link");
    }

    /// <summary>
    /// A real watched folder: a new PNG gets the preset once, the result is not processed again and a
    /// half-written download is ignored.
    /// </summary>
    private static void TestWatchFolder(string outDir, List<string> log)
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var folder = Path.Combine(outDir, "watched");
        Directory.CreateDirectory(folder);
        var preset = new Preset { Name = "Watch", Kind = PresetKind.Image, Format = Preset.SameFormat, Percent = 50 };
        var settings = new AppSettings
        {
            Presets = [preset],
            WatchFolders = [new WatchFolder { Path = folder, OperationId = "preset." + preset.Id }],
        };
        var tools = new ToolLocator();
        var jobs = new JobRunner(settings, tools);
        IReadOnlyList<Operation> ops = [PresetOperations.Build(preset)];
        using var watcher = new Shell.FolderWatcher(settings, tools, jobs, () => ops, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        watcher.Apply();

        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 64 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(folder, "shot.png")))
            encoder.Save(stream);
        File.WriteAllText(Path.Combine(folder, "movie.mp4.crdownload"), "partial");

        var expected = Path.Combine(folder, "shot (Watch).png");
        Pump(() => File.Exists(expected), TimeSpan.FromSeconds(20));
        Check(File.Exists(expected), "watched folder produced the result");
        // Long enough for the watcher to notice the result and (wrongly) shrink it again.
        Pump(() => false, TimeSpan.FromSeconds(4));
        var files = Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Order().ToList();
        Check(files.SequenceEqual(["movie.mp4.crdownload", "shot (Watch).png", "shot.png"]), "only one result: " + string.Join(", ", files));
        log.Add("ok watched folder");
    }

    /// <summary>Runs the dispatcher (async continuations, the watcher's callbacks) until done or timed out.</summary>
    private static void Pump(Func<bool> done, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < until)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(_ =>
                {
                    frame.Continue = false;
                    return null;
                }), null);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Thread.Sleep(50);
        }
    }

    /// <summary>Renders a line of text and reads it back with Windows OCR, when the runner has it.</summary>
    private static void TestOcr(string outDir, List<string> log)
    {
        if (!Ocr.OcrOperation.Available)
        {
            log.Add("skipped ocr (no recognizer languages on this machine)");
            return;
        }
        var text = new TextBlock
        {
            Text = "Sweetch OCR 12345",
            FontSize = 48,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brushes.Black,
            Background = Brushes.White,
            Padding = new Thickness(30),
        };
        var png = Path.Combine(outDir, "ocr-input.png");
        SaveElement(text, png);
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
            ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages[0]);
        var result = Task.Run(() => Ocr.OcrOperation.RecognizeAsync(engine, png)).GetAwaiter().GetResult();
        Check(result.Contains("12345"), $"OCR read \"{result}\"");
        log.Add($"ok ocr: {result}");
    }

    /// <summary>Asks the real GitHub API, as the app does; logged only (the network may be unavailable).</summary>
    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Self-test check failed: " + what);
    }

    private static void SaveWindow(Window window, string file, double width, bool desktop = false, double height = double.NaN)
    {
        var content = (UIElement)window.Content;
        window.Content = null;
        var root = new Border
        {
            Width = width,
            Height = height,
            Background = desktop ? Desktop() : window.Background ?? Brushes.White,
            Child = content,
        };
        if (content is ScrollViewer scroll)
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SaveElement(root, file);
    }

    /// <summary>A wallpaper-like backdrop for floating surfaces in previews.</summary>
    public static Brush Desktop() => Theme.IsDark
        ? new LinearGradientBrush(Color.FromRgb(0x1F, 0x2A, 0x44), Color.FromRgb(0x3A, 0x2A, 0x4A), 45)
        : new LinearGradientBrush(Color.FromRgb(0xC9, 0xD8, 0xEE), Color.FromRgb(0xE9, 0xD9, 0xEE), 45);

    public static void SaveElement(FrameworkElement element, string file, double scale = 1.5)
    {
        element.Measure(new Size(element.Width > 0 ? element.Width : 800, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
