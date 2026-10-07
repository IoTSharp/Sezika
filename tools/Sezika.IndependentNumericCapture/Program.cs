using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sezika;

namespace Sezika.IndependentNumericCapture;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("Sezika.IndependentNumericCapture capture --package <converted-package> --output <new-directory> [--source-config <original-config.json>] [--timeout-seconds 1..120] [--cancel-file <file>]");
            Console.WriteLine("Sezika.IndependentNumericCapture self-test --output <new-directory> [--maximum-cases 1..8] [--timeout-seconds 1..20]");
            return 0;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        ConsoleCancelEventHandler handler = (_, value) => { value.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var options = Options.Parse(args);
            cancellation.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var budget = new Budget(options.TimeoutSeconds, cancellation.Token, options.CancelFile);
            if (options.Command == "self-test") return SelfTest.Run(options, budget);
            Capture.Run(options, budget);
            Console.WriteLine($"cpu_capture_complete: {options.Output}; cases=1, tokens=46, checkpoints=267; upstream parity and quality remain unmeasured");
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("capture cancelled or deadline exceeded; no complete output is published"); return 124; }
        catch (OutOfMemoryException) { Console.Error.WriteLine("capture memory allocation failed; retained evidence is incomplete"); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or
            InvalidOperationException or JsonException or KeyNotFoundException or OverflowException or DecisionException)
        { Console.Error.WriteLine($"capture failed: {error.Message}"); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}

internal sealed record Options(string Command, string Output, string? Package, string? SourceConfig,
    int TimeoutSeconds, string? CancelFile, int MaximumCases)
{
    internal static Options Parse(IReadOnlyList<string> args)
    {
        Require.That(args.Count is >= 3 and <= 11 && args.Count % 2 == 1 && args[0] is "capture" or "self-test", "Invalid bounded command.");
        var watch = Stopwatch.StartNew();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var permitted = args[0] == "capture"
            ? new HashSet<string>(StringComparer.Ordinal) { "--package", "--output", "--source-config", "--timeout-seconds", "--cancel-file" }
            : new HashSet<string>(StringComparer.Ordinal) { "--output", "--maximum-cases", "--timeout-seconds" };
        for (var index = 1; index < args.Count; index += 2)
        {
            Require.That(watch.Elapsed < TimeSpan.FromSeconds(5), "Option parsing deadline exceeded.");
            Require.That(index + 1 < args.Count && permitted.Contains(args[index]) && args[index + 1].Length <= 4096 && !string.IsNullOrWhiteSpace(args[index + 1]) &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal) && values.TryAdd(args[index], args[index + 1]), "Unknown, duplicate or missing option.");
        }
        string Needed(string name) => values.TryGetValue(name, out var value) ? value : throw new InvalidDataException($"Missing {name}.");
        int Number(string name, int maximum, int fallback)
        {
            if (!values.TryGetValue(name, out var text)) return fallback;
            Require.That(int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= maximum, $"Invalid {name}.");
            return number;
        }
        var package = args[0] == "capture" ? Path.GetFullPath(Needed("--package")) : null;
        var config = package is null ? null : Path.GetFullPath(values.TryGetValue("--source-config", out var supplied)
            ? supplied : Path.Combine(package, "..", "source", "config.json"));
        return new(args[0], Path.GetFullPath(Needed("--output")), package, config,
            Number("--timeout-seconds", package is null ? 20 : 120, package is null ? 20 : 120),
            values.TryGetValue("--cancel-file", out var cancel) ? Path.GetFullPath(cancel) : null,
            Number("--maximum-cases", 8, 8));
    }
}

