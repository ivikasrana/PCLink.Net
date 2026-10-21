using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PCLink.Net;

public static class DriveMapper
{
    private const int TypeDisk = 1;
    private const int UpdateProfile = 1;
    private const int SaveCredentials = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource resource, string? password, string? user, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    public static IEnumerable<string> FreeLetters()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var c = 'Z'; c >= 'D'; c--)
            if (!used.Contains(c)) yield return c + ":";
    }

    public static string? Map(string drive, string host, int port, string user, string password)
    {
        WNetCancelConnection2(drive, 0, true);
        var resource = new NetResource
        {
            Type = TypeDisk,
            LocalName = drive,
            RemoteName = $@"\\{host}@{port}\DavWWWRoot",
        };
        var code = WNetAddConnection2(ref resource, password, user, UpdateProfile | SaveCredentials);
        return code == 0 ? null : new Win32Exception(code).Message;
    }

    public static string? Unmap(string drive)
    {
        var code = WNetCancelConnection2(drive, UpdateProfile, true);
        return code == 0 ? null : new Win32Exception(code).Message;
    }
}
