using System.IO;
using System.Windows;

namespace PCLink.Net;

public partial class App : Application
{
    private static readonly string ThemeFile = Path.Combine(AppPaths.DataFolder, "theme.txt");

    public static bool IsDark { get; private set; } = true;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (e.Args.Contains("--apply"))
        {
            Shutdown(WebClientTweaks.TryApply(out _) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--restore"))
        {
            Shutdown(WebClientTweaks.TryRestore(out _) ? 0 : 1);
            return;
        }
        if (e.Args.Length >= 6 && e.Args[0] == "--selftest")
        {
            Shutdown(await SelfTest.RunAsync(e.Args));
            return;
        }
        if (e.Args.Length >= 3 && e.Args[0] == "--shot")
        {
            ApplyTheme(!e.Args.Contains("light"), false);
            var preview = new MainWindow();
            preview.Show();
            await preview.SaveSnapshotAsync(e.Args[1], int.Parse(e.Args[2]));
            Shutdown(0);
            return;
        }
        instanceMutex = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            if (!e.Args.Contains("--tray"))
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            }
            Shutdown(0);
            return;
        }
        if (AppState.Settings.StartWithWindows) _ = Task.Run(() => AutoStart.Set(true));
        try { IsDark = !File.Exists(ThemeFile) || File.ReadAllText(ThemeFile).Trim() != "light"; } catch { }
        ApplyTheme(IsDark);
        var window = new MainWindow();
        ListenForShowRequests(window);
        if (!e.Args.Contains("--tray")) window.Show();
    }

    private const string InstanceName = @"Local\PCLink.Net.Instance";
    private const string ShowEventName = @"Local\PCLink.Net.Show";
    private static Mutex? instanceMutex;

    private static void ListenForShowRequests(MainWindow window)
    {
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (showEvent.WaitOne())
                window.Dispatcher.BeginInvoke(window.ShowWindow);
        })
        { IsBackground = true };
        thread.Start();
    }

    public static void ApplyTheme(bool dark, bool persist = true)
    {
        IsDark = dark;
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile)!);
            File.WriteAllText(ThemeFile, dark ? "dark" : "light");
        }
        catch { }
    }
}
