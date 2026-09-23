using System.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Net.Sockets;
using Sezika;

namespace Sezika.Tests;

/// <summary>Small deterministic checks for the S1-04 install/lease/uninstall lifecycle.</summary>
public static class ModelPackageLifecycleChecks
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sezika-package-check-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "model.json"), "{\"schema_version\":1,\"model_id\":\"fixture/model\",\"revision\":\"r1\"}", Encoding.UTF8);
            Directory.CreateDirectory(Path.Combine(source, "tokenizer"));
            File.WriteAllText(Path.Combine(source, "tokenizer", "tokenizer.json"), "{}", Encoding.UTF8);
            var store = new ModelPackageStore(models);
            var record = store.InstallDirectoryAsync(source, "fixture/model", "r1").GetAwaiter().GetResult();
            var package = store.GetPackageDirectory("fixture/model", "r1");
            var inventoryMatches = record.Files.Length == 2 && record.Files.All(file =>
            {
                var path = Path.Combine(package, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path) && new FileInfo(path).Length == file.Length &&
                    file.Sha256.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparison.OrdinalIgnoreCase);
            });
            var staleStaging = package + ".staging-fixture";
            Directory.CreateDirectory(staleStaging);
            File.Copy(Path.Combine(package, "installation.json"), Path.Combine(staleStaging, "installation.json"));
            Ensure(inventoryMatches && store.ListInstalled().Count == 1 && ResumableDownloadCheck(root) && LoopbackRangeDownloadCheck(root) && EmptyAssetHashCheck(root) && DownloadBudgetCheck(root), "installation inventory records every package file and bounded HTTP Range resume is verified");
            Ensure(File.Exists(Path.Combine(package, "installation.json")), "installation record is durable");

            using (store.Acquire("fixture/model", "r1"))
            {
                ExpectCode(() => store.Uninstall("fixture/model", "r1"), "decision_session_busy");
                ExpectCode(() => store.InstallDirectoryAsync(source, "fixture/model", "r1").GetAwaiter().GetResult(), "decision_session_busy");
            }
            store.Uninstall("fixture/model", "r1");
            Ensure(!Directory.Exists(store.GetPackageDirectory("fixture/model", "r1")), "uninstall removes the package directory");
            return 4;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {message}");
    }

    private static void ExpectCode(Action action, string code)
    {
        try { action(); }
        catch (DecisionException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected {code}.");
    }

    private static bool ResumableDownloadCheck(string root)
    {
        var data = Encoding.UTF8.GetBytes("bounded-resume-fixture");
        var destination = Path.Combine(root, "download", "asset.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination + ".part", data[..3]);
        var handler = new RangeHandler(data);
        var downloader = new ModelAssetDownloader(
            new ModelDownloadOptions { ChunkBytes = 4096, MaxChunks = 2, MaxAttempts = 1, UseProxyFallback = false, Timeout = TimeSpan.FromSeconds(5) },
            _ => new HttpClient(handler));
        downloader.DownloadAsync(new ModelDownloadFile
        {
            RelativePath = "asset.bin",
            Source = new Uri("https://fixture.invalid/asset.bin"),
            ExpectedLength = data.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(data)),
        }, destination).GetAwaiter().GetResult();
        return handler.RequestCount == 1 && File.ReadAllBytes(destination).SequenceEqual(data) && !File.Exists(destination + ".part");
    }

    private static bool LoopbackRangeDownloadCheck(string root)
    {
        var data = Encoding.UTF8.GetBytes("loopback-http-range-fixture");
        var destination = Path.Combine(root, "loopback", "asset.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination + ".part", data[..4]);
        using var server = new LoopbackRangeServer(data);
        server.Start();
        var downloader = new ModelAssetDownloader(
            new ModelDownloadOptions { ChunkBytes = 4096, MaxChunks = 2, MaxAttempts = 1, UseProxyFallback = false, Timeout = TimeSpan.FromSeconds(5) });
        downloader.DownloadAsync(new ModelDownloadFile
        {
            RelativePath = "asset.bin",
            Source = server.Uri,
            ExpectedLength = data.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(data)),
        }, destination).GetAwaiter().GetResult();
        server.StopAndWait();
        return server.RequestCount == 1 && server.FirstRangeStart == 4 && File.ReadAllBytes(destination).SequenceEqual(data) && !File.Exists(destination + ".part");
    }

    private static bool EmptyAssetHashCheck(string root)
    {
        var destination = Path.Combine(root, "empty", "asset.bin");
        var downloader = new ModelAssetDownloader(
            new ModelDownloadOptions { ChunkBytes = 4096, MaxChunks = 1, MaxAttempts = 1, UseProxyFallback = false, Timeout = TimeSpan.FromSeconds(5) },
            _ => new HttpClient(new RangeHandler([])));
        downloader.DownloadAsync(new ModelDownloadFile
        {
            RelativePath = "asset.bin",
            Source = new Uri("https://fixture.invalid/empty.bin"),
            ExpectedLength = 0,
            Sha256 = Convert.ToHexString(SHA256.HashData([])),
        }, destination).GetAwaiter().GetResult();
        return File.Exists(destination) && new FileInfo(destination).Length == 0 && !File.Exists(destination + ".part");
    }

    private static bool DownloadBudgetCheck(string root)
    {
        var downloader = new ModelAssetDownloader(
            new ModelDownloadOptions { ChunkBytes = 4096, MaxChunks = 1, MaxAttempts = 1, UseProxyFallback = false, Timeout = TimeSpan.FromSeconds(5) },
            _ => new HttpClient(new RangeHandler([1])));
        try
        {
            downloader.DownloadAsync(new ModelDownloadFile
            {
                RelativePath = "oversized.bin",
                Source = new Uri("https://fixture.invalid/oversized.bin"),
                ExpectedLength = 4097,
            }, Path.Combine(root, "oversized", "asset.bin")).GetAwaiter().GetResult();
            return false;
        }
        catch (ModelPackageException exception) when (exception.Code == "decision_asset_size_invalid") { return true; }
    }

    private sealed class RangeHandler(byte[] data) : HttpMessageHandler
    {
        private readonly byte[] _data = data;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            var start = range?.From ?? 0;
            var end = range?.To ?? (_data.Length - 1);
            if (start < 0 || end < start || end >= _data.Length) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            var payload = _data[(int)start..checked((int)end + 1)];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(payload) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, _data.Length);
            return Task.FromResult(response);
        }
    }

    /// <summary>Small bounded HTTP/1.1 loopback server used to exercise HttpClient Range behavior.</summary>
    private sealed class LoopbackRangeServer(byte[] data) : IDisposable
    {
        private const int MaxRequests = 4;
        private readonly byte[] _data = data;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new(TimeSpan.FromSeconds(5));
        private Task? _serverTask;
        public Uri Uri { get; private set; } = null!;
        public int RequestCount { get; private set; }
        public long? FirstRangeStart { get; private set; }

        public void Start()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Uri = new Uri($"http://127.0.0.1:{endpoint.Port}/asset.bin");
            _serverTask = Task.Run(ServeAsync);
        }

        public void StopAndWait()
        {
            _cancellation.Cancel();
            _listener.Stop();
            if (_serverTask is not null && !_serverTask.Wait(TimeSpan.FromSeconds(2)))
                throw new InvalidOperationException("Loopback range server did not stop within its bound.");
        }

        private async Task ServeAsync()
        {
            try
            {
                for (var requestIndex = 0; requestIndex < MaxRequests; requestIndex++)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    var rangeStart = 0L;
                    var rangeEnd = _data.Length - 1L;
                    var line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                    while ((line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false)) is not null && line.Length != 0)
                    {
                        if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = line[6..].Trim();
                            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected Range unit.");
                            var bounds = value[6..].Split('-', 2);
                            rangeStart = long.Parse(bounds[0], System.Globalization.CultureInfo.InvariantCulture);
                            if (bounds.Length == 2 && bounds[1].Length != 0) rangeEnd = long.Parse(bounds[1], System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    if (line is null) throw new InvalidOperationException("HTTP request headers were incomplete.");
                    if (rangeStart < 0 || rangeEnd < rangeStart || rangeEnd >= _data.Length) throw new InvalidOperationException("HTTP Range exceeded fixture bounds.");
                    RequestCount++;
                    FirstRangeStart ??= rangeStart;
                    var payload = _data[(int)rangeStart..checked((int)rangeEnd + 1)];
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 206 Partial Content\r\nContent-Length: {payload.Length}\r\nContent-Range: bytes {rangeStart}-{rangeEnd}/{_data.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _cancellation.Token).ConfigureAwait(false);
                    await stream.WriteAsync(payload, _cancellation.Token).ConfigureAwait(false);
                    await stream.FlushAsync(_cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            StopAndWait();
            _cancellation.Dispose();
        }
    }
}