internal static class Require
{
    internal static void That(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}

internal sealed class Budget : IDisposable
{
    internal const long MaximumResidentBytes = 4L * 1024 * 1024 * 1024;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly string? _cancelFile;
    private readonly int _seconds;
    private readonly bool _enforceResident;
    private double _nextResidentObservationSeconds;
    // Internal synthetic sensor; never set by capture/options and never allocates memory.
    internal static long SyntheticPeakForSelfTest { get; set; }
    internal CancellationToken Token { get; }
    internal double ElapsedSeconds => _watch.Elapsed.TotalSeconds;
    internal Budget(int seconds, CancellationToken token, string? cancelFile = null, bool enforceResident = true)
    { _seconds = seconds; Token = token; _cancelFile = cancelFile; _enforceResident = enforceResident; }
    internal void Check()
    {
        Token.ThrowIfCancellationRequested();
        if (_watch.Elapsed.TotalSeconds >= _seconds || (_cancelFile is not null && File.Exists(_cancelFile)))
            throw new OperationCanceledException("Capture deadline or cancellation marker reached.", Token);
        if (_enforceResident)
        {
            Require.That(SyntheticPeakForSelfTest <= MaximumResidentBytes,
                "Observed process resident memory exceeded 4 GiB; capture is incomplete.");
            // Keep cancellation/deadline checks at every call without querying the OS
            // for every tensor element or path ancestor. Peak observations retain
            // short-lived increases; this remains a sampled resource boundary.
            if (_watch.Elapsed.TotalSeconds >= _nextResidentObservationSeconds)
            {
                _process.Refresh();
                Require.That(_process.WorkingSet64 <= MaximumResidentBytes && _process.PeakWorkingSet64 <= MaximumResidentBytes,
                    "Observed process resident memory exceeded 4 GiB; capture is incomplete.");
                _nextResidentObservationSeconds = _watch.Elapsed.TotalSeconds + 0.1;
            }
        }
    }
    internal ProcessIdentity Identity() => new(Environment.ProcessId, _process.StartTime.ToUniversalTime(), Environment.CommandLine,
        Environment.GetCommandLineArgs(), "Parent chain is recorded by the required external bounded runner.");
    public void Dispose() => _process.Dispose();
}

internal static class FrozenCase
{
    internal const string Id = "independent-smoke-46-v1";
    internal const string ConfigSha256 = "47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37";
    internal const string TokenIdsSha256 = "BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B";
    internal const string Request = "{\"schema_version\":1,\"model_id\":\"sezika/mmbert-base-independent-s4\",\"state\":{\"text\":\"independent encoder smoke\"},\"questions\":[{\"id\":\"intent\",\"type\":\"choice\",\"instruction\":\"choose the intent\",\"candidates\":[{\"id\":\"a\",\"text\":\"first\"},{\"id\":\"b\",\"text\":\"second\"}]}]}";
    internal static readonly string[] Segments = ["{\"type\":\"choice\",\"id\":\"intent\",\"instruction\":\"choose the intent\"}",
        "{\"text\":\"independent encoder smoke\"}", "{\"id\":\"a\",\"text\":\"first\"}", "{\"id\":\"b\",\"text\":\"second\"}"];
}

internal static class Capture
{
    internal static void Run(Options options, Budget budget)
    {
        budget.Check();
        var package = options.Package ?? throw new InvalidDataException("Package is required.");
        Paths.Check(package, budget); Paths.Check(options.SourceConfig!, budget); Paths.Check(options.Output, budget);
        Require.That(!Paths.IsWithin(options.Output, package), "Output must be outside the input package.");
        Require.That(!Paths.IsWithin(options.Output, Path.GetDirectoryName(options.SourceConfig!)!), "Output must be outside original source assets.");
        using var inputLeases = new InputLeases([
            Path.Combine(package, "model.json"), Path.Combine(package, "source_lock.json"),
            Path.Combine(package, "encoder.safetensors"), Path.Combine(package, "tokenizer", "tokenizer.json"), options.SourceConfig!
        ], budget);
        using var stage = new Stage(options.Output, budget);
        var report = new CaptureReport { CaseId = FrozenCase.Id, PackageDirectory = package, StartedUtc = DateTimeOffset.UtcNow, Process = budget.Identity() };
        IndependentEncoderModelPackage? model = null;
        try
        {
            report.Assets.Add(stage.CopySmall("package-manifest.json", Path.Combine(package, "model.json"), budget, "captured_package_manifest"));
            report.Assets.Add(stage.CopySmall("source-config.json", options.SourceConfig!, budget, "verified_original_config", FrozenCase.ConfigSha256));
            report.Assets.Add(stage.CopySmall("source-lock.json", Path.Combine(package, "source_lock.json"), budget, "captured_provenance_not_license_approval"));
            var managedProgram = Path.Combine(AppContext.BaseDirectory, "Sezika.IndependentNumericCapture.dll");
            var programPath = Environment.ProcessPath ?? throw new InvalidDataException("Process executable path unavailable.");
            report.Assets.Add(Files.Identity(programPath, "actual_process_executable", budget, 32 * 1024 * 1024));
            if (File.Exists(managedProgram)) report.Assets.Add(Files.Identity(managedProgram, "managed_capture_dll_snapshot_not_process_executable", budget, 32 * 1024 * 1024));
            var corePath = Path.Combine(AppContext.BaseDirectory, "Sezika.dll");
            if (File.Exists(corePath)) report.Assets.Add(Files.Identity(corePath, "verified_core_program", budget, 32 * 1024 * 1024));
            report.Assets.Add(new("pytorch_model.bin", IndependentModelLoader.SourceWeightsSha256, 1_231_188_142,
                "fixed_upstream_identity_not_rehashed_this_capture", null));
            stage.Partial(report);
            Console.Error.WriteLine("Loading fixed independent package; cases=1, tokens<=128, resident<=4GiB, deadline<=120s.");
            model = IndependentModelLoader.Load(package, new EncoderExecutionOptions {
                Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(options.TimeoutSeconds)
            }, budget.Token);
            budget.Check();
            Require.That(model.EstimatedResidentBytes <= Budget.MaximumResidentBytes, "Model resident estimate exceeds capture budget.");
            Require.That(model.ModelId == IndependentModelLoader.ModelId && model.SourceModelId == IndependentModelLoader.SourceModelId &&
                model.Revision == IndependentModelLoader.SourceRevision && model.Encoder.Config is { HiddenSize: 768, IntermediateSize: 1152, LayerCount: 22 },
                "Independent package configuration differs from frozen capture.");
            report.Assets.Add(new("encoder.safetensors", model.EncoderSha256, 1_227_772_549, "verified_by_independent_loader", null));
            report.Assets.Add(new("tokenizer/tokenizer.json", model.TokenizerSha256, 17_525_329, "verified_by_independent_loader", null));
            report.EstimatedModelResidentBytes = model.EstimatedResidentBytes;
            var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(FrozenCase.Request));
            var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, request, request.Questions[0],
                new IndependentDecisionSequenceOptions { MaxTokens = 128, MaxCharacters = 4096, Deadline = TimeSpan.FromSeconds(10) }, budget.Token);
            Require.That(sequence.TokenIds.Length == 46 && sequence.MarkerPositions.SequenceEqual([25, 35]) &&
                sequence.CandidateIds.SequenceEqual(["a", "b"]) && sequence.SerializedQuestion == FrozenCase.Segments[0] &&
                sequence.SerializedState == FrozenCase.Segments[1], "Frozen input sequence changed.");
            report.Request = stage.Text("request.json", FrozenCase.Request);
            for (var index = 0; index < FrozenCase.Segments.Length; index++)
            {
                budget.Check(); report.Segments.Add(stage.Text($"segment-{index}.json", FrozenCase.Segments[index]));
            }
            report.TokenIds = (int[])sequence.TokenIds.Clone(); report.MarkerPositions = (int[])sequence.MarkerPositions.Clone();
            report.TokenIdsArtifact = stage.Int32("token-ids.i32", sequence.TokenIds);
            report.MarkerPositionsArtifact = stage.Int32("marker-positions.i32", sequence.MarkerPositions);
            Require.That(report.TokenIdsArtifact.Sha256 == FrozenCase.TokenIdsSha256, "Frozen token IDs SHA-256 changed.");
            var tensors = new TensorCapture(stage, TraceSchema.Build(46, 768, 1152, 22, budget), budget, report.Tensors,
                () => { report.ElapsedSeconds = budget.ElapsedSeconds; stage.Partial(report); });
            model.Encoder.Trace = tensors.Accept;
            stage.Partial(report);
            var hidden = model.Encoder.Encode(sequence.TokenIds, budget.Token);
            model.Encoder.Trace = null;
            tensors.Complete();
            Require.That(hidden.Length == 46 * 768, "Encoder final shape differs from frozen input.");
            report.FinalHiddenSha256 = Files.FloatHash(hidden, budget);
            Require.That(report.Tensors[^1].Name == "encoder/final" && report.Tensors[^1].Sha256 == report.FinalHiddenSha256,
                "Encode return and final trace disagree.");
            var markerValues = new float[2 * 768];
            for (var index = 0; index < 2; index++) { budget.Check(); hidden.AsSpan(sequence.MarkerPositions[index] * 768, 768).CopyTo(markerValues.AsSpan(index * 768)); }
            report.MarkerVectors = stage.Float32("marker-vectors.f32", markerValues, [2, 768]);
            model.Dispose(); model = null; budget.Check();
            report.Status = "cpu_capture_complete"; report.CompletedUtc = DateTimeOffset.UtcNow; report.ElapsedSeconds = budget.ElapsedSeconds;
            stage.Complete(report);
            stage.Publish();
        }
        catch (Exception error)
        {
            report.Status = "incomplete"; report.TerminalError = error is OperationCanceledException ? "cancelled_or_deadline_exceeded" : error.GetType().Name;
            report.TerminalDetail = error.Message.Length <= 1024 ? error.Message : error.Message[..1024];
            report.CompletedUtc = DateTimeOffset.UtcNow; report.ElapsedSeconds = budget.ElapsedSeconds;
            stage.Fail(report);
            throw;
        }
        finally { if (model is not null) { model.Encoder.Trace = null; model.Dispose(); } }
    }
}

