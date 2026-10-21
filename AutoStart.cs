using Microsoft.Win32;

namespace PCLink.Net;

public static class AutoStart
{
    private const string Name = "PCLink.Net";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null) return;
        if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue(Name, false);
    }
}
