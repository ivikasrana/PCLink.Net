using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PCLink.Net;

public static class WindowsInfo
{
    public static string Describe()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var build = key?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
        var display = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? "";
        var ubr = key?.GetValue("UBR");
        if (int.TryParse(build, out var number) && number >= 22000) product = product.Replace("Windows 10", "Windows 11");
        var buildText = ubr is null ? build : $"{build}.{ubr}";
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        return $"{product} {display} (build {buildText}, {arch})".Replace("  ", " ");
    }

    public static string ServiceState(string service)
    {
        var result = ProcessRunner.Run("sc.exe", $"query {service}");
        if (result.Output.Contains("1060")) return "Not installed";
        if (result.Output.Contains("RUNNING")) return "Running";
        if (result.Output.Contains("STOPPED")) return "Stopped";
        return "Unknown";
    }
}