internal sealed record TensorSpec(string Name, int[] Shape);

internal sealed class InputLeases : IDisposable
{
    private readonly List<FileStream> _files = new(5);
    internal InputLeases(string[] paths, Budget budget)
    {
        Require.That(paths.Length == 5, "Input lease path count must be exactly five.");
        try
        {
            for (var index = 0; index < 5; index++)
            {
                budget.Check(); Paths.Check(paths[index], budget);
                var file = new FileStream(paths[index], FileMode.Open, FileAccess.Read, FileShare.Read);
                _files.Add(file);
                var bound = index == 2 ? 1_227_772_549L : index == 3 ? 17_525_329L : 1_048_576L;
                Require.That(file.Length is > 0 && file.Length <= bound, "Input lease file size exceeds its fixed asset bound.");
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        // Exactly five read-only handles at most; closing never waits on capture cancellation.
        for (var index = _files.Count - 1; index >= 0 && index < 5; index--) _files[index].Dispose();
        _files.Clear();
    }
}

internal static class TraceSchema
{
    internal static TensorSpec[] Build(int tokens, int hidden, int intermediate, int layers, Budget budget)
    {
        Require.That(tokens is >= 2 and <= 128 && hidden is >= 2 and <= 768 && intermediate is >= 2 and <= 1152 && layers is >= 1 and <= 22,
            "Trace schema exceeds frozen capture dimensions.");
        var result = new List<TensorSpec>(3 + 12 * layers) { new("embedding/raw", [tokens, hidden]), new("embedding/norm", [tokens, hidden]) };
        for (var layer = 0; layer < layers; layer++)
        {
            budget.Check(); var prefix = $"layer/{layer}/";
            result.Add(new(prefix + "norm1", [tokens, hidden])); result.Add(new(prefix + "qkv", [tokens, 3 * hidden]));
            result.Add(new(prefix + "rope_q", [tokens, hidden])); result.Add(new(prefix + "rope_k", [tokens, hidden]));
            result.Add(new(prefix + "attention", [tokens, hidden])); result.Add(new(prefix + "projected", [tokens, hidden]));
            result.Add(new(prefix + "residual", [tokens, hidden])); result.Add(new(prefix + "norm2", [tokens, hidden]));
            result.Add(new(prefix + "up", [tokens, 2 * intermediate])); result.Add(new(prefix + "gelu", [tokens, intermediate]));
            result.Add(new(prefix + "mlp", [tokens, hidden])); result.Add(new(prefix + "hidden", [tokens, hidden]));
        }
        result.Add(new("encoder/final", [tokens, hidden]));
        Require.That(result.Count <= 300, "Trace checkpoint count exceeded."); return result.ToArray();
    }
}

internal sealed class TensorCapture(Stage stage, TensorSpec[] schema, Budget budget, List<TensorArtifact> tensors, Action? onCheckpoint = null)
{
    internal void Accept(string name, float[] values)
    {
        budget.Check();
        Require.That(tensors.Count < schema.Length && tensors.Count < 300, "Too many trace checkpoints.");
        var expected = schema[tensors.Count];
        Require.That(name == expected.Name, "Unknown, duplicate or reordered trace checkpoint.");
        Require.That(values.Length == checked(expected.Shape[0] * expected.Shape[1]), "Trace tensor shape differs from its checkpoint.");
        var artifact = stage.Float32($"tensor-{tensors.Count:D3}.f32", values, expected.Shape);
        tensors.Add(artifact with { Name = name });
        if (name.EndsWith("/hidden", StringComparison.Ordinal) || name == "encoder/final")
        {
            Console.Error.WriteLine($"Captured {tensors.Count}/{schema.Length}: {name}");
            onCheckpoint?.Invoke();
        }
    }
    internal void Complete() { budget.Check(); Require.That(tensors.Count == schema.Length, "Trace capture is incomplete."); }
}

internal static class Paths
{
    internal static bool IsWithin(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Equals(root, comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
    internal static void Check(string path, Budget budget)
    {
        string? component = Path.GetFullPath(path);
        for (var depth = 0; depth < 64 && component is not null; depth++)
        {
            budget.Check();
            if (File.Exists(component) || Directory.Exists(component))
                Require.That((File.GetAttributes(component) & FileAttributes.ReparsePoint) == 0, "Links and junctions are unsupported for capture paths.");
            component = Path.GetDirectoryName(component);
        }
        Require.That(component is null, "Path ancestry exceeds the 64 component bound.");
    }
}

internal static class Files
{
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static Artifact Identity(string path, string verification, Budget budget, int maximumBytes)
    {
        Paths.Check(path, budget);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        Require.That(stream.Length is > 0 && stream.Length <= maximumBytes, "Program or metadata file exceeds its bound.");
        var length = stream.Length; var blocks = checked((int)((length + 65535) / 65536));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; var remaining = length;
        for (var block = 0; block < blocks; block++)
        {
            budget.Check(); var count = (int)Math.Min(remaining, buffer.Length); stream.ReadExactly(buffer.AsSpan(0, count));
            hash.AppendData(buffer, 0, count); remaining -= count;
        }
        budget.Check(); Require.That(remaining == 0 && stream.ReadByte() == -1, "File length changed during hash verification.");
        return new(Path.GetFullPath(path), Convert.ToHexString(hash.GetHashAndReset()), length, verification, null);
    }
    internal static byte[] Small(string path, Budget budget)
    {
        Paths.Check(path, budget); budget.Check();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require.That(stream.Length is > 0 and <= 1_048_576, "Metadata file exceeds 1 MiB.");
        var result = new byte[(int)stream.Length]; stream.ReadExactly(result); budget.Check();
        Require.That(stream.ReadByte() == -1, "Metadata length changed during read."); return result;
    }
    internal static byte[] FloatBytes(float[] values, Budget budget)
    {
        Require.That(values.Length is >= 1 and <= 128 * 2304, "Float tensor exceeds capture element bound.");
        var bytes = new byte[checked(values.Length * sizeof(float))];
        for (var index = 0; index < values.Length; index++)
        {
            if ((index & 255) == 0) budget.Check();
            Require.That(float.IsFinite(values[index]), "Trace tensor contains NaN or infinity.");
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4, 4), values[index]);
        }
        budget.Check(); return bytes;
    }
    internal static string FloatHash(float[] values, Budget budget) => Hash(FloatBytes(values, budget));
}

internal sealed class Stage : IDisposable
{
    internal const long MaximumBytes = 100L * 1024 * 1024;
    private readonly Budget _budget;
    private readonly string _output;
    private readonly Dictionary<string, Artifact> _owned = new(StringComparer.Ordinal);
    private readonly string _owner;
    private bool _published;
    private bool _completed;
    private long _bytes;
    internal string DirectoryPath { get; private set; }
    internal Stage(string output, Budget budget)
    {
        _budget = budget; _output = Path.GetFullPath(output); Paths.Check(_output, budget);
        Require.That(!File.Exists(_output) && !Directory.Exists(_output), "Output already exists; overwrite is forbidden.");
        var parent = Path.GetDirectoryName(_output) ?? throw new InvalidDataException("Output requires a parent directory.");
        Require.That(Directory.Exists(parent), "Output parent must already exist.");
        _owner = Guid.NewGuid().ToString("N");
        DirectoryPath = Path.GetFullPath(Path.Combine(parent, ".numeric-capture-" + _owner + ".partial"));
        Paths.Check(DirectoryPath, budget); Require.That(!Directory.Exists(DirectoryPath) && !File.Exists(DirectoryPath), "Stage collision.");
        Directory.CreateDirectory(DirectoryPath);
        WriteJson("owner.json", new OwnerMarker(1, _owner, _output), CaptureJsonContext.Default.OwnerMarker);
    }
    internal Artifact CopySmall(string name, string source, Budget budget, string verification, string? expectedHash = null)
    {
        var bytes = Files.Small(source, budget); var hash = Files.Hash(bytes);
        Require.That(expectedHash is null || string.Equals(expectedHash, hash, StringComparison.OrdinalIgnoreCase), "Original config SHA-256 mismatch.");
        return Write(name, bytes) with { Verification = verification, SourcePath = Path.GetFullPath(source) };
    }
    internal Artifact Text(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));
    internal Artifact Int32(string name, int[] values)
    {
        Require.That(values.Length is >= 1 and <= 128, "Integer tensor exceeds capture count bound.");
        var bytes = new byte[checked(values.Length * sizeof(int))];
        for (var index = 0; index < values.Length; index++) { _budget.Check(); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), values[index]); }
        return Write(name, bytes);
    }
    internal TensorArtifact Float32(string name, float[] values, int[] shape)
    {
        Require.That(shape.Length == 2 && shape[0] >= 1 && shape[1] >= 1 && checked(shape[0] * shape[1]) == values.Length, "Float shape mismatch.");
        var file = Write(name, Files.FloatBytes(values, _budget));
        return new(name, file.Path, "F32", (int[])shape.Clone(), "little_endian_row_major", file.Bytes, file.Sha256);
    }
    private Artifact Write(string name, byte[] bytes, bool replace = false, Budget? recoveryBudget = null)
    {
        var activeBudget = recoveryBudget ?? _budget; activeBudget.Check();
        Require.That((!_published || recoveryBudget is not null) && Path.GetFileName(name) == name && name.Length is > 0 and <= 128, "Invalid stage artifact path.");
        var prior = _owned.GetValueOrDefault(name); var size = checked(_bytes - (prior?.Bytes ?? 0) + bytes.LongLength);
        Require.That(size <= MaximumBytes && _bytes + bytes.LongLength <= MaximumBytes && _owned.Count < 299,
            "Capture output byte/file budget exceeded, including temporary replacement.");
        var path = Path.GetFullPath(Path.Combine(DirectoryPath, name));
        Require.That(Paths.IsWithin(path, DirectoryPath), "Stage artifact escaped its owned directory.");
        if (prior is not null)
        {
            Require.That(replace, "Duplicate stage artifact."); VerifyOwned(prior, activeBudget);
        }
        else Require.That(!File.Exists(path) && !Directory.Exists(path), "Stage artifact collision.");
        var temporary = path + ".writing-" + _owner;
        Require.That(!File.Exists(temporary), "Temporary artifact collision.");
        // The GUID stage and matching owner marker establish task ownership.
        // Failed partial writes are retained in this stage for external audit.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var blocks = checked((bytes.Length + 65535) / 65536);
            for (var block = 0; block < blocks; block++)
            {
                activeBudget.Check();
                var offset = block * 65536; stream.Write(bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)));
            }
            stream.Flush(true);
        }
        activeBudget.Check();
        File.Move(temporary, path, replace);
        var artifact = new Artifact(name, Files.Hash(bytes), bytes.LongLength, "captured_bytes", null);
        _owned[name] = artifact; _bytes = size; return artifact;
    }
    private void VerifyOwned(Artifact artifact, Budget? activeBudget = null)
    {
        var path = Path.Combine(DirectoryPath, artifact.Path);
        var actual = Files.Identity(path, "stage_ownership", activeBudget ?? _budget, 1_048_576);
        Require.That(actual.Bytes == artifact.Bytes && actual.Sha256 == artifact.Sha256, "Owned artifact changed; retaining it without deletion or overwrite.");
    }
    private Artifact WriteJson<T>(string name, T value, JsonTypeInfo<T> type, Budget? recoveryBudget = null) =>
        Write(name, JsonSerializer.SerializeToUtf8Bytes(value, type), replace: _owned.ContainsKey(name), recoveryBudget: recoveryBudget);
    internal void Partial(CaptureReport report) { _budget.Check(); WriteJson("capture.partial.json", report, CaptureJsonContext.Default.CaptureReport); }
    internal void Complete(CaptureReport report)
    {
        _budget.Check();
        Require.That(report.Tensors.Count == report.ExpectedCheckpoints && report.TokenIds.Length is >= 2 and <= 128 && report.MarkerPositions.Length == 2 &&
            report.Request is not null && report.Segments.Count == 4 && report.MarkerVectors is not null && report.FinalHiddenSha256 is not null &&
            report.Status is "cpu_capture_complete" or "fixture_only_capture_complete",
            "Capture metadata is incomplete.");
        WriteJson("capture.json", report, CaptureJsonContext.Default.CaptureReport); _completed = true;
    }
    internal void CompleteSelfTest(SelfTestReport report)
    {
        _budget.Check(); Require.That(report.FixtureOnly && !report.RepresentsUpstreamParity && !report.RepresentsModelQuality &&
            report.Cases.Length is >= 1 and <= 8 && report.Cases.All(item => item.Status == "passed"), "Self-test report is incomplete.");
        WriteJson("self-test.json", report, CaptureJsonContext.Default.SelfTestReport); _completed = true;
    }
    internal void Publish(Action? beforePublish = null, Action? afterMove = null)
    {
        _budget.Check(); Require.That(_completed, "Incomplete capture cannot publish.");
        VerifyOwned(_owned["owner.json"]);
        beforePublish?.Invoke(); _budget.Check();
        Require.That(!Directory.Exists(_output) && !File.Exists(_output), "Output appeared before publication; overwrite is forbidden.");
        Paths.Check(_output, _budget); _budget.Check();
        Directory.Move(DirectoryPath, _output); DirectoryPath = _output; _published = true;
        afterMove?.Invoke(); _budget.Check();
    }
    internal void Fail(CaptureReport report)
    {
        using var recoveryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var recoveryBudget = new Budget(5, recoveryCancellation.Token, enforceResident: false);
        try
        {
            // Recovery is capped independently at five seconds; it never publishes.
            Paths.Check(DirectoryPath, recoveryBudget); VerifyOwned(_owned["owner.json"], recoveryBudget);
            foreach (var name in new[] { "capture.json", "self-test.json" })
            {
                recoveryBudget.Check();
                if (_owned.TryGetValue(name, out var complete))
                {
                    VerifyOwned(complete, recoveryBudget); File.Delete(Path.Combine(DirectoryPath, complete.Path));
                    _owned.Remove(complete.Path); _bytes -= complete.Bytes;
                }
            }
            report.Status = "incomplete"; WriteJson("capture.partial.json", report, CaptureJsonContext.Default.CaptureReport, recoveryBudget);
            if (_published)
            {
                // Only move a fully journaled, hash-matching task directory. Unknown contents stay in place.
                using var entries = Directory.EnumerateFileSystemEntries(DirectoryPath).GetEnumerator();
                var ended = false;
                for (var index = 0; index <= 300; index++)
                {
                    recoveryBudget.Check();
                    if (!entries.MoveNext()) { ended = true; break; }
                    var name = Path.GetFileName(entries.Current);
                    Require.That(_owned.TryGetValue(name, out var owned) && !Directory.Exists(entries.Current), "Unknown directory content prevents rollback.");
                    VerifyOwned(owned!, recoveryBudget);
                }
                Require.That(ended, "Rollback directory exceeds 300 files.");
                var rollback = Path.Combine(Path.GetDirectoryName(_output)!, ".numeric-capture-" + _owner + "-rollback-" + Guid.NewGuid().ToString("N") + ".partial");
                Paths.Check(rollback, recoveryBudget); Require.That(!Directory.Exists(rollback) && !File.Exists(rollback), "Rollback path collision.");
                recoveryBudget.Check(); Directory.Move(DirectoryPath, rollback); DirectoryPath = rollback; _published = false;
            }
            Console.Error.WriteLine($"Incomplete audit retained: {DirectoryPath}");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        { Console.Error.WriteLine($"Partial audit could not be updated; owned stage retained: {DirectoryPath}; {failure.GetType().Name}"); }
    }
    public void Dispose() { /* Deliverables and failed partial evidence are deliberately retained. No recursive deletion. */ }
}

