using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.Tooling;

// Fixture-only HTTP basic LFS endpoint. No file/standalone/custom adapter.
internal sealed class NativeLfsServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop;
    private readonly Task serving;
    private readonly Dictionary<string, string> repositories = new(StringComparer.Ordinal);
    private readonly List<Task> handlers = [];
    private readonly List<string> errors = [];
    private readonly List<string> actualRequests = [];
    private readonly SemaphoreSlim slots = new(32, 32);
    private int requests;
    private int active;
    private int batchResponses;
    private int uploadResponses;
    private int downloadResponses;
    private int missingObjects;
    private int unsupportedLockResponses;
    internal int Port { get; }
    internal int Requests => requests;
    internal int BatchResponses => batchResponses;
    internal int UploadResponses => uploadResponses;
    internal int DownloadResponses => downloadResponses;
    internal int MissingObjects => missingObjects;
    internal int UnsupportedLockResponses => unsupportedLockResponses;
    internal string[] ActualRequests { get { lock (actualRequests) return actualRequests.ToArray(); } }
    internal string[] Errors { get { lock (errors) return errors.ToArray(); } }
    internal Func<NativeHttpRequest, CancellationToken, Task<NativeHttpResponse>>? GitHandler { get; set; }
    internal Func<NativeHttpRequest, CancellationToken, Task<NativeHttpResponse>>? ControlHandler { get; set; }

    internal NativeLfsServer(CancellationToken cancellation)
    {
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stop.CancelAfter(TimeSpan.FromSeconds(240));
        listener.Start(32); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        serving = Serve();
    }

    internal string Register(string name, string bare, bool future = false)
    {
        if (name.Length is < 1 or > 64 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || !Path.IsPathFullyQualified(bare) || (!future && !Directory.Exists(bare)))
            throw new InvalidDataException("Explicit fixture scope and admitted bare directory required.");
        SafePaths.NoLinks(bare, new Budget(5, null, stop.Token));
        lock (repositories)
        {
            if (repositories.Count >= 6 || !repositories.TryAdd(name, bare)) throw new InvalidDataException("LFS fixture repository bound6 or duplicate scope.");
        }
        return $"http://127.0.0.1:{Port}/lfs/{name}";
    }

    private async Task Serve()
    {
        try
        {
            for (var accepted = 0; accepted < 256; accepted++)
            {
                await slots.WaitAsync(stop.Token).ConfigureAwait(false);
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref requests); Interlocked.Increment(ref active);
                    var task = Handle(client); client = null; handlers.Add(task);
                    if (requests % 16 == 0) Console.Error.WriteLine($"native-lfs-fixture requests={requests}/256 active={active}/32");
                }
                catch { client?.Dispose(); slots.Release(); throw; }
            }
            lock (errors) errors.Add("LFS fixture request bound256 exhausted.");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (stop.IsCancellationRequested && error is SocketException or ObjectDisposedException) { }
        catch (Exception error) { lock (errors) errors.Add(error.GetType().Name + ": " + error.Message); }
        finally { listener.Stop(); }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var token = deadline.Token; var stream = client.GetStream();
                var header = new List<byte>(); var single = new byte[1]; var matched = 0;
                for (var read = 0; read < 16384; read++)
                {
                    if (await stream.ReadAsync(single, token).ConfigureAwait(false) != 1) throw new EndOfStreamException("HTTP header EOF.");
                    header.Add(single[0]);
                    matched = single[0] == "\r\n\r\n"[matched] ? matched + 1 : single[0] == '\r' ? 1 : 0;
                    if (matched == 4) break;
                }
                if (matched != 4) throw new InvalidDataException("HTTP header bound16KiB.");
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var request = lines[0].Split(' ');
                if (request.Length != 3 || request[2] != "HTTP/1.1" || request[1].Length > 256) throw new InvalidDataException("HTTP request line differs.");
                lock (actualRequests)
                {
                    if (actualRequests.Count >= 256) throw new InvalidDataException("HTTP request trace bound256.");
                    actualRequests.Add(request[0] + " " + request[1]);
                }
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1))
                {
                    var colon = line.IndexOf(':');
                    if (colon <= 0 || !values.TryAdd(line[..colon], line[(colon + 1)..].Trim())) throw new InvalidDataException("Duplicate/malformed HTTP header.");
                }
                if (!values.TryGetValue("Host", out var host) || host != "127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("HTTP fixture host/explicit port differs.");
                if (values.ContainsKey("Transfer-Encoding")) throw new InvalidDataException("Fixture requires bounded Content-Length.");
                var length = values.TryGetValue("Content-Length", out var raw) ? int.Parse(raw, CultureInfo.InvariantCulture) : 0;
                if (length is < 0 or > 1048576) throw new InvalidDataException("HTTP body bound1MiB.");
                if (values.TryGetValue("Expect", out var expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                    await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
                var body = new byte[length]; var offset = 0;
                for (var read = 0; read < 1025 && offset < body.Length; read++)
                {
                    var size = await stream.ReadAsync(body.AsMemory(offset), token).ConfigureAwait(false);
                    if (size == 0) throw new EndOfStreamException("HTTP body EOF."); offset += size;
                }
                if (offset != body.Length) throw new InvalidDataException("HTTP body read bound1025.");
                if (request[1].StartsWith("/fixture/", StringComparison.Ordinal) && ControlHandler is { } control)
                {
                    var response = await control(new(request[0], request[1], values.GetValueOrDefault("Content-Type"), values.GetValueOrDefault("Content-Encoding"), values.GetValueOrDefault("Git-Protocol"), body), token).ConfigureAwait(false);
                    await Respond(stream, response.Status, response.ContentType, response.Body, token).ConfigureAwait(false);
                    return;
                }
                if (request[1].StartsWith("/git/", StringComparison.Ordinal) && GitHandler is { } git)
                {
                    var response = await git(new(request[0], request[1], values.GetValueOrDefault("Content-Type"), values.GetValueOrDefault("Content-Encoding"), values.GetValueOrDefault("Git-Protocol"), body), token).ConfigureAwait(false);
                    await Respond(stream, response.Status, response.ContentType, response.Body, token).ConfigureAwait(false);
                    return;
                }
                var pieces = request[1].Split('/', StringSplitOptions.RemoveEmptyEntries);
                string? bare;
                lock (repositories) repositories.TryGetValue(pieces.Length > 1 ? pieces[1] : "", out bare);
                if (bare is not null)
                {
                    SafePaths.NoLinks(bare, new Budget(5, null, token));
                    if (!Directory.Exists(bare)) throw new InvalidDataException("Admitted future LFS repository has not actually been created.");
                }
                if (pieces.Length == 4 && pieces[0] == "lfs" && pieces[2] == "locks" && pieces[3] == "verify" && bare is not null && request[0] == "POST")
                {
                    // Standard LFS capability absence: this fixture provides
                    // basic transport, not a locking service. Preserve actual
                    // request/response counts; never invent an empty lock DB.
                    await Respond(stream, 404, "application/vnd.git-lfs+json", "{\"message\":\"Fixture locking API not implemented\"}"u8.ToArray(), token).ConfigureAwait(false);
                    Interlocked.Increment(ref unsupportedLockResponses); return;
                }
                if (pieces.Length != 4 || pieces[0] != "lfs" || pieces[2] != "objects" || bare is null)
                    throw new InvalidDataException("Unknown LFS fixture scope/path: " + request[0] + " " + request[1]);
                if (request[0] == "POST" && pieces[3] == "batch")
                {
                    using var batch = JsonDocument.Parse(body); var root = batch.RootElement;
                    var operation = root.GetProperty("operation").GetString(); var objects = root.GetProperty("objects");
                    if (operation is not ("upload" or "download") || objects.ValueKind != JsonValueKind.Array || objects.GetArrayLength() is < 1 or > 16)
                        throw new InvalidDataException("LFS batch operation/object bound16.");
                    var result = new List<NativeLfsObject>(); var missing = 0;
                    foreach (var item in objects.EnumerateArray())
                    {
                        var oid = item.GetProperty("oid").GetString()!; var bytes = item.GetProperty("size").GetInt64(); ValidateObject(oid, bytes);
                        var path = ObjectPath(bare, oid, token); var exists = File.Exists(path);
                        if (exists) Verify(path, oid, bytes, token);
                        var href = $"http://127.0.0.1:{Port}/lfs/{pieces[1]}/objects/{oid}";
                        if (operation == "download" && !exists) missing++;
                        result.Add(operation == "download" && !exists ? new(oid, bytes, Error: new(404, "Fixture object missing")) :
                            new(oid, bytes, operation == "download" ? new(Download: new(href)) : exists ? null : new(Upload: new(href))));
                    }
                    await Respond(stream, 200, "application/vnd.git-lfs+json", JsonSerializer.SerializeToUtf8Bytes(new NativeLfsBatch("basic", result.ToArray()), NativeLfsJson.Default.NativeLfsBatch), token).ConfigureAwait(false);
                    Interlocked.Increment(ref batchResponses);
                    Interlocked.Add(ref missingObjects, missing);
                }
                else
                {
                    var oid = pieces[3]; ValidateObject(oid, body.Length); var path = ObjectPath(bare, oid, token);
                    if (request[0] == "PUT")
                    {
                        if (Convert.ToHexStringLower(SHA256.HashData(body)) != oid) throw new InvalidDataException("Actual upload SHA differs.");
                        if (File.Exists(path)) Verify(path, oid, body.Length, token);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!); var pending = path + ".fixture-" + Guid.NewGuid().ToString("N") + ".pending";
                            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await file.WriteAsync(body, token).ConfigureAwait(false); file.Flush(true); }
                            File.Move(pending, path, false); Verify(path, oid, body.Length, token);
                        }
                        await Respond(stream, 200, "application/octet-stream", [], token).ConfigureAwait(false);
                        Interlocked.Increment(ref uploadResponses);
                    }
                    else if (request[0] == "GET" && File.Exists(path))
                    {
                        var bytes = new FileInfo(path).Length; ValidateObject(oid, bytes); Verify(path, oid, bytes, token);
                        await Respond(stream, 200, "application/octet-stream", await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), token).ConfigureAwait(false);
                        Interlocked.Increment(ref downloadResponses);
                    }
                    else await Respond(stream, 404, "text/plain", "Fixture object missing"u8.ToArray(), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception error) { lock (errors) errors.Add(error.GetType().Name + ": " + error.Message); }
            finally { Interlocked.Decrement(ref active); slots.Release(); }
        }
    }

    private static string ObjectPath(string bare, string oid, CancellationToken cancellation) =>
        SafePaths.Within(bare, "lfs/objects/" + oid[..2] + "/" + oid.Substring(2, 2) + "/" + oid, new Budget(5, null, cancellation));
    private static void ValidateObject(string oid, long bytes)
    {
        if (oid.Length != 64 || oid.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) || bytes is < 0 or > 1048576) throw new InvalidDataException("LFS object SHA/bytes bound1MiB.");
    }
    private static void Verify(string path, string oid, long bytes, CancellationToken cancellation)
    {
        var budget = new Budget(5, null, cancellation); RegularFiles.Require(path, budget);
        using var file = File.OpenRead(path);
        if (file.Length != bytes || budget.Hash(file, bytes) != oid) throw new InvalidDataException("Actual fixture object byte/SHA readback differs.");
    }
    private static async Task Respond(NetworkStream stream, int status, string type, byte[] body, CancellationToken cancellation)
    {
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false); await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
    }
    public void Dispose()
    {
        stop.Cancel(); listener.Stop(); serving.GetAwaiter().GetResult();
        Task.WhenAll(handlers).GetAwaiter().GetResult();
        if (active != 0) throw new IOException("LFS fixture handlers remain active.");
        slots.Dispose(); stop.Dispose();
    }
}

internal sealed record NativeLfsAction([property: JsonPropertyName("href")] string Href);
internal sealed record NativeLfsActions([property: JsonPropertyName("upload")] NativeLfsAction? Upload = null, [property: JsonPropertyName("download")] NativeLfsAction? Download = null);
internal sealed record NativeLfsError([property: JsonPropertyName("code")] int Code, [property: JsonPropertyName("message")] string Message);
internal sealed record NativeLfsObject([property: JsonPropertyName("oid")] string Oid, [property: JsonPropertyName("size")] long Size, [property: JsonPropertyName("actions")] NativeLfsActions? Actions = null, [property: JsonPropertyName("error")] NativeLfsError? Error = null);
internal sealed record NativeLfsBatch([property: JsonPropertyName("transfer")] string Transfer, [property: JsonPropertyName("objects")] NativeLfsObject[] Objects);
internal sealed record NativeLfsReceipt(string Status, int Port, int Requests, int BatchResponses, int UploadResponses, int DownloadResponses, int MissingObjects, int UnsupportedLockResponses, string[] ActualRequests, string[] Errors);
[JsonSerializable(typeof(NativeLfsBatch))]
[JsonSerializable(typeof(NativeLfsReceipt))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class NativeLfsJson : JsonSerializerContext;
