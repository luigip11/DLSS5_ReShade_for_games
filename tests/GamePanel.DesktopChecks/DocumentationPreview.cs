using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamePanel.Core;

// Documentation fixtures live only in this check executable, never in the app.
// Screenshots render the production WPF views; no game or downloaded EXE runs.
internal static class DocumentationPreview
{
    public static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previous = Environment.GetEnvironmentVariable("GAMEPANEL_DATA_ROOT");
            var root = Path.Combine(Path.GetTempPath(), "GamePanel-docs-" + Guid.NewGuid().ToString("N"));
            Window? window = null;
            try
            {
                Environment.SetEnvironmentVariable("GAMEPANEL_DATA_ROOT", Path.Combine(root, "data"));
                var now = DateTimeOffset.UtcNow;
                LibraryGame Game(string id, string title, string folder, string exe)
                {
                    var path = Path.Combine(root, folder, exe); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "Documentation placeholder. Not an executable.");
                    return new(id, title, path, new string('a', 64), "x86 (32 bit)", ["Direct3D 9"], now, now, null, true);
                }
                var ug1 = Game("11111111111111111111111111111111", "Need for Speed Underground", "NFS Underground", "Speed.exe");
                var ug2 = Game("22222222222222222222222222222222", "Need for Speed Underground 2", "NFS Underground 2", "speed2.exe");
                var remix = Game("33333333333333333333333333333333", "Underground 2 · Remix", "NFS Underground 2 Remix", "speed2.exe");
                var library = new LibrarySnapshot(1, remix.Id, [ug1, ug2, remix], [new(ug2.Id, remix.Id)]);
                var type = Assembly.Load("GamePanel").GetType("GamePanel.Desktop.MainWindow", true)!;
                var coverType = type.Assembly.GetType("GamePanel.Desktop.GameCoverStore", true)!;
                foreach (var (game, number) in new[] { (ug1, "01"), (ug2, "02") })
                {
                    var path = (string)coverType.GetMethod("PathFor")!.Invoke(null, [game.Id])!;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); Cover(path, number);
                }
                window = (Window)Activator.CreateInstance(type, nonPublic: true)!;
                window.Width = 1280; window.Height = 960; window.MinWidth = 1000;
                window.Title = "GamePanel alpha.16 · anteprima dimostrativa";
                void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
                object Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                void Render(int page)
                {
                    Set("_page", page); type.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        binder: null, types: Type.EmptyTypes, modifiers: null)!.Invoke(window, null);
                    ((ScrollViewer)Get("_pageScroll")).ScrollToTop(); Pump(window);
                }
                Set("_library", library);
                Set("_libraryMessage", "Anteprima dimostrativa · copertine illustrative locali · Originale e Remix nella stessa scheda.");
                window.Show(); Pump(window); Render(5);
                Require(Find(window, "LibrarySort") is null, "Sort shown for two library games.");
                var names = new[] { "Need for Speed Most Wanted", "Half-Life 2", "Grand Theft Auto San Andreas", "Need for Speed Carbon", "Portal", "Max Payne 2" };
                var extra = names.Select((name, i) => Game((i + 4).ToString("x32"), name, "Demo-" + i, "game.exe") with { LastInspectedAt = now.AddDays(-i - 1) }).ToArray();
                Set("_library", library with { Games = library.Games.Concat(extra).ToArray() }); Render(5);
                var sort = Find(window, "LibrarySort") as ComboBox ?? throw new InvalidOperationException("Sort missing for eight games.");
                Require(sort.Items.Count == 3, "Sort options missing."); sort.SelectedIndex = 2; Pump(window);
                var cards = (Panel)Get("_libraryCards");
                Require(AutomationProperties.GetAutomationId(cards.Children[0]) == "LibraryCard-" + extra[2].Id, "Alphabetical sort incorrect.");
                sort.SelectedIndex = 1; Pump(window); Require(AutomationProperties.GetAutomationId(cards.Children[0]) == "LibraryCard-" + extra[^1].Id, "Oldest sort incorrect.");
                sort.SelectedIndex = 0; Pump(window);
                foreach (var width in new[] { 1000d, 1280d })
                {
                    window.Width = width; Pump(window);
                    var play = Find(window, "LibraryPlay-" + ug2.Id)!; var analyze = Find(window, "LibraryOpen-" + ug2.Id)!;
                    Require(play.ActualWidth > 90 && Math.Abs(play.ActualWidth - analyze.ActualWidth) < 1, "Library action widths differ.");
                    foreach (FrameworkElement card in cards.Children) Require(card.TranslatePoint(new Point(card.ActualWidth, 0), cards).X <= cards.ActualWidth + 1, "Library card overflows.");
                }
                Save(window, Path.Combine(output, "alpha16-library-nfs.png"));
                var deselect = (Button)Find(window, "DeselectLibraryGame")!;
                Require(deselect.Content is Panel, "Deselect icon missing.");
                ((ScrollViewer)Get("_pageScroll")).ScrollToVerticalOffset(260); Pump(window);
                Save(window, Path.Combine(output, "alpha16-library-actions.png"));
                Console.WriteLine("PASS UX library: conditional sorting, real selection order, equal action widths and responsive cards.");

                var report = new GameReport(1, now, remix.ExecutablePath, remix.Sha256, remix.Architecture,
                    remix.GraphicsApis, [new(".trex/d3d9.dll", "RTX Remix", "Demo")], [], false, false);
                var settings = new RemixDlssSettings(1, 4, true, true, true, 3, 1);
                Set("_report", report); Set("_dlssSettings", settings);
                Set("_assetMessage", "Anteprima dimostrativa dei percorsi per la copia selezionata.");
                Set("_graphicsAdapters", "NVIDIA GeForce RTX 5080 Laptop GPU · esempio dimostrativo");
                Set("_remixManagedStatus", new RemixManagedStatus(true, true, false, false,
                    "Anteprima dimostrativa dei controlli · nessuna configurazione reale modificata.", ug2.ExecutablePath, settings));
                Set("_remixManagedMessage", "Anteprima dimostrativa · valori di esempio, nessuna misurazione del gioco.");
                Set("_capabilityReport", report); Set("_selectedCapabilities", new GameCapabilities(true, true, true, false, false, false, false, "Need for Speed Underground 2", "Documentation fixture"));
                Render(0);
                Require(Find(window, "GameSelectedCover") is not null, "Selected cover missing on Game.");
                var hardware = Find(window, "GameHardwareCard")!;
                Require(Find(hardware, "WindowsSystemInfo") is not null, "System information outside hardware card.");
                var gamePlay = Find(window, "GamePlay")!; Require(gamePlay.HorizontalAlignment == HorizontalAlignment.Stretch, "Game play not stretched.");
                Save(window, Path.Combine(output, "alpha16-game.png"));
                Require(Find(hardware, "HardwareCpu") is TextBlock { Text.Length: > 0 } && Find(hardware, "HardwareRam") is TextBlock { Text.Length: > 0 }, "CPU/RAM specification cards missing.");
                hardware.BringIntoView(); Pump(window); Save(window, Path.Combine(output, "alpha16-hardware.png"));
                Render(7); var tabs = (TabControl)Find(window, "AssetsTabs")!;
                var first = (TabItem)tabs.Items[0]; var second = (TabItem)tabs.Items[1];
                Require(first.ActualWidth > 200 && Math.Abs(first.ActualWidth - second.ActualWidth) < 1, "Graphics tabs not equal-width.");
                var dlssLink = (Button)Find(window, "GraphicsOpenDlss")!; var screenLink = (Button)Find(window, "GraphicsOpenScreen")!;
                Require(dlssLink.Content is Panel && screenLink.Content is Panel && Math.Abs(dlssLink.ActualWidth - screenLink.ActualWidth) < 1 && dlssLink.ActualWidth > 200, "Graphics shortcut icons or expanded widths missing.");
                Require(Find(window, "GraphicsPlay") is null, "Graphics play should be removed.");
                Save(window, Path.Combine(output, "alpha16-graphics.png"));
                var backups = (BackupService)Get("_backups");
                var backupReport = report with { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(report.ExecutablePath))).ToLowerInvariant(), Components = [] };
                var snapshot = backups.Create(backupReport);
                Set("_snapshots", backups.List(report.ExecutablePath)); Set("_selectedBackup", snapshot.Id); Set("_backupForPath", report.ExecutablePath);
                Render(3); var create = Find(window, "CreateBackup")!; var load = Find(window, "LoadBackups")!;
                Require(Math.Abs(create.ActualWidth - load.ActualWidth) < 1 && create.ActualWidth > 200, "Backup actions not equal-width.");
                Require(Find(window, "BackupPoints") is not null && Find(window, "VerifyBackup") is Button { Content: Panel } && Find(window, "DeleteBackup-" + snapshot.Id) is Button { Content: Panel }, "Backup point, inspect or delete missing.");
                var faqBackup = ((StackPanel)Get("_content")).Children.OfType<Expander>().First();
                Require(Find(window, "BackupPoints")!.TranslatePoint(new Point(0, Find(window, "BackupPoints")!.ActualHeight), window).Y + 20 <= faqBackup.TranslatePoint(new Point(), window).Y, "Backup FAQ spacing too small.");
                Save(window, Path.Combine(output, "alpha16-backups.png"));
                Render(6); Set("_resolutionMethod", ResolutionMethod.Ini); Render(6);
                var monitorPreview = Find(window, "SelectedMonitorPreview") ?? throw new InvalidOperationException("Monitor illustration missing.");
                var monitorChoice = Find(window, "TargetDisplay")!; var resolutionChoice = (ComboBox)Find(window, "GameResolutionSize")!;
                Require(Math.Abs(monitorChoice.TranslatePoint(new Point(), window).Y - resolutionChoice.TranslatePoint(new Point(), window).Y) < 1, "Monitor and resolution selectors not on same row.");
                if (resolutionChoice.Items.Count > 1) { resolutionChoice.SelectedIndex = resolutionChoice.SelectedIndex == 0 ? 1 : 0; Pump(window); }
                Require(((TextBlock)Find(window, "DetectedDisplayResolution")!).Text == resolutionChoice.SelectedItem.ToString()!.Split(" · ")[0], "Monitor preview not updated to selected mode.");
                Require(Find(window, "ScreenAdvancedSettings") is Expander && Find(window, "PrepareGameResolution") is Button { Content: Panel, HorizontalAlignment: HorizontalAlignment.Stretch }, "Screen advanced controls or prepare action missing.");
                var sectionInput = Find(window, "ResolutionIniSection")!; var widthInput = Find(window, "ResolutionIniWidth")!; var heightInput = Find(window, "ResolutionIniHeight")!;
                Require(Math.Abs(sectionInput.TranslatePoint(new Point(), window).Y - widthInput.TranslatePoint(new Point(), window).Y) < 1 && Math.Abs(widthInput.TranslatePoint(new Point(), window).Y - heightInput.TranslatePoint(new Point(), window).Y) < 1, "INI inputs not on one row.");
                Save(window, Path.Combine(output, "alpha16-screen.png"));
                ((Expander)Find(window, "ScreenAdvancedSettings")!).BringIntoView(); Pump(window); Save(window, Path.Combine(output, "alpha16-screen-advanced.png"));
                Render(8); Require(Find(window, "HdrDisplayStatus") is not null && Find(window, "WindowsHdrSettings") is not null, "HDR page unavailable.");
                Save(window, Path.Combine(output, "alpha16-hdr.png"));
                Render(1);
                var capture = (Button)Find(window, "CaptureRemixProfile")!; var restoreProfile = (Button)Find(window, "RestoreRemixProfile")!;
                Require(capture.Content is Panel && restoreProfile.Content is Panel && Math.Abs(capture.ActualWidth - restoreProfile.ActualWidth) < 1 && ReferenceEquals(capture.Parent, restoreProfile.Parent), "DLSS save/restore not equal-width icon actions on one row.");
                ((FrameworkElement)capture.Parent).BringIntoView(); Pump(window); Save(window, Path.Combine(output, "alpha16-dlss-profile.png"));
                var content = (StackPanel)Get("_content");
                var advanced = content.Children.OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "DlssAdvancedSettings");
                advanced.BringIntoView(); Pump(window);
                Save(window, Path.Combine(output, "alpha16-dlss-parameters.png"));
                var playDlss = Find(window, "RemixProfilePlay")!;
                Require(playDlss.HorizontalAlignment == HorizontalAlignment.Stretch, "DLSS play not stretched.");
                var faq = content.Children.OfType<Expander>().Single(e => e.Header is string text && text == "Fluidità e prestazioni");
                Require(content.Children.IndexOf(playDlss) < content.Children.IndexOf(faq), "DLSS play below FAQ.");
                Require(Find(window, "ProjectRepository") is not null, "Author repository action missing.");
                Console.WriteLine("PASS UX game, graphics, backups, HDR, DLSS action order and repository attribution.");
                Console.WriteLine("PASS documentation screenshots: production WPF library and DLSS controls, isolated demonstration data only.");
            }
            catch (Exception e) { failure = e; }
            finally
            {
                window?.Close(); Environment.SetEnvironmentVariable("GAMEPANEL_DATA_ROOT", previous);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Documentation preview failed.", failure);
        return 0;
    }
    private static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (root is FrameworkElement self && AutomationProperties.GetAutomationId(self) == id) return self;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var found = Find(VisualTreeHelper.GetChild(root, i), id); if (found is not null) return found; }
        return null;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    private static void Save(Visual visual, string path)
    {
        var size = (FrameworkElement)visual;
        var image = new RenderTargetBitmap((int)size.ActualWidth, (int)size.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); png.Save(stream);
    }
    private static void Cover(string path, string number)
    {
        var art = new DrawingVisual();
        using (var dc = art.RenderOpen())
        {
            var green = new SolidColorBrush(Color.FromRgb(188, 255, 105));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 22, 32)), null, new Rect(0, 0, 600, 340));
            for (var i = 0; i < 15; i++)
            {
                var height = 30 + i * 37 % 110;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 47, 59)), null, new Rect(i * 43, 270 - height, 30, height));
            }
            dc.DrawLine(new Pen(green, 3), new Point(30, 273), new Point(570, 273));
            void Text(string text, int size, Brush brush, double x, double y) => dc.DrawText(new FormattedText(text,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), size, brush, 1), new Point(x, y));
            Text("NEED FOR SPEED", 21, Brushes.White, 32, 24);
            Text("UNDERGROUND", 37, green, 30, 61);
            Text(number, 94, Brushes.White, 435, 115);
            Text("COPERTINA ILLUSTRATIVA", 13, green, 32, 302);
        }
        var image = new RenderTargetBitmap(600, 340, 96, 96, PixelFormats.Pbgra32); image.Render(art);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
