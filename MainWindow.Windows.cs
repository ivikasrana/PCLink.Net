using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCLink.Net;

public partial class MainWindow
{
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
        RetryText.Text = state.ServerNotFoundCache switch
        {
            0 => "Right away",
            null => "After 60 seconds (Windows default)",
            var seconds => $"After {seconds} seconds",
        };
        Paint(RetryText, state.RetryOk);
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
