using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

const string ModelId = "convaiinnovations/laya-multilingual";
const string Revision = "052592a15d198d9ad47da779604259b10b47b7aa";
const string BaseModelId = "jhu-clsp/mmBERT-base";
const string BaseRevision = "c5955035435e2bf121cde7f3c8863ef52ff35d82";
const string ModelSha256 = "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204";
const string TokenizerSha256 = "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f";
const long DefaultTimeoutSeconds = 1200;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
var packageDirectory = GetOption(args, "--package-dir") ?? Path.Combine(".artifacts", "models", "laya-mmbert");
var timeoutSeconds = ParseBoundedInt(GetOption(args, "--timeout-seconds"), 1, (int)DefaultTimeoutSeconds, (int)DefaultTimeoutSeconds);
var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
try
{
    switch (command)
    {
        case "download":
            await DownloadAsync(Path.GetFullPath(packageDirectory), cancellation.Token);
            break;
        case "manifest":
            WriteManifest(Path.GetFullPath(packageDirectory), cancellation.Token);
            break;
        case "verify":
            VerifyPackage(Path.GetFullPath(packageDirectory), cancellation.Token);
            break;
        default:
            PrintHelp();
            break;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("model-tool: operation cancelled or exceeded its bounded timeout");
    return 124;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"model-tool: {exception.Message}");
    return 1;
}
return 0;

