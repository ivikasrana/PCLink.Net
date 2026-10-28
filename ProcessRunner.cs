using System.Diagnostics;

namespace PCLink.Net;

public static class ProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static (int Exit, string Output) Run(string file, string args)
    {
        var info = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            try { process.Kill(true); }
            catch (InvalidOperationException) { }
            return (-1, $"{file} did not finish within {Timeout.TotalSeconds:0} seconds");
        }
        return (process.ExitCode, output.Result + error.Result);
    }
}
