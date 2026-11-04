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
                window.Title = "GamePanel alpha.15 · anteprima dimostrativa";
                void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
                object Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                void Render(int page)
                {
                    Set("_page", page); type.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    ((ScrollViewer)Get("_pageScroll")).ScrollToTop(); Pump(window);
                }
                Set("_library", library);
                Set("_libraryMessage", "Anteprima dimostrativa · copertine illustrative locali · Originale e Remix nella stessa scheda.");
                window.Show(); Pump(window); Render(5);
                Save(window, Path.Combine(output, "alpha15-library-nfs.png"));

                var report = new GameReport(1, now, remix.ExecutablePath, remix.Sha256, remix.Architecture,
                    remix.GraphicsApis, [new(".trex/d3d9.dll", "RTX Remix", "Demo")], [], false, false);
                var settings = new RemixDlssSettings(1, 4, true, true, true, 3, 1);
                Set("_report", report); Set("_dlssSettings", settings);
                Set("_graphicsAdapters", "NVIDIA GeForce RTX 5080 Laptop GPU · esempio dimostrativo");
                Set("_remixManagedStatus", new RemixManagedStatus(true, true, false, false,
                    "Anteprima dimostrativa dei controlli · nessuna configurazione reale modificata.", ug2.ExecutablePath, settings));
                Set("_remixManagedMessage", "Anteprima dimostrativa · valori di esempio, nessuna misurazione del gioco.");
                Render(1);
                var content = (StackPanel)Get("_content");
                var advanced = content.Children.OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "DlssAdvancedSettings");
                advanced.BringIntoView(); Pump(window);
                Save(window, Path.Combine(output, "alpha15-dlss-parameters.png"));
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
