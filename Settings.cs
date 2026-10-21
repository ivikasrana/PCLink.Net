using System.IO;
using System.Text.Json;

namespace PCLink.Net;

public class Settings
{
    public string Host { get; set; } = "";
    public int DavPort { get; set; } = 1099;
    public int HttpPort { get; set; } = 1199;
    public int FtpPort { get; set; } = 1299;
    public string User { get; set; } = "pclink";
    public string PhoneId { get; set; } = "";
    public string Drive { get; set; } = "";
    public bool ReconnectDrive { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MirrorNotifications { get; set; }
    public string UploadFolder { get; set; } = "/Download";
    public string SyncLocal { get; set; } = "";
    public string SyncRemote { get; set; } = "/Sync";

    private static readonly string FilePath = Path.Combine(AppPaths.DataFolder, "settings.json");
    private static readonly Lock SaveLock = new();

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        lock (SaveLock)
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, true);
        }
    }

    public bool TrySave()
    {
        try
        {
            Save();
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

public static class AppPaths
{
    public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCLink.Net");
}

public static class AppState
{
    public static Settings Settings { get; } = Settings.Load();
    public static string Password { get; set; } = CredentialStore.Load();

    public static PhoneClient? NewClient()
    {
        var s = Settings;
        if (string.IsNullOrWhiteSpace(s.Host) || Password.Length == 0) return null;
        var port = s.HttpPort > 0 ? s.HttpPort : s.DavPort;
        if (!Validation.IsHost(s.Host) || !Validation.IsPort(port) || port == 0) return null;
        return new PhoneClient(s.Host, port, s.User, Password);
    }
}

public static class Validation
{
    public static bool IsHost(string host) => Uri.CheckHostName(host) != UriHostNameType.Unknown;

    public static bool IsPort(int port) => port is >= 0 and <= 65535;
}