internal sealed record Artifact(string Path, string Sha256, long Bytes, string Verification, string? SourcePath);
internal sealed record TensorArtifact(string Name, string Path, string Dtype, int[] Shape, string Layout, long Bytes, string Sha256);
internal sealed record OwnerMarker(int SchemaVersion, string Owner, string RequestedOutput);
internal sealed record ProcessIdentity(int Pid, DateTime StartedUtc, string CommandLine, string[] Arguments, string ParentChainEvidence);
internal sealed class CaptureReport
{
    public int SchemaVersion { get; init; } = 1;
    public string Status { get; set; } = "running";
    public string Kind { get; init; } = "csharp_cpu_capture";
    public string ModelId { get; init; } = IndependentModelLoader.ModelId;
    public string SourceModelId { get; init; } = IndependentModelLoader.SourceModelId;
    public string SourceRevision { get; init; } = IndependentModelLoader.SourceRevision;
    public string Protocol { get; init; } = "decision-v1";
    public string LengthPolicy { get; init; } = "strict";
    public string Backend { get; init; } = "csharp_scalar_fp32";
    public string Framework { get; init; } = RuntimeInformation.FrameworkDescription;
    public string Platform { get; init; } = RuntimeInformation.OSDescription;
    public string Architecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string CaseId { get; init; } = FrozenCase.Id;
    public string? PackageDirectory { get; init; }
    public bool FixtureOnly { get; init; }
    public bool RepresentsUpstreamParity => false;
    public bool RepresentsModelQuality => false;
    public bool RepresentsCalibration => false;
    public bool RepresentsBenchmark => false;
    public bool RepresentsLicenseApproval => false;
    public int MaximumCases => 1;
    public int MaximumTokens => 128;
    public int MaximumCheckpoints => 300;
    public long MaximumOutputBytes => Stage.MaximumBytes;
    public long MaximumResidentBytes => Budget.MaximumResidentBytes;
    public string MemoryBudgetObservation => "OS working set and process peak checked at bounded capture checkpoints; external runner remains required for native-call timeout and process-tree cleanup.";
    public string InputSnapshotLeasePolicy => OperatingSystem.IsWindows()
        ? "Five FileShare.Read leases hold package manifest/source lock/encoder/tokenizer/original config from before snapshots through loader/forward; Windows sharing denies file writes/deletion. Ancestor path mutation is outside this static preflight contract."
        : "Five read leases are held, but Unix unlink/path replacement does not provide the Windows file-sharing guarantee; cross-platform atomic input snapshot proof is not established.";
    public int ExpectedCheckpoints { get; init; } = 267;
    public long EstimatedModelResidentBytes { get; set; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public double ElapsedSeconds { get; set; }
    public ProcessIdentity? Process { get; init; }
    public string? TerminalError { get; set; }
    public string? TerminalDetail { get; set; }
    public Artifact? Request { get; set; }
    public List<Artifact> Segments { get; } = [];
    public List<Artifact> Assets { get; } = [];
    public int[] TokenIds { get; set; } = [];
    public int[] MarkerPositions { get; set; } = [];
    public string IntegerDtype => "I32";
    public string IntegerLayout => "little_endian_contiguous";
    public Artifact? TokenIdsArtifact { get; set; }
    public Artifact? MarkerPositionsArtifact { get; set; }
    public TensorArtifact? MarkerVectors { get; set; }
    public List<TensorArtifact> Tensors { get; } = [];
    public string? FinalHiddenSha256 { get; set; }
}
internal sealed record SelfTestCase(int Number, string Name, string Status);
internal sealed record SelfTestReport(int SchemaVersion, string Status, bool FixtureOnly, bool RepresentsUpstreamParity,
    bool RepresentsModelQuality, SelfTestCase[] Cases);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(CaptureReport))]
