namespace PCLink.Net;

public sealed class PhoneMonitor
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(20);
    private readonly CancellationTokenSource stop = new();
    private bool online;
    private DateTime lastDiscovery = DateTime.MinValue;
    private long lastNote = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public event Action<bool, string>? Status;
    public event Action<string, string>? Notice;
    public event Action<string>? TextFromPhone;
    public event Action<PhoneInfo>? PhoneMoved;

    public void Start() => Task.Run(Loop);

    public void Stop() => stop.Cancel();

    private async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            try { await Tick(); }
            catch (Exception) when (!stop.IsCancellationRequested) { }
            try { await Task.Delay(Interval, stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private CancellationTokenSource Timeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(RequestTimeout);
        return timeout;
    }

    private async Task Tick()
    {
        var s = AppState.Settings;
        if (string.IsNullOrWhiteSpace(s.Host))
        {
            Status?.Invoke(false, "No phone set up");
            return;
        }
        using var client = AppState.NewClient();
        PhoneInfo? info = null;
        if (client is not null)
        {
            try
            {
                using var timeout = Timeout();
                info = await client.InfoAsync(timeout.Token);
            }
            catch (Exception) when (!stop.IsCancellationRequested) { }
        }

        if (client is null || info is null)
        {
            Status?.Invoke(false, "Phone offline");
            online = false;
            await Rediscover(s.PhoneId, s.Host);
            return;
        }

        Status?.Invoke(true, $"Connected to {info.Name} at {s.Host}");
        await ReconnectDrive(s);

        using (var timeout = Timeout())
        {
            foreach (var text in await client.OutboxAsync(timeout.Token)) TextFromPhone?.Invoke(text);
        }

        if (s.MirrorNotifications)
        {
            using var timeout = Timeout();
            var notes = await client.NotificationsAsync(lastNote, timeout.Token);
            foreach (var note in notes.OrderBy(n => n.Time).TakeLast(5))
                Notice?.Invoke(note.App, string.Join("\n", new[] { note.Title, note.Text }.Where(t => t.Length > 0)));
            if (notes.Count > 0) lastNote = notes.Max(n => n.Time);
        }
        else lastNote = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private async Task Rediscover(string phoneId, string host)
    {
        if (phoneId.Length == 0 || DateTime.UtcNow - lastDiscovery < DiscoveryInterval) return;
        lastDiscovery = DateTime.UtcNow;
        var match = (await Discovery.FindAsync()).FirstOrDefault(p => p.Id == phoneId);
        if (match is not null && match.Host != host) PhoneMoved?.Invoke(match);
    }

    private async Task ReconnectDrive(Settings s)
    {
        var drive = s.Drive;
        var host = s.Host;
        var port = s.DavPort;
        var user = s.User;
        var password = AppState.Password;
        if (!s.ReconnectDrive || drive.Length == 0 || port <= 0 || !WebClientTweaks.Read().BasicOk) return;
        var cameBack = !online;
        online = true;
        if (!DriveMapper.IsConnected(drive)) await Task.Run(() => DriveMapper.Map(drive, host, port, user, password));
        else if (cameBack) DriveMapper.RefreshExplorer(drive);
    }
}
