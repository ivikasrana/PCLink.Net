using System.IO;
using System.Windows;

namespace PCLink.Net;

public partial class MainWindow
{
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
        var client = AppState.NewClient();
        foreach (var (local, relative) in Expand(paths))
        {
            if (apk && !local.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
            var item = new TransferItem { Name = relative };
            transfers.Insert(0, item);
            var remote = settings.UploadFolder.TrimEnd('/') + "/" + relative;
            Enqueue(async () =>
            {
                await RunTransfer(item, client, local, remote, apk, ct);
                TrimTransfers();
            });
        }
        Enqueue(() =>
        {
            client?.Dispose();
            return Task.CompletedTask;
        });
    }

    private void TrimTransfers()
    {
        for (var i = transfers.Count - 1; i >= 0 && transfers.Count > MaxTransferRows; i--)
        {
            if (transfers[i].Finished) transfers.RemoveAt(i);
        }
    }

    private static async Task RunTransfer(TransferItem item, PhoneClient? client, string local, string remote, bool apk, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            item.Status = "Cancelled";
            return;
        }
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
                await Task.Run(() => client.InstallApkAsync(local, ct), ct);
                item.Status = "Confirm on phone";
            }
            else
            {
                var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(local)).ToUnixTimeMilliseconds();
                var progress = new Progress<double>(p => item.Progress = p * 100);
                await Task.Run(() => client.UploadAsync(local, remote, mtime, progress, ct), ct);
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
            var (local, remote) = (settings.SyncLocal, settings.SyncRemote);
            var summary = await Task.Run(() => SyncEngine.RunAsync(client, local, remote, log, CancellationToken.None));
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
}
