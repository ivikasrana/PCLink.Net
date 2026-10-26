using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private const int MaxTransferRows = 300;
    private const int MaxReceivedTexts = 100;

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
            if (received.Count > MaxReceivedTexts) received.RemoveAt(received.Count - 1);
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => ExitApp();

    public void ShowWindow()
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
        if (!exiting) ExitApp();
        base.OnClosing(e);
    }

    private static void Paint(TextBlock target, bool good) =>
        target.SetResourceReference(TextBlock.ForegroundProperty, good ? "GoodBrush" : "BadBrush");

    private static void Say(TextBlock target, string text, bool good)
    {
        target.Text = text;
        Paint(target, good);
    }
}
