using Microsoft.Win32;

namespace PCLink.Net;

public record TweakState(int? BasicAuthLevel, uint? FileSizeLimit, int? ServerNotFoundCache)
{
    public bool BasicOk => BasicAuthLevel == 2;
    public bool SizeOk => FileSizeLimit == uint.MaxValue;
    public bool RetryOk => ServerNotFoundCache == 0;
    public bool AllOk => BasicOk && SizeOk && RetryOk;
}

public static class WebClientTweaks
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\WebClient\Parameters";
    private const string Service = "WebClient";

    public static TweakState Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        var basic = key?.GetValue("BasicAuthLevel") is int b ? b : (int?)null;
        var size = key?.GetValue("FileSizeLimitInBytes") is int s ? unchecked((uint)s) : (uint?)null;
        var cache = key?.GetValue("ServerNotFoundCacheLifeTimeInSec") is int c ? c : (int?)null;
        return new TweakState(basic, size, cache);
    }

    public static bool TryApply(out string error)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
            key.SetValue("BasicAuthLevel", 2, RegistryValueKind.DWord);
            key.SetValue("FileSizeLimitInBytes", unchecked((int)uint.MaxValue), RegistryValueKind.DWord);
            key.SetValue("ServerNotFoundCacheLifeTimeInSec", 0, RegistryValueKind.DWord);
            return Restart(out error);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public static bool TryRestore(out string error)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
            key.DeleteValue("BasicAuthLevel", false);
            key.DeleteValue("FileSizeLimitInBytes", false);
            key.DeleteValue("ServerNotFoundCacheLifeTimeInSec", false);
            return Restart(out error);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private static bool Restart(out string error)
    {
        error = "";
        ProcessRunner.Run("sc.exe", $"config {Service} start= auto");
        ProcessRunner.Run("net.exe", $"stop {Service}");
        var start = ProcessRunner.Run("net.exe", $"start {Service}");
        if (start.Exit == 0) return true;
        error = start.Output.Trim();
        return false;
    }
}
