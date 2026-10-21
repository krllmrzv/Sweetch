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
            var disabledUpdater = new Shell.Updater();
            Check(disabledUpdater.CheckAsync().GetAwaiter().GetResult() is null && disabledUpdater.LastChecked is null, "updates disabled");
            log.Add("ok Sweetch 1.0 branding, links, languages and disabled updates");
            TestBundledPandoc(outDir, log);
            foreach (var (language, dark, folder) in new[] { (Language.English, true, "en"), (Language.English, false, "en-light"), (Language.Russian, true, "ru"), (Language.Russian, false, "ru-light") })
            {
                Loc.Current = language;
                Theme.Override = dark;
                Theme.Refresh();
                RenderAll(Path.Combine(outDir, folder), language == Language.English, log);
            }

            log.AddRange(SmokeTest.Run(Path.Combine(outDir, "smoke")));
            // Saved after every step, so a hard crash (which skips the catch below) still shows where it stopped.
            foreach (var step in new Action[] { () => TestSettingsRoundTrip(log), () => TestExplorerCom(outDir, log), () => TestDrops(log), () => TestWatchFolder(outDir, log), () => TestOcr(outDir, log), () => TestPlayer(outDir, log), () => TestRelight(outDir, log), () => log.Add("ok updates disabled: no network request") })
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
        var settings = new AppSettings();
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
            log.Add($"ok {Path.GetFileName(outDir)}/{s.Name}");
        }

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
            log.Add($"ok {Path.GetFileName(outDir)}/{file}");
        }
        var presetDialog = new PresetDialog(web);
        SaveWindow(presetDialog, Path.Combine(outDir, "preset-dialog.png"), 620);
        presetDialog.Close();
        var watchDialog = new WatchFolderDialog(settings.WatchFolders[0], SettingsWindow.Operations());
        SaveWindow(watchDialog, Path.Combine(outDir, "watch-dialog.png"), 640);
        watchDialog.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/preset and watch dialogs");

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

        var jobs = new JobsWindow();
        var running = jobs.Add(N("Převod na MP4", "Convert to MP4"), N("klip.mov", "clip.mov"), () => { }, @"C:\x.mov");
        running.Report(0.42);
        jobs.Add(N("Komprimovat", "Compress"), N("dovolená.jpg", "vacation.jpg"), () => { }, @"C:\x.jpg").Complete([N(@"C:\Fotky\dovolená (komprimováno).jpg", @"C:\Photos\vacation (compressed).jpg")], null);
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
        var encode = new ParameterDialog(core.Single(o => o.Id == "video.encode"), [new FileItem(N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov"))]);
        Check(encode.Values["codec"] == "H.264", "codec choice default");
        SaveWindow(encode, Path.Combine(outDir, "encode-dialog.png"), 520);
        var speed = new ParameterDialog(core.Single(o => o.Id == "video.speed2"), [new FileItem(N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov"))]);
        Check(speed.Values["speed"] == "2×", "speed choice default");
        SaveWindow(speed, Path.Combine(outDir, "speed-dialog.png"), 620);
        log.Add($"ok {Path.GetFileName(outDir)}/parameter dialog");
    }

    /// <summary>The settings file is written with source-generated JSON (no reflection in the trimmed build).</summary>
    private static void TestSettingsRoundTrip(List<string> log)
    {
        var settings = new AppSettings { Hotkey = HotkeyPreset.CtrlAltW, FavoriteOperations = ["image.to.webp"], Language = Language.Russian, Appearance = AppAppearance.Light };
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!;
        Check(back.Hotkey == HotkeyPreset.CtrlAltW && back.FavoriteOperations.Single() == "image.to.webp" && back.Language == Language.Russian, "settings JSON round trip");
        Check(json.Contains("\"CtrlAltW\""), "enums stored as names");
        Check(back.Appearance == AppAppearance.Light, "theme setting round trip");
        Check(AppSettings.Folder.EndsWith("Sweetch") && !new AppSettings().CheckForUpdates, "isolated settings and updates off by default");
        log.Add("ok settings json");
    }

    private static void TestBundledPandoc(string outDir, List<string> log)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "tools", "pandoc.exe");
        Check(File.Exists(exe), "Pandoc is included in the application folder");
        Check(!Shell.ToolInstaller.CanInstall(ExternalTool.Pandoc), "Pandoc never requires a download");
        var tools = new ToolLocator();
        Check(string.Equals(tools.Pandoc, exe, StringComparison.OrdinalIgnoreCase), "bundled Pandoc takes priority over PATH");
        var dir = Path.Combine(outDir, "bundled-pandoc");
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
        log.Add("ok bundled Pandoc: offline Markdown -> DOCX -> TXT, Cyrillic preserved");
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

    /// <summary>The trim player loads the bundled LibVLC, reads length and frame rate, and steps frames.</summary>
    private static void TestPlayer(string outDir, List<string> log)
    {
        var dir = Path.Combine(outDir, "player");
        Directory.CreateDirectory(dir);
        var trim = WheelController.BuildOperations().Single(o => o.Id == "video.trim");

        var wav = Path.Combine(dir, "тон #1.wav");
        WriteTone(wav, seconds: 2);
        var audio = new PlayerWindow(trim, [new FileItem(wav)], new ToolLocator());
        audio.Show();
        Pump(() => audio.Length > 0, TimeSpan.FromSeconds(15));
        Check(audio.Length is > 1900 and < 2100, $"player reads audio length ({audio.Length} ms)");
        audio.Close();

        var tools = new ToolLocator();
        if (!tools.Has(ExternalTool.Ffmpeg))
        {
            log.Add("ok player (audio; no FFmpeg for a video sample)");
            return;
        }
        var mp4 = Path.Combine(dir, "clip.mp4");
        ProcessRunner.RunAsync(tools.Require(ExternalTool.Ffmpeg), ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=4",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-shortest", "-pix_fmt", "yuv420p", mp4], default).GetAwaiter().GetResult();
        var video = new PlayerWindow(trim, [new FileItem(mp4)], tools);
        video.Show();
        Pump(() => video.Length > 0, TimeSpan.FromSeconds(15));
        Check(video.Length is > 3900 and < 4100 && Math.Abs(video.FrameMs - 40) < 0.01, $"player reads video length and frame rate ({video.Length} ms, {video.FrameMs} ms/frame)");
        Pump(() => false, TimeSpan.FromSeconds(1));
        CaptureScreen(video, Path.Combine(dir, "opened.png"));
        video.TogglePlay();
        Pump(() => false, TimeSpan.FromSeconds(1.5));
        CaptureScreen(video, Path.Combine(dir, "playing.png"));
        video.Step(37 - video.Position / 40);
        Pump(() => false, TimeSpan.FromSeconds(2));
        Check(video.Position == 37 * 40, $"frame step lands on frame 37 ({video.Position} ms)");
        CaptureScreen(video, Path.Combine(dir, "frame-37.png")); // testsrc2 prints the frame number
        video.Step(-1);
        Pump(() => false, TimeSpan.FromSeconds(1));
        Check(video.Position == 36 * 40, $"frame step back ({video.Position} ms)");
        video.MarkHere(end: false);
        video.Step(40);
        Pump(() => false, TimeSpan.FromSeconds(1));
        video.MarkHere(end: true);
        Check(video.Values["start"] == "00:00:01.440" && video.Values["end"] == "00:00:03.079", $"marks keep frames 36–76 ({video.Values["start"]} – {video.Values["end"]})");
        Pump(() => false, TimeSpan.FromSeconds(0.5));
        CaptureScreen(video, Path.Combine(dir, "marked.png"));
        video.Close();

        var watchAction = Sweetchs.Build(new AppSettings(), () => { }).Single(o => o.Id == "action.watch");
        Check(watchAction.AppliesTo([new FileItem(mp4)]) && !watchAction.AppliesTo([new FileItem(Path.Combine(dir, "doc.pdf"))]), "watch action is offered for video only");
        var watch = new PlayerWindow(null, [new FileItem(mp4)], tools);
        watch.Show();
        Pump(() => watch.Position > 500, TimeSpan.FromSeconds(15));
        Check(watch.Position > 500, $"watch starts playing by itself ({watch.Position} ms)");
        CaptureScreen(watch, Path.Combine(dir, "watch.png"));
        watch.Close();

        var picker = new PlayerWindow(WheelController.BuildOperations().Single(o => o.Id == "video.thumbnail"), [new FileItem(mp4)], tools);
        picker.Show();
        Pump(() => picker.Length > 0, TimeSpan.FromSeconds(15));
        Pump(() => false, TimeSpan.FromSeconds(1));
        picker.Step(10);
        picker.AddFrame();
        picker.Step(5);
        picker.AddFrame();
        Check(picker.Values["times"] == "00:00:00.400; 00:00:00.600", $"picked frames ({picker.Values["times"]})");
        Pump(() => false, TimeSpan.FromSeconds(1));
        CaptureScreen(picker, Path.Combine(dir, "frames.png"));
        picker.Close();
        log.Add("ok player");
    }

    /// <summary>The light & color window renders its live preview for a photo and a video.</summary>
    private static void TestRelight(string outDir, List<string> log)
    {
        var tools = new ToolLocator();
        if (!tools.Has(ExternalTool.Ffmpeg))
        {
            log.Add("skipped relight (no FFmpeg)");
            return;
        }
        var dir = Path.Combine(outDir, "relight");
        Directory.CreateDirectory(dir);
        var photo = Path.Combine(dir, "фото.jpg");
        var clip = Path.Combine(dir, "clip.mp4");
        ProcessRunner.RunAsync(tools.Require(ExternalTool.Ffmpeg), ["-hide_banner", "-y", "-f", "lavfi", "-i", "gradients=size=1280x720:seed=7", "-frames:v", "1", photo], default).GetAwaiter().GetResult();
        ProcessRunner.RunAsync(tools.Require(ExternalTool.Ffmpeg), ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:duration=3", "-pix_fmt", "yuv420p", clip], default).GetAwaiter().GetResult();
        var ops = WheelController.BuildOperations();
        foreach (var (id, file) in new[] { ("image.relight", photo), ("video.relight", clip) })
        {
            var window = new RelightWindow(ops.Single(o => o.Id == id), [new FileItem(file)], tools);
            window.Show();
            Pump(() => window.Rendered, TimeSpan.FromSeconds(20));
            Check(window.Rendered, $"{id} preview");
            window.Set("exposure", 40);
            window.Set("warmth", 70);
            Pump(() => false, TimeSpan.FromSeconds(0.5));
            Pump(() => window.Rendered, TimeSpan.FromSeconds(20));
            Check(window.Values["warmth"] == "70", $"{id} values");
            SaveWindow(window, Path.Combine(dir, id + ".png"), 1000);
            window.Close();
        }
        log.Add("ok relight");
    }

    private static void WriteTone(string path, double seconds)
    {
        const int rate = 44100;
        var samples = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        for (var i = 0; i < samples; i++)
            w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
    }

    /// <summary>A real screen grab: the video lives in a native child window that RenderTargetBitmap can't see.</summary>
    private static void CaptureScreen(Window window, string file)
    {
        var toDevice = PresentationSource.FromVisual(window)!.CompositionTarget.TransformToDevice;
        var topLeft = toDevice.Transform(new Point(window.Left, window.Top));
        var size = toDevice.Transform(new Point(window.ActualWidth, window.ActualHeight));
        using var bitmap = new System.Drawing.Bitmap((int)size.X, (int)size.Y);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
            g.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, bitmap.Size);
        bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
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

    private static void SaveWindow(Window window, string file, double width, bool desktop = false)
    {
        var content = (UIElement)window.Content;
        window.Content = null;
        var root = new Border
        {
            Width = width,
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

    public static void SaveElement(FrameworkElement element, string file)
    {
        element.Measure(new Size(element.Width > 0 ? element.Width : 800, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