[JsonSerializable(typeof(OwnerMarker))]
[JsonSerializable(typeof(SelfTestReport))]
internal partial class CaptureJsonContext : JsonSerializerContext { }

internal static class SelfTest
{
    internal static int Run(Options options, Budget budget)
    {
        using var root = new Stage(options.Output, budget);
        var cases = new List<SelfTestCase>(8);
        try
        {
            for (var number = 1; number <= options.MaximumCases; number++)
            {
                budget.Check();
                Console.Error.WriteLine($"Self-test {number}/{options.MaximumCases}; synthetic tensors only.");
                var target = Path.Combine(root.DirectoryPath, $"case-{number}");
                var name = number switch {
                    1 => Tiny(target, budget),
                    2 => RejectNonFinite(target, budget),
                    3 => RejectContract(target, budget),
                    4 => RejectIncomplete(target, budget),
                    5 => RejectOverwrite(target, budget),
                    6 => RejectCancelledPublication(target, budget),
                    7 => RejectAfterMoveCancellation(target, budget),
                    8 => RecoverAfterIrreversiblePeak(target, budget),
                    _ => throw new InvalidDataException("Self-test case bound exceeded.")
                };
                cases.Add(new(number, name, "passed"));
            }
            root.CompleteSelfTest(new(1, "fixture_only", true, false, false, cases.ToArray()));
            root.Publish(); Console.WriteLine($"self-test passed: {cases.Count}/{options.MaximumCases}; {options.Output}"); return 0;
        }
        catch (Exception error)
        {
            root.Fail(new CaptureReport { FixtureOnly = true, Kind = "self_test_incomplete", TerminalError = error.GetType().Name });
            throw;
        }
    }
    private static CaptureReport Fixture(Budget budget) => new() {
        FixtureOnly = true, Kind = "synthetic_direct_ids_trace", ModelId = "sezika/numeric-capture-fixture",
        SourceModelId = "synthetic/csharp-trace-fixture", SourceRevision = "fixture-v1", Protocol = "direct-token-ids-fixture",
        LengthPolicy = "fixed-six-tokens", CaseId = "tiny-trace-six-tokens", ExpectedCheckpoints = 27,
        StartedUtc = DateTimeOffset.UtcNow, Process = budget.Identity()
    };
    private static string Tiny(string output, Budget budget, Action? beforePublish = null, Action? afterMove = null)
    {
        using var stage = new Stage(output, budget); var report = Fixture(budget);
        ModernBertEncoder? encoder = null;
        try
        {
            float[] Values(int count)
            {
                Require.That(count is >= 1 and <= 128, "Tiny weight count exceeded."); var values = new float[count];
                for (var index = 0; index < count; index++) { budget.Check(); values[index] = MathF.Sin((index + 1) * 0.17f) * 0.1f; }
                return values;
            }
            var config = new ModernBertConfig { VocabularySize = 8, HiddenSize = 4, IntermediateSize = 4, LayerCount = 2,
                HeadCount = 2, MaxTokens = 8, GlobalAttentionEvery = 1, LocalAttention = 4 };
            var layers = new ModernBertLayerWeights[2];
            for (var index = 0; index < 2; index++)
            {
                budget.Check(); layers[index] = new() { AttentionNorm = index == 0 ? null : [1f, 1f, 1f, 1f],
                    Qkv = Values(48), AttentionOutput = Values(16), MlpNorm = [1f, 1f, 1f, 1f], MlpUp = Values(32), MlpDown = Values(16) };
            }
            encoder = new(config, new ModernBertWeights { TokenEmbeddings = Values(32), EmbeddingNorm = [1f, 1f, 1f, 1f],
                FinalNorm = [1f, 1f, 1f, 1f], Layers = layers },
                new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(3) }, budget.Token);
            report.Request = stage.Text("request.json", "{\"fixture_only\":true,\"tokenizer_used\":false}");
            for (var index = 0; index < 4; index++) { budget.Check(); report.Segments.Add(stage.Text($"segment-{index}.json", "{\"fixture_only\":true}")); }
            report.TokenIds = [2, 5, 4, 6, 4, 1]; report.MarkerPositions = [2, 4];
            report.TokenIdsArtifact = stage.Int32("token-ids.i32", report.TokenIds);
            report.MarkerPositionsArtifact = stage.Int32("marker-positions.i32", report.MarkerPositions);
            var tensors = new TensorCapture(stage, TraceSchema.Build(6, 4, 4, 2, budget), budget, report.Tensors);
            encoder.Trace = tensors.Accept; var hidden = encoder.Encode(report.TokenIds, budget.Token); encoder.Trace = null;
            tensors.Complete();
            Require.That(report.Tensors.Count == 27 && report.Tensors[0].Dtype == "F32" && hidden.Length == 24 && hidden.Any(value => value != 0),
                "Actual tiny encoder trace or finite nonzero output missing.");
            report.FinalHiddenSha256 = Files.FloatHash(hidden, budget);
            Require.That(report.Tensors[^1].Sha256 == report.FinalHiddenSha256, "Final tensor hash differs from return value.");
            var markers = new float[8]; hidden.AsSpan(8, 4).CopyTo(markers); hidden.AsSpan(16, 4).CopyTo(markers.AsSpan(4));
            report.MarkerVectors = stage.Float32("marker-vectors.f32", markers, [2, 4]);
            report.Status = "fixture_only_capture_complete"; report.CompletedUtc = DateTimeOffset.UtcNow; report.ElapsedSeconds = budget.ElapsedSeconds;
            stage.Complete(report); stage.Publish(beforePublish, afterMove);
            Require.That(File.Exists(Path.Combine(output, "capture.json")) && !report.RepresentsUpstreamParity && !report.RepresentsModelQuality,
                "Tiny export must remain fixture-only.");
            return "actual_tiny_encoder_ordered_shape_hash_marker_capture";
        }
        catch (Exception error) { report.TerminalError = error.GetType().Name; stage.Fail(report); throw; }
        finally { if (encoder is not null) { encoder.Trace = null; encoder.Dispose(); } }
    }
    private static string RejectNonFinite(string output, Budget budget)
    {
        using var stage = new Stage(output, budget); var report = Fixture(budget);
        var tensor = new TensorCapture(stage, [new("embedding/raw", [2, 2])], budget, report.Tensors);
        Reject<InvalidDataException>(() => tensor.Accept("embedding/raw", [1, float.NaN, 2, 3]));
        Reject<InvalidDataException>(() => tensor.Accept("embedding/raw", [1, 2, float.PositiveInfinity, 3]));
        Require.That(report.Tensors.Count == 0 && !File.Exists(Path.Combine(stage.DirectoryPath, "tensor-000.f32")), "Nonfinite tensor bytes were published.");
        report.TerminalError = "expected_nonfinite_rejection"; stage.Fail(report); Require.That(!Directory.Exists(output), "Failed output published.");
        return "nan_and_infinity_rejected_before_tensor_write";
    }
    private static string RejectContract(string output, Budget budget)
    {
        using var stage = new Stage(output, budget); var report = Fixture(budget);
        var tensor = new TensorCapture(stage, [new("embedding/raw", [2, 2]), new("encoder/final", [2, 2])], budget, report.Tensors);
        Reject<InvalidDataException>(() => tensor.Accept("unknown/name", [1, 2, 3, 4]));
        Reject<InvalidDataException>(() => tensor.Accept("embedding/raw", [1, 2, 3]));
        tensor.Accept("embedding/raw", [1, 2, 3, 4]);
        Reject<InvalidDataException>(() => tensor.Accept("embedding/raw", [1, 2, 3, 4]));
        Require.That(report.Tensors.Count == 1, "Rejected trace changed accepted count.");
        report.TerminalError = "expected_contract_rejection"; stage.Fail(report); return "unknown_shape_and_duplicate_checkpoint_rejection";
    }
    private static string RejectIncomplete(string output, Budget budget)
    {
        using var stage = new Stage(output, budget); var report = Fixture(budget);
        var tensor = new TensorCapture(stage, [new("embedding/raw", [2, 2]), new("encoder/final", [2, 2])], budget, report.Tensors);
        tensor.Accept("embedding/raw", [1, 2, 3, 4]); Reject<InvalidDataException>(tensor.Complete);
        Reject<InvalidDataException>(() => stage.Complete(report)); Reject<InvalidDataException>(() => stage.Publish());
        Require.That(!Directory.Exists(output) && !File.Exists(Path.Combine(stage.DirectoryPath, "capture.json")), "Incomplete schema published a complete manifest.");
        report.TerminalError = "expected_incomplete_rejection"; stage.Fail(report); return "missing_checkpoint_blocks_complete_and_atomic_publish";
    }
    private static string RejectOverwrite(string output, Budget budget)
    {
        using var stage = new Stage(output, budget);
        stage.CompleteSelfTest(new(1, "fixture_only", true, false, false, [new(1, "sentinel", "passed")]));
        var sentinel = Encoding.UTF8.GetBytes("owned synthetic publication-race sentinel");
        Reject<InvalidDataException>(() => stage.Publish(() => {
            budget.Check(); Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, "sentinel.txt"), sentinel);
        }));
        Require.That(File.ReadAllBytes(Path.Combine(output, "sentinel.txt")).SequenceEqual(sentinel) && !File.Exists(Path.Combine(output, "capture.json")),
            "Publication replaced an existing target.");
        Reject<InvalidDataException>(() => { using var duplicate = new Stage(output, budget); });
        stage.Fail(new CaptureReport { FixtureOnly = true, TerminalError = "expected_publication_collision" });
        return "target_appearing_at_publish_and_existing_target_never_overwritten";
    }
    private static string RejectCancelledPublication(string output, Budget budget)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        using var cancelledBudget = new Budget(20, cancellation.Token);
        using var stage = new Stage(output, cancelledBudget);
        stage.CompleteSelfTest(new(1, "fixture_only", true, false, false, [new(1, "cancellation", "passed")]));
        Reject<OperationCanceledException>(() => stage.Publish(cancellation.Cancel));
        Require.That(!Directory.Exists(output), "Cancellation in publication window was ignored.");
        stage.Fail(new CaptureReport { FixtureOnly = true, TerminalError = "expected_cancelled_publication" });
        return "cancellation_after_final_metadata_prevents_publication";
    }
    private static string RejectAfterMoveCancellation(string output, Budget budget)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        using var cancelledBudget = new Budget(20, cancellation.Token);
        var moved = false;
        Reject<OperationCanceledException>(() => Tiny(output, cancelledBudget, afterMove: () => {
            Require.That(Directory.Exists(output) && File.Exists(Path.Combine(output, "capture.json")), "Regression did not reach an actual complete directory move.");
            moved = true; cancellation.Cancel();
        }));
        Require.That(moved && !Directory.Exists(output), "Cancellation after actual move left a success directory.");
        RequirePartial(output, budget, "OperationCanceledException");
        return "actual_move_then_cancel_rolls_back_and_removes_complete_manifest";
    }
    private static string RecoverAfterIrreversiblePeak(string output, Budget budget)
    {
        try
        {
            Reject<InvalidDataException>(() => Tiny(output, budget, beforePublish: () => Budget.SyntheticPeakForSelfTest = Budget.MaximumResidentBytes + 1));
            // The synthetic high-water observation remains over budget throughout recovery.
            Reject<InvalidDataException>(budget.Check);
            Require.That(!Directory.Exists(output), "Peak-memory failure published a success directory.");
            // Audit inspection is bounded independently; it does not reset the failed peak sensor.
            using var inspectionCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var inspection = new Budget(5, inspectionCancellation.Token, enforceResident: false);
            RequirePartial(output, inspection, "InvalidDataException");
            return "irreversible_synthetic_peak_failure_recovers_small_audit_without_4gib_allocation";
        }
        finally { Budget.SyntheticPeakForSelfTest = 0; }
    }
    private static void RequirePartial(string output, Budget budget, string error)
    {
        var parent = Path.GetDirectoryName(output)!;
        using var entries = Directory.EnumerateDirectories(parent, ".numeric-capture-*.partial").GetEnumerator();
        var found = false; var ended = false;
        for (var index = 0; index < 32; index++)
        {
            budget.Check(); if (!entries.MoveNext()) { ended = true; break; }
            var owner = Path.Combine(entries.Current, "owner.json");
            using var marker = JsonDocument.Parse(Files.Small(owner, budget));
            if (marker.RootElement.GetProperty("requested_output").GetString() != output) continue;
            Require.That(!File.Exists(Path.Combine(entries.Current, "capture.json")), "Failed task retained a complete capture manifest.");
            using var audit = JsonDocument.Parse(Files.Small(Path.Combine(entries.Current, "capture.partial.json"), budget));
            Require.That(audit.RootElement.GetProperty("status").GetString() == "incomplete" &&
                audit.RootElement.GetProperty("terminal_error").GetString() == error && audit.RootElement.GetProperty("fixture_only").GetBoolean(),
                "Recovery audit did not preserve the actual failure and fixture boundary.");
            found = true;
        }
        Require.That(ended && found, "Matching complete failure audit missing or search exceeded 32 task directories.");
    }
    private static void Reject<TError>(Action action) where TError : Exception
    {
        try { action(); }
        catch (TError) { return; }
        throw new InvalidDataException($"Expected {typeof(TError).Name} rejection did not occur.");
    }
}
