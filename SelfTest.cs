using System.IO;
using System.Security.Cryptography;

namespace PCLink.Net;

public static class SelfTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        var output = args[1];
        var lines = new List<string>();
        var failed = 0;
        void Check(bool ok, string text)
        {
            lines.Add((ok ? "PASS " : "FAIL ") + text);
            if (!ok) failed++;
        }

        var temp = Path.Combine(Path.GetTempPath(), "pclink-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var client = new PhoneClient(args[2], int.Parse(args[3]), args[4], args[5]);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = cts.Token;

            var info = await client.InfoAsync(ct);
            Check(info.Name.Length > 0 && info.Id.Length > 0, $"info: {info.Name} {info.Id}");

            var big = Path.Combine(temp, "up.bin");
            var data = new byte[40_000_000];
            Random.Shared.NextBytes(data);
            await File.WriteAllBytesAsync(big, data, ct);
            var steps = 0;
            await client.UploadAsync(big, "/Download/pcltest/up.bin", 1700000000000, new Progress<double>(_ => steps++), ct);
            var listed = await client.ManifestAsync("/Download/pcltest", ct);
            Check(listed.Any(i => i.Path == "up.bin" && i.Size == data.Length && i.Mtime == 1700000000000), $"chunked upload (40 MB in {steps} chunks, size and mtime kept)");

            var back = Path.Combine(temp, "down.bin");
            await client.DownloadAsync("/Download/pcltest/up.bin", back, 1700000000000, ct);
            Check(SHA256.HashData(await File.ReadAllBytesAsync(back, ct)).SequenceEqual(SHA256.HashData(data)), "download matches upload");

            var empty = Path.Combine(temp, "empty.txt");
            await File.WriteAllBytesAsync(empty, [], ct);
            await client.UploadAsync(empty, "/Download/pcltest/empty.txt", 0, null, ct);
            Check((await client.ManifestAsync("/Download/pcltest", ct)).Any(i => i.Path == "empty.txt" && i.Size == 0), "empty file upload");

            var remoteSync = "/Download/pcltest/sync-" + Guid.NewGuid().ToString("N")[..8];
            var local = Path.Combine(temp, "sync");
            Directory.CreateDirectory(Path.Combine(local, "sub"));
            await File.WriteAllTextAsync(Path.Combine(local, "a.txt"), "alpha", ct);
            await File.WriteAllTextAsync(Path.Combine(local, "sub", "b.txt"), "beta", ct);
            var log = new Progress<string>(_ => { });
            var first = await SyncEngine.RunAsync(client, local, remoteSync, log, ct);
            Check(first.Contains("2 sent, 0 received"), $"sync first run: {first}");
            var second = await SyncEngine.RunAsync(client, local, remoteSync, log, ct);
            Check(second.Contains("0 sent, 0 received, 2 unchanged"), $"sync second run: {second}");
            await client.UploadAsync(empty, remoteSync + "/c.txt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, ct);
            var third = await SyncEngine.RunAsync(client, local, remoteSync, log, ct);
            Check(third.Contains("0 sent, 1 received") && File.Exists(Path.Combine(local, "c.txt")), $"sync receives new phone file: {third}");

            if (args.Length > 6)
            {
                await client.InstallApkAsync(args[6], ct);
                lines.Add("INFO apk sent for install, confirm on the phone");
            }

            await client.SendTextAsync("selftest text", ct);
            Check((await client.OutboxAsync(ct)).Count == 0, "outbox empty");
            Check((await client.NotificationsAsync(0, ct)).Count >= 0, "notifications endpoint");

            var link = Pairing.SetupLink("pclink", "pa ss&word", 1099, 1199, 1299);
            var qr = Pairing.RenderQr(link);
            var pixels = new byte[qr.PixelWidth * qr.PixelHeight];
            qr.CopyPixels(pixels, qr.PixelWidth, 0);
            var reader = new ZXing.BarcodeReaderGeneric();
            var decoded = reader.Decode(new ZXing.RGBLuminanceSource(pixels, qr.PixelWidth, qr.PixelHeight, ZXing.RGBLuminanceSource.BitmapFormat.Gray8));
            Check(decoded?.Text == link, $"pairing qr {qr.PixelWidth}x{qr.PixelHeight} decodes back to the same link");

            var found = await Discovery.FindAsync(800);
            lines.Add($"INFO discovery found {found.Count} phones from this network");

            var state = WebClientTweaks.Read();
            lines.Add($"INFO registry basic={state.BasicAuthLevel} size={state.FileSizeLimit} windows={WindowsInfo.Describe()}");
        }
        catch (Exception e)
        {
            Check(false, "exception: " + e);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
        await File.WriteAllLinesAsync(output, lines);
        return failed == 0 ? 0 : 1;
    }
}
