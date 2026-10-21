using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
        var service = WindowsInfo.ServiceState("WebClient");
        results.Add((service == "Running", $"WebClient service: {service}"));

        if (string.IsNullOrWhiteSpace(s.Host))
        {
            results.Add((false, "No phone set up"));
            return results;
        }

        foreach (var (name, port) in new[] { ("WebDAV", s.DavPort), ("HTTP", s.HttpPort), ("FTP", s.FtpPort) })
        {
            if (port <= 0) continue;
            results.Add((await CanConnect(s.Host, port), $"{name} port {port} reachable"));
        }

        if (AppState.Password.Length == 0)
        {
            results.Add((false, "No password saved"));
            return results;
        }

        if (s.HttpPort > 0) results.Add(await Api());
        if (s.DavPort > 0) results.Add(await WebDav(s));
        if (s.FtpPort > 0) results.Add(await Ftp(s));

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

    private static HttpClient NewHttp(Settings s, int port)
    {
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri($"http://{s.Host}:{port}/"), Timeout = TimeSpan.FromSeconds(4) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.User}:{AppState.Password}")));
        return http;
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
            using var http = NewHttp(s, s.DavPort);
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
