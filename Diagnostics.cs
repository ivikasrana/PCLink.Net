using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace PCLink.Net;

public static class Diagnostics
{
    public static async Task<List<(bool Ok, string Text)>> RunAsync()
    {
        var results = new List<(bool, string)>();
        var s = AppState.Settings;

        var tweaks = WebClientTweaks.Read();
        results.Add((tweaks.BasicOk, "Windows allows Basic login over http"));
        results.Add((tweaks.SizeOk, "Windows WebDAV file size limit raised"));
        results.Add((tweaks.RetryOk, "Windows retries the phone right away after it was offline"));
        var service = WindowsInfo.ServiceState("WebClient");
        results.Add((service == "Running", $"WebClient service: {service}"));

        if (string.IsNullOrWhiteSpace(s.Host))
        {
            results.Add((false, "No phone set up"));
            return results;
        }

        var ports = new[] { ("WebDAV", s.DavPort), ("HTTP", s.HttpPort), ("FTP", s.FtpPort) }
            .Where(p => p.Item2 > 0)
            .Select(async p => (await CanConnect(s.Host, p.Item2), $"{p.Item1} port {p.Item2} reachable"));
        results.AddRange(await Task.WhenAll(ports.ToList()));

        if (AppState.Password.Length == 0)
        {
            results.Add((false, "No password saved"));
            return results;
        }

        var logins = new List<Task<(bool, string)>>();
        if (s.HttpPort > 0) logins.Add(Api());
        if (s.DavPort > 0) logins.Add(WebDav(s));
        if (s.FtpPort > 0) logins.Add(Ftp(s));
        results.AddRange(await Task.WhenAll(logins));

        var found = await Discovery.FindAsync();
        results.Add((found.Any(p => s.PhoneId.Length == 0 || p.Id == s.PhoneId), $"Phone answers discovery ({found.Count} found)"));
        return results;
    }

    private static async Task<bool> CanConnect(string host, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(2000);
            await tcp.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<(bool, string)> Api()
    {
        try
        {
            using var client = AppState.NewClient();
            if (client is null) return (false, "Phone address or port is not valid");
            using var timeout = new CancellationTokenSource(3000);
            var info = await client.InfoAsync(timeout.Token);
            return (true, $"Login accepted by {info.Name}");
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return (false, "Phone rejected the username or password");
        }
        catch (Exception e)
        {
            return (false, $"HTTP API failed: {e.Message}");
        }
    }

    private static async Task<(bool, string)> WebDav(Settings s)
    {
        try
        {
            using var http = PhoneClient.CreateHttp(s.Host, s.DavPort, s.User, AppState.Password, TimeSpan.FromSeconds(4));
            using var request = new HttpRequestMessage(HttpMethod.Options, "/");
            using var response = await http.SendAsync(request);
            if ((int)response.StatusCode == 401) return (false, "WebDAV rejected the username or password");
            return (response.Headers.Contains("DAV"), $"WebDAV answers ({(int)response.StatusCode})");
        }
        catch (Exception e)
        {
            return (false, $"WebDAV failed: {e.Message}");
        }
    }

    private static async Task<(bool, string)> Ftp(Settings s)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(3000);
            await tcp.ConnectAsync(s.Host, s.FtpPort, timeout.Token);
            using var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8);
            using var writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            await reader.ReadLineAsync(timeout.Token);
            await writer.WriteLineAsync($"USER {s.User}");
            await reader.ReadLineAsync(timeout.Token);
            await writer.WriteLineAsync($"PASS {AppState.Password}");
            var answer = await reader.ReadLineAsync(timeout.Token) ?? "";
            return (answer.StartsWith("230"), answer.StartsWith("230") ? "FTP login accepted" : "FTP rejected the username or password");
        }
        catch (Exception e)
        {
            return (false, $"FTP failed: {e.Message}");
        }
    }
}
