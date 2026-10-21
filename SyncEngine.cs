using System.IO;

namespace PCLink.Net;

public static class SyncEngine
{
    private const long MtimeToleranceMs = 2000;

    private record Entry(long Size, long Mtime);

    private enum Step { None, Upload, Download }

    public static async Task<string> RunAsync(PhoneClient client, string local, string remote, IProgress<string> log, CancellationToken ct)
    {
        var root = Path.GetFullPath(local);
        Directory.CreateDirectory(root);
        var remoteFiles = (await client.ManifestAsync(remote, ct)).Where(i => !i.Dir).ToDictionary(i => i.Path, i => new Entry(i.Size, i.Mtime));
        var localFiles = ScanLocal(root);

        var up = 0;
        var down = 0;
        var same = 0;
        var skipped = 0;
        foreach (var key in localFiles.Keys.Union(remoteFiles.Keys).Order())
        {
            ct.ThrowIfCancellationRequested();
            localFiles.TryGetValue(key, out var l);
            remoteFiles.TryGetValue(key, out var r);
            var remotePath = remote.TrimEnd('/') + "/" + key;
            var localPath = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
            if (!localPath.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                log.Report($"Skip  {key}");
                skipped++;
                continue;
            }
            switch (Decide(l, r))
            {
                case Step.Upload:
                    log.Report($"Up    {key}");
                    await client.UploadAsync(localPath, remotePath, l!.Mtime, null, ct);
                    up++;
                    break;
                case Step.Download:
                    log.Report($"Down  {key}");
                    await client.DownloadAsync(remotePath, localPath, r!.Mtime, ct);
                    down++;
                    break;
                default:
                    same++;
                    break;
            }
        }
        var summary = $"Done: {up} sent, {down} received, {same} unchanged";
        return skipped > 0 ? $"{summary}, {skipped} skipped" : summary;
    }

    private static Dictionary<string, Entry> ScanLocal(string root)
    {
        var files = new Dictionary<string, Entry>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(PhoneClient.TempSuffix)) continue;
            var info = new FileInfo(file);
            files[Path.GetRelativePath(root, file).Replace('\\', '/')] = new Entry(info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
        }
        return files;
    }

    private static Step Decide(Entry? local, Entry? remote)
    {
        if (local is null) return remote is null ? Step.None : Step.Download;
        if (remote is null) return Step.Upload;
        var differs = local.Size != remote.Size || Math.Abs(local.Mtime - remote.Mtime) > MtimeToleranceMs;
        if (!differs) return Step.None;
        return local.Mtime > remote.Mtime ? Step.Upload : Step.Download;
    }
}
