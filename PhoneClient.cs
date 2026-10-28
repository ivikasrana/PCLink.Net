using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PCLink.Net;

public record PhoneInfo(string Id, string Name, string Host, int Http, int Dav, int Ftp, string User);

public record RemoteItem(string Path, bool Dir, long Size, long Mtime);

public record PhoneNote(long Time, string App, string Title, string Text);

public sealed class PhoneClient : IDisposable
{
    public const string TempSuffix = ".pclinktmp";

    private readonly HttpClient http;
    private readonly string host;

    public PhoneClient(string host, int port, string user, string password)
    {
        this.host = host;
        http = CreateHttp(host, port, user, password, TimeSpan.FromMinutes(10));
    }

    public static HttpClient CreateHttp(string host, int port, string user, string password, TimeSpan timeout)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri($"http://{host}:{port}/"), Timeout = timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        return client;
    }

    public void Dispose() => http.Dispose();

    private static string Esc(string value) => Uri.EscapeDataString(value);

    private static string UrlPath(string remote) =>
        "/" + string.Join("/", remote.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        return document.RootElement.Clone();
    }

    public async Task<PhoneInfo> InfoAsync(CancellationToken ct)
    {
        var e = await GetJsonAsync("api/info", ct);
        return new PhoneInfo(
            e.GetProperty("id").GetString() ?? "",
            e.GetProperty("name").GetString() ?? "",
            host,
            e.GetProperty("http").GetInt32(),
            e.GetProperty("dav").GetInt32(),
            e.GetProperty("ftp").GetInt32(),
            e.GetProperty("user").GetString() ?? "");
    }

    public async Task<List<RemoteItem>> ManifestAsync(string remote, CancellationToken ct)
    {
        var e = await GetJsonAsync($"api/manifest?path={Esc(remote)}", ct);
        return e.GetProperty("items").EnumerateArray()
            .Select(i => new RemoteItem(i.GetProperty("path").GetString() ?? "", i.GetProperty("dir").GetBoolean(), i.GetProperty("size").GetInt64(), i.GetProperty("mtime").GetInt64()))
            .ToList();
    }

    public async Task UploadAsync(string local, string remote, long mtimeMs, IProgress<double>? progress, CancellationToken ct)
    {
        const int Chunk = 16 * 1024 * 1024;
        const int Attempts = 3;
        const int MaxResyncs = 5;
        var length = new FileInfo(local).Length;
        var offset = 0L;
        var status = await GetJsonAsync($"api/chunk?path={Esc(remote)}&total={length}&mtime={mtimeMs}", ct);
        var part = status.GetProperty("part").GetInt64();
        if (part > 0 && part < length) offset = part;
        await using var stream = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[(int)Math.Min(Chunk, Math.Max(length, 1))];
        var resyncs = 0;
        do
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var count = (int)Math.Min(Chunk, length - offset);
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count), ct);
            var url = $"api/chunk?path={Esc(remote)}&offset={offset}&total={length}&mtime={mtimeMs}";
            var advanced = false;
            for (var attempt = 0; attempt < Attempts && !advanced; attempt++)
            {
                var last = attempt == Attempts - 1;
                using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(buffer, 0, count) };
                try
                {
                    using var response = await http.SendAsync(request, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        offset += count;
                        advanced = true;
                        resyncs = 0;
                    }
                    else if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        if (++resyncs > MaxResyncs) throw new IOException("Phone keeps rejecting the upload offset");
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        offset = document.RootElement.GetProperty("part").GetInt64();
                        break;
                    }
                    else if (last) throw new HttpRequestException($"Phone answered {(int)response.StatusCode}", null, response.StatusCode);
                }
                catch (HttpRequestException) when (!last)
                {
                }
                if (!advanced) await Task.Delay(500 * (attempt + 1), ct);
            }
            progress?.Report(length == 0 ? 1 : (double)offset / length);
        }
        while (offset < length);
    }

    public async Task DownloadAsync(string remote, string local, long mtimeMs, CancellationToken ct)
    {
        using var response = await http.GetAsync(UrlPath(remote), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var temp = local + TempSuffix;
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, 256 * 1024, ct);
            }
            File.Move(temp, local, true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
        if (mtimeMs > 0) File.SetLastWriteTimeUtc(local, DateTimeOffset.FromUnixTimeMilliseconds(mtimeMs).UtcDateTime);
    }

    public async Task InstallApkAsync(string local, CancellationToken ct)
    {
        await using var stream = File.OpenRead(local);
        using var content = new StreamContent(stream);
        content.Headers.ContentLength = stream.Length;
        using var response = await http.PutAsync($"_install/{Esc(Path.GetFileName(local))}", content, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        using var response = await http.PostAsync("api/text", new StringContent(text, Encoding.UTF8, "text/plain"), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<string>> OutboxAsync(CancellationToken ct)
    {
        var e = await GetJsonAsync("api/outbox", ct);
        return e.EnumerateArray().Select(i => i.GetString() ?? "").ToList();
    }

    public async Task<List<PhoneNote>> NotificationsAsync(long since, CancellationToken ct)
    {
        var e = await GetJsonAsync($"api/notifications?since={since}", ct);
        return e.EnumerateArray()
            .Select(i => new PhoneNote(i.GetProperty("time").GetInt64(), i.GetProperty("app").GetString() ?? "", i.GetProperty("title").GetString() ?? "", i.GetProperty("text").GetString() ?? ""))
            .ToList();
    }
}
