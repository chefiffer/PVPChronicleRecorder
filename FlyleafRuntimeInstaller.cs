using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace PvpChronicleRecorder;

/// <summary>
/// Installs the official Flyleaf FFmpeg build only after an explicit user action.  The release
/// URL, size and digest are pinned so a changed remote asset is never executed silently.
/// </summary>
internal sealed class FlyleafRuntimeInstaller
{
    internal const string ReleaseUrl =
        "https://github.com/SuRGeoNix/Flyleaf/releases/download/v3.11.8/Flyleaf_v3.11.8.7z";
    internal const long ReleaseSize = 44_833_957;
    internal const string ReleaseSha256 =
        "70bead90305f2293fd15a9f147d0f6962d6796579aa40a8307dc3831864eb095";

    private readonly string configDirectory;
    private readonly Configuration configuration;
    private readonly Action save;
    private readonly IPluginLog log;
    private int installing;
    private string statusKey = string.Empty;
    private string statusDetail = string.Empty;
    private double downloadProgress;
    private double verifyProgress;
    private double extractProgress;

    public FlyleafRuntimeInstaller(string configDirectory, Configuration configuration,
        Action save, IPluginLog log)
    {
        this.configDirectory = configDirectory;
        this.configuration = configuration;
        this.save = save;
        this.log = log;
    }