static string? GetOption(string[] args, string name)
{
    for (var i = 1; i + 1 < args.Length; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static int ParseBoundedInt(string? value, int min, int max, int fallback)
    => int.TryParse(value, out var result) && result >= min && result <= max ? result : fallback;

static void PrintHelp()
{
    Console.WriteLine("Sezika.ModelTool commands:");
    Console.WriteLine("  download  download the pinned files with resume and SHA-256 checks");
    Console.WriteLine("  manifest  read SafeTensors metadata and write model.json/source_lock.json");
    Console.WriteLine("  verify    verify pinned file hashes and tensor ranges");
    Console.WriteLine("Options: --package-dir <path> --timeout-seconds <1..1200>");
}

static async Task DownloadAsync(string root, CancellationToken cancellationToken)
{
    Directory.CreateDirectory(root);
    var files = new (string Relative, string Sha256, long Size)[]
    {
        ("model.safetensors", ModelSha256, 643835514),
        ("tokenizer/tokenizer.json", TokenizerSha256, 34363188),
        ("encoder/config.json", "", 1938),
        ("rl_agent_config.json", "", 472),
        ("tokenizer/tokenizer_config.json", "", 502),
    };
    if (files.Length > 8) throw new InvalidOperationException("file bound exceeded");
    foreach (var file in files)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(Path.Combine(root, file.Relative));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var url = $"https://huggingface.co/{ModelId}/resolve/{Revision}/{file.Relative}";
        Console.WriteLine($"Downloading {file.Relative} (max {file.Size:N0} bytes)");
        await DownloadOneAsync(url, destination, file.Size, cancellationToken);
        if (!string.IsNullOrWhiteSpace(file.Sha256))
        {
            var actual = await HashFileAsync(destination, cancellationToken);
            if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 mismatch for {file.Relative}: {actual}");
        }
    }
}

static async Task DownloadOneAsync(string url, string destination, long expectedSize, CancellationToken cancellationToken)
{
    var existing = File.Exists(destination) ? new FileInfo(destination).Length : 0L;
    if (existing > expectedSize) throw new InvalidDataException($"Existing file is larger than expected: {destination}");
    Exception? firstFailure = null;
    for (var attempt = 1; attempt <= 2; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None };
            if (attempt == 2) handler.Proxy = new WebProxy("http://127.0.0.1:7890");
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            await using var target = new FileStream(destination, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            const long chunkBytes = 16L * 1024 * 1024;
            var chunkCount = 0;
            while (existing < expectedSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++chunkCount > 64) throw new InvalidDataException("bounded download chunk count exceeded");
                var end = Math.Min(expectedSize - 1, existing + chunkBytes - 1);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(existing, end);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    if (existing != 0) throw new InvalidDataException("server ignored bounded Range request");
                    if (response.Content.Headers.ContentLength is not null && response.Content.Headers.ContentLength > expectedSize) throw new InvalidDataException("Downloaded file exceeded pinned size");
                }
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                var remaining = end - existing + 1;
                var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                try
                {
                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                        if (count == 0) throw new EndOfStreamException("bounded range ended early");
                        await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        existing += count;
                        remaining -= count;
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            if (new FileInfo(destination).Length != expectedSize) throw new InvalidDataException($"Unexpected size for {destination}");
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
        {
            firstFailure ??= exception;
            if (attempt == 2) throw new IOException($"Download failed after direct/proxy attempts: {destination}", firstFailure);
            existing = File.Exists(destination) ? new FileInfo(destination).Length : 0L;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}

static void WriteManifest(string root, CancellationToken cancellationToken)
{
    var weights = Path.Combine(root, "model.safetensors");
    var tokenizer = Path.Combine(root, "tokenizer", "tokenizer.json");
    var encoderConfig = Path.Combine(root, "encoder", "config.json");
    var agentConfig = Path.Combine(root, "rl_agent_config.json");
    var tokenizerConfig = Path.Combine(root, "tokenizer", "tokenizer_config.json");
    foreach (var path in new[] { weights, tokenizer, encoderConfig, agentConfig, tokenizerConfig })
        if (!File.Exists(path)) throw new FileNotFoundException("Required model file is missing", path);
    var descriptors = ReadSafeTensorHeader(weights, cancellationToken);
    var fileHash = HashFile(weights, cancellationToken);
    var tokenizerHash = HashFile(tokenizer, cancellationToken);
    if (!fileHash.Equals(ModelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pinned model SHA-256 does not match");
    if (!tokenizerHash.Equals(TokenizerSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pinned tokenizer SHA-256 does not match");
    var tensorRecords = descriptors.Select(d => new Dictionary<string, object?>
    {
        ["name"] = d.Name, ["file"] = "model.safetensors", ["sha256"] = HashRange(weights, d.AbsoluteStart, d.ByteLength, cancellationToken),
        ["dtype"] = d.Dtype, ["shape"] = d.Shape, ["offsets"] = d.Offsets,
    }).ToArray();
    var layers = Enumerable.Range(0, 22).Select(i => new Dictionary<string, object?>
    {
        ["index"] = i,
        ["qkv"] = $"encoder.layers.{i}.attn.Wqkv.weight",
        ["attention_output"] = $"encoder.layers.{i}.attn.Wo.weight",
        ["attention_norm"] = i == 0 ? null : $"encoder.layers.{i}.attn_norm.weight",
        ["mlp_input"] = $"encoder.layers.{i}.mlp.Wi.weight",
        ["mlp_output"] = $"encoder.layers.{i}.mlp.Wo.weight",
        ["mlp_norm"] = $"encoder.layers.{i}.mlp_norm.weight",
    }).ToArray();
    var manifest = new Dictionary<string, object?>
    {
        ["schema_version"] = 1, ["model_id"] = ModelId, ["revision"] = Revision,
        ["architecture"] = "ModernBertForSequenceClassification", ["license"] = "Apache-2.0",
        ["source_url"] = $"https://huggingface.co/{ModelId}", ["source_revision"] = Revision,
        ["tokenizer_revision"] = Revision, ["tokenizer_file"] = "tokenizer/tokenizer.json", ["weights_file"] = "model.safetensors",
        ["weights_sha256"] = fileHash, ["tokenizer_sha256"] = tokenizerHash, ["dtype"] = "F16", ["tensor_layout"] = "row-major pytorch [out,in]",
        ["tensors"] = tensorRecords,
        ["tokenizer"] = new Dictionary<string, object?> { ["pipeline"] = "huggingface-tokenizers-json", ["normalizer"] = "Replace(space->▁)+Metaspace", ["pre_tokenizer"] = "Metaspace", ["model"] = "BPE", ["post_processor"] = "TemplateProcessing" },
        ["special_tokens"] = new Dictionary<string, int> { ["pad"] = 0, ["eos"] = 1, ["bos"] = 2, ["unk"] = 3, ["mask"] = 4, ["start_of_turn"] = 106, ["end_of_turn"] = 107 },
        ["encoder"] = new Dictionary<string, object?> { ["vocab_size"] = 256000, ["hidden_size"] = 768, ["intermediate_size"] = 1152, ["layer_count"] = 22, ["head_count"] = 12, ["max_tokens"] = 1024, ["global_attention_every"] = 3, ["local_attention"] = 128, ["global_rope_theta"] = 160000, ["local_rope_theta"] = 160000, ["norm_epsilon"] = 1e-5 },
        ["head"] = new Dictionary<string, object?> { ["hidden_size"] = 768, ["layers"] = 2, ["intermediate_size"] = 3072, ["max_tokens"] = 256 },
        ["calibration"] = new Dictionary<string, object?> { ["status"] = "uncalibrated", ["temperature"] = new[] { 1.0, 1.0, 1.0 } },
        ["tensor_mapping"] = new Dictionary<string, object?> { ["embedding"] = "encoder.embeddings.tok_embeddings.weight", ["embedding_norm"] = "encoder.embeddings.norm.weight", ["final_norm"] = "encoder.final_norm.weight", ["layers"] = layers },
    };
    WriteJson(Path.Combine(root, "model.json"), manifest);
    var lockFile = new Dictionary<string, object?>
    {
        ["model"] = new Dictionary<string, object?> { ["id"] = ModelId, ["revision"] = Revision, ["license"] = "Apache-2.0", ["source_url"] = $"https://huggingface.co/{ModelId}", ["weights_sha256"] = fileHash, ["tokenizer_sha256"] = tokenizerHash },
        ["base_encoder"] = new Dictionary<string, object?> { ["id"] = BaseModelId, ["revision"] = BaseRevision, ["license"] = "MIT", ["source_url"] = $"https://huggingface.co/{BaseModelId}" },
        ["files"] = new Dictionary<string, object?> { ["model.safetensors"] = fileHash, ["tokenizer/tokenizer.json"] = tokenizerHash, ["encoder/config.json"] = HashFile(encoderConfig, cancellationToken), ["rl_agent_config.json"] = HashFile(agentConfig, cancellationToken), ["tokenizer/tokenizer_config.json"] = HashFile(tokenizerConfig, cancellationToken) },
        ["generated_by"] = "Sezika.ModelTool",
    };
    WriteJson(Path.Combine(root, "source_lock.json"), lockFile);
    Console.WriteLine($"Wrote model.json with {descriptors.Count} tensor descriptors");
}

static void VerifyPackage(string root, CancellationToken cancellationToken)
{
    var model = Path.Combine(root, "model.safetensors");
    var tok = Path.Combine(root, "tokenizer", "tokenizer.json");
    if (!HashFile(model, cancellationToken).Equals(ModelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("model hash mismatch");
    if (!HashFile(tok, cancellationToken).Equals(TokenizerSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("tokenizer hash mismatch");
    var descriptors = ReadSafeTensorHeader(model, cancellationToken);
    Console.WriteLine($"Verified {descriptors.Count} tensors, model and tokenizer SHA-256");
}

static IReadOnlyList<TensorDescriptor> ReadSafeTensorHeader(string path, CancellationToken cancellationToken)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
    Span<byte> length = stackalloc byte[8]; stream.ReadExactly(length);
    var headerLength = BinaryPrimitives.ReadUInt64LittleEndian(length);
    if (headerLength is 0 or > 64 * 1024 * 1024 || headerLength > (ulong)(stream.Length - 8)) throw new InvalidDataException("SafeTensors header is outside bounded limits");
    var header = new byte[checked((int)headerLength)]; stream.ReadExactly(header);
    using var document = JsonDocument.Parse(header, new JsonDocumentOptions { MaxDepth = 32 });
    var descriptors = new List<TensorDescriptor>(document.RootElement.EnumerateObject().Count());
    foreach (var property in document.RootElement.EnumerateObject())
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (property.NameEquals("__metadata__")) continue;
        var value = property.Value;
        var dtype = value.GetProperty("dtype").GetString() ?? throw new InvalidDataException("missing dtype");
        var shape = value.GetProperty("shape").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var offsets = value.GetProperty("data_offsets").EnumerateArray().Select(v => v.GetUInt64()).ToArray();
        if (offsets.Length != 2 || offsets[1] <= offsets[0]) throw new InvalidDataException($"invalid offsets for {property.Name}");
        long elements = 1; foreach (var d in shape) elements = checked(elements * d);
        var width = dtype is "F16" or "BF16" ? 2 : dtype == "F32" ? 4 : 0;
        if (width == 0 || (ulong)checked(elements * width) != offsets[1] - offsets[0]) throw new InvalidDataException($"dtype/shape mismatch for {property.Name}");
        descriptors.Add(new TensorDescriptor(property.Name, dtype, shape, offsets, checked((long)(8 + headerLength + offsets[0])), checked((long)(offsets[1] - offsets[0]))));
    }
    var ordered = descriptors.OrderBy(d => d.AbsoluteStart).ToArray();
    for (var i = 1; i < ordered.Length; i++) if (ordered[i - 1].AbsoluteStart + ordered[i - 1].ByteLength > ordered[i].AbsoluteStart) throw new InvalidDataException("overlapping SafeTensors ranges");
    return descriptors;
}

static string HashFile(string path, CancellationToken cancellationToken)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
    try { int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); } return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(); }
    finally { ArrayPool<byte>.Shared.Return(buffer); }
}

static string HashRange(string path, long offset, long length, CancellationToken cancellationToken)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan); stream.Position = offset;
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024); var remaining = length;
    try { while (remaining > 0) { cancellationToken.ThrowIfCancellationRequested(); var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, read); remaining -= read; } return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(); }
    finally { ArrayPool<byte>.Shared.Return(buffer); }
}

static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken) => await Task.Run(() => HashFile(path, cancellationToken), cancellationToken);

static void WriteJson(string path, object value)
{
    var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    File.WriteAllText(path, JsonSerializer.Serialize(value, options) + Environment.NewLine);
}

readonly record struct TensorDescriptor(string Name, string Dtype, int[] Shape, ulong[] Offsets, long AbsoluteStart, long ByteLength);
