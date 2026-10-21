using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace PCLink.Net;

public partial class MainWindow : Window
{
    private const string DefaultUser = "pclink";
    private const string DefaultUploadFolder = "/Download";
    private const string DefaultSyncRemote = "/Sync";

    private readonly Settings settings = AppState.Settings;
    private readonly PhoneMonitor monitor = new();
    private readonly Tray tray = new();
    private readonly ObservableCollection<TransferItem> transfers = new();
    private readonly ObservableCollection<string> received = new();
    private Task queue = Task.CompletedTask;
    private CancellationTokenSource transferCts = new();
    private CancellationTokenSource? pairCts;
    private bool syncing;
    private bool exiting;

    public MainWindow()
    {
        InitializeComponent();
        ShowConnection();
        UserBox.Text = settings.User;
        ReconnectCheck.IsChecked = settings.ReconnectDrive;
        StartupCheck.IsChecked = settings.StartWithWindows;
        MirrorCheck.IsChecked = settings.MirrorNotifications;
        UploadFolderBox.Text = settings.UploadFolder;
        SyncLocalBox.Text = settings.SyncLocal;
        SyncRemoteBox.Text = settings.SyncRemote;
        TransferList.ItemsSource = transfers;
        ReceivedList.ItemsSource = received;
        var letters = DriveMapper.FreeLetters().ToList();
        if (settings.Drive.Length > 0 && !letters.Contains(settings.Drive)) letters.Insert(0, settings.Drive);
        DriveBox.ItemsSource = letters;
        DriveBox.SelectedItem = settings.Drive.Length > 0 ? settings.Drive : letters.FirstOrDefault();
        ConnectionStatus.Text = AppState.Password.Length > 0 ? "A password is saved." : "No password saved yet.";
        ConnectionStatus.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");

        tray.OpenRequested += () => Dispatcher.Invoke(ShowWindow);
        tray.ExitRequested += () => Dispatcher.Invoke(ExitApp);
        tray.SendClipboardRequested += () => Dispatcher.Invoke(SendClipboardAsync);
        tray.SyncRequested += () => Dispatcher.Invoke(SyncAsync);

        monitor.Status += (online, text) => Dispatcher.Invoke(() =>
        {
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, online ? "GoodBrush" : "BadBrush");
            StatusText.Text = text;
            tray.SetText("PCLink: " + text);
        });
        monitor.Notice += (title, body) => Dispatcher.Invoke(() => tray.Balloon(title, body));
        monitor.TextFromPhone += text => Dispatcher.Invoke(() =>
        {
            received.Insert(0, text);
            try { Clipboard.SetText(text); }
            catch (Exception) { }
            tray.Balloon("Text from phone", "Copied to the clipboard");
        });
        monitor.PhoneMoved += phone => Dispatcher.Invoke(() => PhoneMoved(phone));
        monitor.Start();
        RefreshState();
        UpdateThemeChrome();
    }

    public async Task SaveSnapshotAsync(string path, int tab)
    {
        Tabs.SelectedIndex = tab;
        UpdateThemeChrome();
        await Task.Delay(1500);
        var target = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        App.ApplyTheme(!App.IsDark);
        UpdateThemeChrome();
    }

    private void UpdateThemeChrome()
    {
        Sky.Visibility = App.IsDark ? Visibility.Visible : Visibility.Collapsed;
        BtnTheme.Content = ((char)(App.IsDark ? 0xE706 : 0xE708)).ToString();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase or TextBoxBase or System.Windows.Controls.PasswordBox or ComboBox or TabItem or ListBoxItem or RangeBase or Thumb or ScrollBar) return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        exiting = true;
        transferCts.Cancel();
        pairCts?.Cancel();
        monitor.Stop();
        tray.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private static void Paint(TextBlock target, bool good) =>
        target.SetResourceReference(TextBlock.ForegroundProperty, good ? "GoodBrush" : "BadBrush");

    private static void Say(TextBlock target, string text, bool good)
    {
        target.Text = text;
        Paint(target, good);
    }

    private void ShowConnection()
    {
        HostBox.Text = settings.Host;
        DavPortBox.Text = settings.DavPort.ToString();
        HttpPortBox.Text = settings.HttpPort.ToString();
        FtpPortBox.Text = settings.FtpPort.ToString();
    }

    private void PhoneMoved(PhoneInfo phone)
    {
        settings.Host = phone.Host;
        if (phone.Http > 0) settings.HttpPort = phone.Http;
        if (phone.Dav > 0) settings.DavPort = phone.Dav;
        if (phone.Ftp > 0) settings.FtpPort = phone.Ftp;
        settings.TrySave();
        ShowConnection();
        tray.Balloon("PCLink", $"Phone found at {phone.Host}");
    }

    private void RefreshState()
    {
        WindowsText.Text = WindowsInfo.Describe();
        ServiceText.Text = WindowsInfo.ServiceState("WebClient");
        var state = WebClientTweaks.Read();
        BasicText.Text = state.BasicAuthLevel switch
        {
            2 => "Allowed (level 2)",
            null => "Blocked (Windows default)",
            var level => $"Blocked (level {level})",
        };
        Paint(BasicText, state.BasicOk);
        SizeText.Text = state.FileSizeLimit switch
        {
            uint.MaxValue => "About 4 GB",
            null => "50 MB (Windows default)",
            var bytes => $"{bytes / 1024 / 1024} MB",
        };
        Paint(SizeText, state.SizeOk);
        ApplyButton.IsEnabled = !state.AllOk || ServiceText.Text != "Running";
    }

    private static async Task<bool> RunElevatedAsync(string argument)
    {
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!, argument) { UseShellExecute = true, Verb = "runas" };
            using var process = Process.Start(info)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private async Task RunTweakAsync(Button button, string argument, string success, string failure)
    {
        button.IsEnabled = false;
        var ok = await RunElevatedAsync(argument);
        button.IsEnabled = true;
        RefreshState();
        Say(TweakStatus, ok ? success : failure, ok);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) =>
        await RunTweakAsync((Button)sender, "--apply", "Applied. WebClient restarted.", "Not applied. Allow the administrator prompt and try again.");

    private async void Restore_Click(object sender, RoutedEventArgs e) =>
        await RunTweakAsync((Button)sender, "--restore", "Windows defaults restored.", "Not restored. Allow the administrator prompt and try again.");

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshState();
        TweakStatus.Text = "";
    }

    private bool TryReadPorts(TextBlock status, out int dav, out int http, out int ftp)
    {
        http = ftp = 0;
        var ok = int.TryParse(DavPortBox.Text, out dav) && int.TryParse(HttpPortBox.Text, out http) && int.TryParse(FtpPortBox.Text, out ftp)
                 && Validation.IsPort(dav) && Validation.IsPort(http) && Validation.IsPort(ftp);
        if (!ok) Say(status, "Ports must be numbers from 0 to 65535.", false);
        return ok;
    }

    private bool ReadConnection()
    {
        if (!TryReadPorts(ConnectionStatus, out var dav, out var http, out var ftp)) return false;
        var host = HostBox.Text.Trim();
        if (host.Length > 0 && !Validation.IsHost(host))
        {
            Say(ConnectionStatus, "The phone IP address is not valid.", false);
            return false;
        }
        settings.Host = host;
        settings.DavPort = dav;
        settings.HttpPort = http;
        settings.FtpPort = ftp;
        settings.User = UserBox.Text.Trim();
        settings.Drive = DriveBox.SelectedItem as string ?? "";
        if (PasswordBox.Password.Length > 0)
        {
            AppState.Password = PasswordBox.Password;
            CredentialStore.Save(AppState.Password);
            PasswordBox.Clear();
        }
        settings.TrySave();
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ReadConnection()) Say(ConnectionStatus, "Saved.", true);
    }

    private void Options_Click(object sender, RoutedEventArgs e) => ReadOptions();

    private void ReadOptions()
    {
        settings.ReconnectDrive = ReconnectCheck.IsChecked == true;
        settings.MirrorNotifications = MirrorCheck.IsChecked == true;
        settings.UploadFolder = OrDefault(UploadFolderBox.Text, DefaultUploadFolder);
        settings.SyncLocal = SyncLocalBox.Text.Trim();
        settings.SyncRemote = OrDefault(SyncRemoteBox.Text, DefaultSyncRemote);
        var startup = StartupCheck.IsChecked == true;
        if (startup != settings.StartWithWindows)
        {
            settings.StartWithWindows = startup;
            AutoStart.Set(startup);
        }
        settings.TrySave();
    }

    private static string OrDefault(string text, string fallback) => text.Trim() is { Length: > 0 } value ? value : fallback;

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (pairCts is not null)
        {
            pairCts.Cancel();
            return;
        }
        if (!TryReadPorts(PairText, out var dav, out var http, out var ftp)) return;
        var user = OrDefault(UserBox.Text, DefaultUser);
        var password = Pairing.NewPassword();
        const string prompt = "On the phone open PCLink and tap Scan code from PC.";
        QrImage.Source = Pairing.RenderQr(Pairing.SetupLink(user, password, dav, http, ftp));
        QrImage.Visibility = Visibility.Visible;
        PairButton.Content = "Cancel pairing";
        Say(PairText, prompt, true);
        pairCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var log = new Progress<string>(text => PairText.Text = $"{prompt} {text}");
        PhoneInfo? phone = null;
        try { phone = await Pairing.WaitForPhoneAsync(user, password, http, log, pairCts.Token); }
        catch (Exception) { }
        pairCts.Dispose();
        pairCts = null;
        PairButton.Content = "Show pairing code";
        QrImage.Visibility = Visibility.Collapsed;
        if (phone is null)
        {
            Say(PairText, "Pairing stopped.", false);
            return;
        }
        settings.Host = phone.Host;
        settings.HttpPort = phone.Http > 0 ? phone.Http : http;
        settings.DavPort = phone.Dav;
        settings.FtpPort = phone.Ftp;
        settings.User = user;
        settings.PhoneId = phone.Id;
        settings.TrySave();
        AppState.Password = password;
        CredentialStore.Save(password);
        ShowConnection();
        UserBox.Text = user;
        Say(PairText, $"Paired with {phone.Name} at {phone.Host}.", true);
        Say(ConnectionStatus, "Saved.", true);
    }

    private async void Find_Click(object sender, RoutedEventArgs e)
    {
        FindButton.IsEnabled = false;
        Say(PairText, "Searching the network", true);
        var phones = await Discovery.FindAsync(2000);
        FindButton.IsEnabled = true;
        if (phones.Count == 0)
        {
            Say(PairText, "No phone answered. Start the server in the app and check both are on the same Wi-Fi.", false);
            return;
        }
        var match = phones.FirstOrDefault(p => p.Id == settings.PhoneId) ?? phones[0];
        HostBox.Text = match.Host;
        if (match.Dav > 0) DavPortBox.Text = match.Dav.ToString();
        if (match.Http > 0) HttpPortBox.Text = match.Http.ToString();
        if (match.Ftp > 0) FtpPortBox.Text = match.Ftp.ToString();
        settings.PhoneId = match.Id;
        Say(PairText, $"Found {match.Name} at {match.Host}. Press Save, and enter its password if none is saved.", true);
    }

    private async void Map_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadConnection()) return;
        if (DriveBox.SelectedItem is not string drive)
        {
            Say(DriveStatus, "No free drive letter.", false);
            return;
        }
        if (!WebClientTweaks.Read().BasicOk)
        {
            Say(DriveStatus, "Apply the fixes on the Windows tab first.", false);
            return;
        }
        if (AppState.Password.Length == 0)
        {
            Say(DriveStatus, "Enter the password first.", false);
            return;
        }
        var button = (Button)sender;
        button.IsEnabled = false;
        var (host, port, user, password) = (settings.Host, settings.DavPort, settings.User, AppState.Password);
        var error = await Task.Run(() => DriveMapper.Map(drive, host, port, user, password));
        button.IsEnabled = true;
        Say(DriveStatus, error ?? $"Mapped {drive}", error is null);
    }

    private void Unmap_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string drive) return;
        if (drive == settings.Drive && ReconnectCheck.IsChecked == true)
        {
            ReconnectCheck.IsChecked = false;
            ReadOptions();
        }
        var error = DriveMapper.Unmap(drive);
        Say(DriveStatus, error ?? $"Disconnected {drive}", error is null);
    }

    private void Ftp_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadConnection()) return;
        var user = Uri.EscapeDataString(settings.User);
        Process.Start(new ProcessStartInfo("explorer.exe", $"ftp://{user}@{settings.Host}:{settings.FtpPort}/") { UseShellExecute = true });
    }

    private void Web_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadConnection()) return;
        Process.Start(new ProcessStartInfo($"http://{settings.Host}:{settings.HttpPort}/") { UseShellExecute = true });
    }

    private void Enqueue(Func<Task> job)
    {
        queue = queue.ContinueWith(_ => job(), TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
    }

    private static IEnumerable<(string Local, string Relative)> Expand(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path)) yield return (path, Path.GetFileName(path));
            else if (Directory.Exists(path))
            {
                var parent = Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? path;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var file in Directory.EnumerateFiles(path, "*", options))
                    yield return (file, Path.GetRelativePath(parent, file).Replace('\\', '/'));
            }
        }
    }

    private void QueueUploads(string[]? paths, bool apk)
    {
        if (paths is null) return;
        ReadOptions();
        if (transferCts.IsCancellationRequested) transferCts = new CancellationTokenSource();
        var ct = transferCts.Token;
        foreach (var (local, relative) in Expand(paths))
        {
            if (apk && !local.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
            var item = new TransferItem { Name = relative };
            transfers.Insert(0, item);
            var remote = settings.UploadFolder.TrimEnd('/') + "/" + relative;
            Enqueue(() => RunTransfer(item, local, remote, apk, ct));
        }
    }

    private static async Task RunTransfer(TransferItem item, string local, string remote, bool apk, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            item.Status = "Cancelled";
            return;
        }
        using var client = AppState.NewClient();
        if (client is null)
        {
            item.Status = "Set up the phone first";
            return;
        }
        try
        {
            item.Status = "Sending";
            if (apk)
            {
                await client.InstallApkAsync(local, ct);
                item.Status = "Confirm on phone";
            }
            else
            {
                var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(local)).ToUnixTimeMilliseconds();
                await client.UploadAsync(local, remote, mtime, new Progress<double>(p => item.Progress = p * 100), ct);
                item.Status = "Done";
            }
            item.Progress = 100;
        }
        catch (OperationCanceledException)
        {
            item.Status = "Cancelled";
        }
        catch (Exception e)
        {
            item.Status = "Failed: " + e.Message;
        }
    }

    private void SendDrop_Drop(object sender, DragEventArgs e) => QueueUploads(e.Data.GetData(DataFormats.FileDrop) as string[], false);

    private void ApkDrop_Drop(object sender, DragEventArgs e) => QueueUploads(e.Data.GetData(DataFormats.FileDrop) as string[], true);

    private void CancelTransfers_Click(object sender, RoutedEventArgs e) => transferCts.Cancel();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true)
        {
            SyncLocalBox.Text = dialog.FolderName;
            ReadOptions();
        }
    }

    private async void Sync_Click(object sender, RoutedEventArgs e) => await SyncAsync();

    private async Task SyncAsync()
    {
        if (syncing) return;
        ReadOptions();
        if (settings.SyncLocal.Length == 0)
        {
            SyncLog.Text = "Choose a PC folder first.";
            return;
        }
        using var client = AppState.NewClient();
        if (client is null)
        {
            SyncLog.Text = "Set up the phone first.";
            return;
        }
        syncing = true;
        SyncButton.IsEnabled = false;
        SyncLog.Clear();
        var log = new Progress<string>(line => SyncLog.AppendText(line + Environment.NewLine));
        try
        {
            var summary = await SyncEngine.RunAsync(client, settings.SyncLocal, settings.SyncRemote, log, CancellationToken.None);
            SyncLog.AppendText(summary + Environment.NewLine);
            tray.Balloon("PCLink", summary);
        }
        catch (Exception ex)
        {
            SyncLog.AppendText("Failed: " + ex.Message + Environment.NewLine);
        }
        finally
        {
            syncing = false;
            SyncButton.IsEnabled = true;
        }
    }

    private async Task SendTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Say(ShareStatus, "Nothing to send.", false);
            return;
        }
        using var client = AppState.NewClient();
        if (client is null)
        {
            Say(ShareStatus, "Set up the phone first.", false);
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.SendTextAsync(text, timeout.Token);
            Say(ShareStatus, "Sent. Tap the notification on the phone to copy it.", true);
        }
        catch (Exception ex)
        {
            Say(ShareStatus, "Failed: " + ex.Message, false);
        }
    }

    private async void SendText_Click(object sender, RoutedEventArgs e) => await SendTextAsync(TextToSend.Text);

    private async void SendClipboard_Click(object sender, RoutedEventArgs e) => await SendClipboardAsync();

    private Task SendClipboardAsync()
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch (Exception) { text = ""; }
        return SendTextAsync(text);
    }

    private async void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        DiagButton.IsEnabled = false;
        DiagList.ItemsSource = new[] { new DiagLine("", Brushes.Gray, "Running checks") };
        var good = (Brush)FindResource("GoodBrush");
        var bad = (Brush)FindResource("BadBrush");
        try
        {
            var results = await Task.Run(Diagnostics.RunAsync);
            DiagList.ItemsSource = results.Select(r => new DiagLine(r.Ok ? "✓" : "✗", r.Ok ? good : bad, r.Text)).ToList();
        }
        catch (Exception ex)
        {
            DiagList.ItemsSource = new[] { new DiagLine("✗", bad, "Checks failed: " + ex.Message) };
        }
        finally
        {
            DiagButton.IsEnabled = true;
        }
    }
}
