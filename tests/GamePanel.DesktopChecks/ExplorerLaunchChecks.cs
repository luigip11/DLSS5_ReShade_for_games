using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ExplorerLaunchChecks
{
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    public static int Run(string fixture)
    {
        fixture = Path.GetFullPath(fixture);
        var folder = Path.GetDirectoryName(fixture)!;
        var request = Path.Combine(folder, "GamePanelExplorerProbe.request");
        var result = Path.Combine(folder, "GamePanelExplorerProbe.result");
        var sentinel = Guid.NewGuid().ToString("N"); Exception? failure = null;
        var old = Environment.GetEnvironmentVariable("GAMEPANEL_EXPLORER_TEST_TOKEN");
        try
        {
            if (GetShellWindow() == IntPtr.Zero) throw new InvalidOperationException("CI desktop shell unavailable: start Explorer before the broker check.");
            File.WriteAllText(request, "Synthetic locally compiled fixture only. Never an uploaded game.");
            if (File.Exists(result)) File.Delete(result);
            Environment.SetEnvironmentVariable("GAMEPANEL_EXPLORER_TEST_TOKEN", sentinel);
            var thread = new Thread(() =>
            {
                try
                {
                    var type = Assembly.Load("GamePanel").GetType("GamePanel.Desktop.ExplorerGameLauncher", throwOnError: true)!;
                    type.GetMethod("Launch", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [fixture]);
                }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if (!thread.Join(30000)) throw new InvalidOperationException("Explorer broker timeout.");
            if (failure is not null) throw new InvalidOperationException("Explorer broker failed.", failure);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(result) && DateTime.UtcNow < deadline) Thread.Sleep(100);
            if (!File.Exists(result)) throw new InvalidOperationException("Explorer did not run the synthetic fixture.");
            string[] lines = [];
            while (DateTime.UtcNow < deadline)
            {
                try { lines = File.ReadAllLines(result); if (lines.Length >= 2) break; }
                catch (IOException) { }
                Thread.Sleep(100);
            }
            if (lines.Length < 2 || !Path.TrimEndingDirectorySeparator(lines[0]).Equals(Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Explorer did not preserve the explicit game directory.");
            if (lines[1] == sentinel) throw new InvalidOperationException("GamePanel-only environment leaked into the Explorer-launched process.");
            Console.WriteLine("PASS Explorer desktop broker: real synthetic EXE, exact working directory, GamePanel-only environment excluded.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("GAMEPANEL_EXPLORER_TEST_TOKEN", old);
            if (File.Exists(request)) File.Delete(request);
            if (File.Exists(result)) File.Delete(result);
        }
    }
}