    public bool Installing => Volatile.Read(ref installing) != 0;
    public string Status
    {
        get
        {
            var key = Volatile.Read(ref statusKey);
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var detail = Volatile.Read(ref statusDetail);
            return string.IsNullOrEmpty(detail) ? RecorderLocalization.T(key) : RecorderLocalization.F(key, detail);
        }
    }
    public double DownloadProgress => Volatile.Read(ref downloadProgress);
    public double VerifyProgress => Volatile.Read(ref verifyProgress);
    public double ExtractProgress => Volatile.Read(ref extractProgress);

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref installing, 1, 0) != 0) return;
        Volatile.Write(ref downloadProgress, 0);
        Volatile.Write(ref verifyProgress, 0);
        Volatile.Write(ref extractProgress, 0);
        var work = Path.Combine(configDirectory, "runtime-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var archivePath = Path.Combine(work, "Flyleaf_v3.11.8.7z");
            SetStatus("runtimeDownloading");
            await DownloadAsync(archivePath, (done, total) =>
                Volatile.Write(ref downloadProgress, Ratio(done, total)), cancellationToken)
                .ConfigureAwait(false);

            SetStatus("runtimeVerifying");
            await VerifyAsync(archivePath, (done, total) =>
                Volatile.Write(ref verifyProgress, Ratio(done, total)), cancellationToken)
                .ConfigureAwait(false);

            var extracted = Path.Combine(work, "FFmpeg");
            Directory.CreateDirectory(extracted);
            SetStatus("runtimeExtracting");
            await ExtractFfmpegAsync(archivePath, extracted, (done, total) =>
                    Volatile.Write(ref extractProgress, Ratio(done, total)), cancellationToken)
                .ConfigureAwait(false);
            EnsureRuntimeLooksComplete(extracted);

            var runtimeRoot = Path.Combine(configDirectory, "playback-runtime");
            Directory.CreateDirectory(runtimeRoot);
            var destination = Path.Combine(runtimeRoot, "FFmpeg");
            if (Directory.Exists(destination))
            {
                var backup = Path.Combine(runtimeRoot,
                    "FFmpeg.previous-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
                Directory.Move(destination, backup);
            }
            Directory.Move(extracted, destination);
            configuration.FlyleafFfmpegPath = destination;
            save();
            SetStatus("runtimeInstalled");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Flyleaf playback runtime installation failed");
            SetStatus("runtimeFailed", ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            Volatile.Write(ref installing, 0);
        }
    }

    private static async Task DownloadAsync(string destination, Action<long, long> progress,
        CancellationToken token)
    {
        try
        {
            await DownloadAttemptAsync(destination, true, progress, token).ConfigureAwait(false);
        }
        catch when (!token.IsCancellationRequested)
        {
            // A stale localhost proxy is a common OBS/FFXIV setup failure. Retry the same pinned
            // HTTPS asset directly; the size and SHA256 checks still run before extraction.
            progress(0, ReleaseSize);
            await DownloadAttemptAsync(destination, false, progress, token).ConfigureAwait(false);
        }
    }

    private static async Task DownloadAttemptAsync(string destination, bool useProxy,
        Action<long, long> progress, CancellationToken token)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseProxy = useProxy
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PvpChronicleRecorder");
        using var response = await client.GetAsync(ReleaseUrl,
            HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != ReleaseSize)
            throw new InvalidDataException($"官方运行库大小异常：{length}，预期 {ReleaseSize}");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        long completed = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
                       .ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                completed += read;
                progress(completed, ReleaseSize);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        await output.FlushAsync(token).ConfigureAwait(false);
        progress(completed, ReleaseSize);
    }

    private static async Task VerifyAsync(string archivePath, Action<long, long> progress,
        CancellationToken token)
    {
        var info = new FileInfo(archivePath);
        if (info.Length != ReleaseSize)
            throw new InvalidDataException($"下载不完整：{info.Length}，预期 {ReleaseSize}");
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        long completed = 0;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
                       .ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                completed += read;
                progress(completed, info.Length);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(digest),
                System.Text.Encoding.ASCII.GetBytes(ReleaseSha256)))
            throw new InvalidDataException("官方运行库 SHA256 校验失败，文件已被改变或下载损坏");
    }

    private static async Task ExtractFfmpegAsync(string archivePath, string destination,
        Action<long, long> progress, CancellationToken token)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        var entries = archive.Entries.Where(value => !value.IsDirectory &&
            IsFfmpegEntry(value.Key)).ToArray();
        var totalBytes = Math.Max(1, entries.Sum(value => Math.Max(0, value.Size)));
        long completedBytes = 0;
        var extracted = 0;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var key = (entry.Key ?? string.Empty).Replace('\\', '/');
            var marker = key.IndexOf("/FFmpeg/", StringComparison.OrdinalIgnoreCase);
            var relative = marker >= 0 ? key[(marker + "/FFmpeg/".Length)..] :
                key.StartsWith("FFmpeg/", StringComparison.OrdinalIgnoreCase) ? key["FFmpeg/".Length..] :
                string.Empty;
            if (string.IsNullOrWhiteSpace(relative)) continue;
            var target = Path.GetFullPath(Path.Combine(destination,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("运行库压缩包包含越界路径");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.OpenEntryStream();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, 128 * 1024, token).ConfigureAwait(false);
            completedBytes += Math.Max(0, entry.Size);
            progress(completedBytes, totalBytes);
            extracted++;
        }
        if (extracted == 0) throw new InvalidDataException("官方压缩包中没有找到 FFmpeg 目录");
    }

    private static bool IsFfmpegEntry(string? value)
    {
        var key = (value ?? string.Empty).Replace('\\', '/');
        return key.Contains("/FFmpeg/", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("FFmpeg/", StringComparison.OrdinalIgnoreCase);
    }

    private static double Ratio(long completed, long total) =>
        total <= 0 ? 0 : Math.Clamp(completed / (double)total, 0, 1);

    private void SetStatus(string key, string detail = "")
    {
        Volatile.Write(ref statusDetail, detail);
        Volatile.Write(ref statusKey, key);
    }

    private static void EnsureRuntimeLooksComplete(string directory)
    {
        foreach (var prefix in new[] { "avcodec-", "avformat-", "avutil-", "swresample-", "swscale-" })
        {
            if (!Directory.EnumerateFiles(directory, prefix + "*.dll", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("FFmpeg 运行库缺少 " + prefix + "*.dll");
        }
    }
}
