using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.IndependentModelTool;

/// <summary>Single deterministic diagnostic asset initialization, with no encoder execution or training.</summary>
internal sealed record DiagnosticAssetPreparationCommand(string Package, string Head, int Seed, int TimeoutSeconds)
{
    internal const string Contract = "independent-diagnostic-head-preparation-v1";
    internal const int ManifestByteLimit = 16 * 1024;

    internal static DiagnosticAssetPreparationCommand Parse(IReadOnlyList<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (arguments.Count is not (7 or 9) || !arguments[0].Equals("prepare-diagnostic-head", StringComparison.OrdinalIgnoreCase))
            throw Invalid("Expected --package, --head, --seed and optional --timeout-seconds.");
        var watch = Stopwatch.StartNew();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count && index < 9; index += 2)
        {
            token.ThrowIfCancellationRequested();
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw Invalid("Argument parsing deadline expired.");
            var name = arguments[index];
            if (name is not ("--package" or "--head" or "--seed" or "--timeout-seconds") ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].Length > 2048 ||
                !values.TryAdd(name, arguments[index + 1])) throw Invalid("Unknown, duplicate or empty option.");
        }
        string Required(string name) => values.TryGetValue(name, out var value) ? value : throw Invalid($"Missing {name}.");
        int Integer(string name, string text, int min, int max)
        {
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw Invalid($"Invalid {name}; expected integer in [{min},{max}].");
            return value;
        }
        return new(Path.GetFullPath(Required("--package")), Path.GetFullPath(Required("--head")),
            Integer("--seed", Required("--seed"), int.MinValue, int.MaxValue),
            Integer("--timeout-seconds", values.GetValueOrDefault("--timeout-seconds", "300"), 1, 600));
    }

    internal void Run(Action<string> writeManifest, Action<string> progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(writeManifest); ArgumentNullException.ThrowIfNull(progress);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        token = lifetime.Token;
        var watch = Stopwatch.StartNew();
        void Check() => CheckBudget(watch, TimeSpan.FromSeconds(TimeoutSeconds), token);
        Check();
        ValidatePaths(Package, Head, Check);
        // Hold all three inputs through loading, asset publication and verification. On Windows
        // FileShare.Read excludes writers/deletion; this is cooperative path protection, not inode isolation.
        var inputs = new[] { Path.Combine(Package, "model.json"), Path.Combine(Package, "encoder.safetensors"),
            Path.Combine(Package, "tokenizer", "tokenizer.json") };
        using var leases = new ReadOnlyInputs(inputs, Check);
        progress("loading the fixed independent package; no encoder forward or training will run");
        using var model = IndependentModelLoader.Load(Package, new EncoderExecutionOptions
        {
            Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(TimeoutSeconds),
        }, token);
        Check();
        var identity = new DiagnosticHeadIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, model.Encoder.Config.HiddenSize);
        var provenance = new DiagnosticPackageProvenance(Package, model.SourceModelId, model.Revision, model.License);
        PrepareHead(Head, identity, Seed, provenance, writeManifest, progress, Check, token);
    }

    // The checks exercise only this deterministic single-asset portion with explicit synthetic identity.
    // The production Run entry constructs its identity exclusively from IndependentModelLoader.Load.
    internal static void PrepareHead(string path, DiagnosticHeadIdentity identity, int seed,
        DiagnosticPackageProvenance provenance, Action<string> writeManifest, Action<string> progress,
        Action check, CancellationToken token)
    {
        check();
        ValidateOutput(path, check);
        var owner = new HeadOwnership(path); // Prepare ownership before CreateNew can acquire a file.
        Exception? failure = null;
        try
        {
            progress("initializing one SZDH1 zero-update diagnostic head");
            var created = DiagnosticHeadAssetStore.CreateNew(path, identity, seed, token);
            owner.Acquire(created.FileSha256);
            check();
            progress("reloading the new head with its actual file and initialization descriptor hashes");
            var loaded = DiagnosticHeadAssetStore.Load(path, created.Identity, seed,
                created.InitializationDescriptorSha256, created.FileSha256, token);
            check();
            var result = new DiagnosticAssetPreparationManifest
            {
                SchemaVersion = 1, Contract = Contract, Status = "untrained_diagnostic_only", HeadPath = path,
                HeadBytes = owner.Length, HeadSha256 = loaded.FileSha256, DiagnosticHeadIdentity = loaded.Identity,
                Origin = "UntrainedDiagnostic", CompletedUpdates = loaded.CompletedUpdates, ObservedLoss = loaded.ObservedLoss,
                Seed = loaded.Seed, InitializationAlgorithm = loaded.InitializationAlgorithm, AlgorithmVersion = loaded.AlgorithmVersion,
                InitializationDescriptorDomain = loaded.InitializationDescriptorDomain,
                InitializationDescriptorSha256 = loaded.InitializationDescriptorSha256, PackageProvenance = provenance,
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, DiagnosticPreparationJsonContext.Default.DiagnosticAssetPreparationManifest);
            if (bytes.Length > ManifestByteLimit) throw Invalid("Diagnostic output JSON exceeds its byte bound.");
            var json = Encoding.UTF8.GetString(bytes);
            check();
            // Output failure or cancellation rolls back only the verified owned head. A successful
            // return means both CreateNew/Load verification and delivery of the manifest completed.
            writeManifest(json);
            check();
            return;
        }
        catch (Exception error) { failure = error; }
        try { owner.Cleanup(); }
        catch (Exception cleanup) { throw new AggregateException("Diagnostic preparation failed and owned-head cleanup could not be completed.", failure!, cleanup); }
        ExceptionDispatchInfo.Capture(failure!).Throw();
    }

    internal static void ValidatePaths(string package, string head, Action check)
    {
        if (!Directory.Exists(package)) throw Invalid("The explicit independent package directory does not exist.");
        CheckPath(package, check);
        var root = Path.GetFullPath(package).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var output = Path.GetFullPath(head);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (output.Equals(root, comparison) || output.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw Invalid("The diagnostic head must be outside the input package.");
        ValidateOutput(output, check);
    }

    internal static void ValidateOutput(string path, Action check)
    {
        check();
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(Path.GetDirectoryName(path)))
            throw Invalid("The new head requires an absolute path with an existing output directory.");
        CheckPath(path, check);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("The head path already exists; no overwrite is allowed.");
    }

    internal static void CheckPath(string path, Action check)
    {
        string? component = Path.GetFullPath(path);
        for (var depth = 0; depth < 64 && component is not null; depth++)
        {
            check();
            if ((File.Exists(component) || Directory.Exists(component)) && (File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                throw Invalid("Links and junctions are not supported for diagnostic inputs or outputs.");
            component = Path.GetDirectoryName(component);
        }
        if (component is not null) throw Invalid("Diagnostic path exceeds its depth bound.");
    }

    private static void CheckBudget(Stopwatch watch, TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (watch.Elapsed > duration) throw new DecisionException("diagnostic_preparation_deadline_exceeded", "Diagnostic asset preparation deadline expired.");
    }

    internal sealed class ReadOnlyInputs : IDisposable
    {
        private readonly FileStream?[] _streams;
        private readonly Exception?[] _cleanupErrors;
        private readonly DecisionException _cleanupDeadlineError;
        internal ReadOnlyInputs(IReadOnlyList<string> paths, Action check)
        {
            if (paths.Count is < 1 or > 3) throw Invalid("At most three package inputs may be leased.");
            _streams = new FileStream[paths.Count];
            _cleanupErrors = new Exception?[4]; // Three lease errors plus one independent deadline error.
            _cleanupDeadlineError = new DecisionException("diagnostic_preparation_cleanup_deadline_exceeded", "Input lease cleanup deadline expired.");
            try
            {
                for (var index = 0; index < paths.Count && index < 3; index++)
                {
                    check(); CheckPath(paths[index], check);
                    _streams[index] = new FileStream(paths[index], FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                check();
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Package input acquisition failed and lease cleanup failed.", primary, cleanup); }
                throw;
            }
        }
        public void Dispose()
        {
            // Preserve every registered handle through cleanup, and attempt every one even if a
            // prior Dispose failed. Synchronous close is cooperative; expiry is retained as an
            // error and cannot be reported as successful cleanup after the five-second budget.
            var started = Stopwatch.GetTimestamp();
            for (var index = 0; index < 4; index++) _cleanupErrors[index] = null;
            for (var index = 0; index < _streams.Length && index < 3; index++)
            {
                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) _cleanupErrors[3] = _cleanupDeadlineError;
                try { _streams[index]?.Dispose(); _streams[index] = null; }
                catch (Exception error) { _cleanupErrors[index] = error; }
            }
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) _cleanupErrors[3] = _cleanupDeadlineError;
            var count = 0; Exception? single = null;
            for (var index = 0; index < 4; index++)
                if (_cleanupErrors[index] is { } error) { count++; single = error; }
            if (count == 1) ExceptionDispatchInfo.Capture(single!).Throw();
            if (count > 1)
            {
                var errors = new List<Exception>(4);
                for (var index = 0; index < 4; index++) if (_cleanupErrors[index] is { } error) errors.Add(error);
                throw new AggregateException("Multiple input lease cleanup errors.", errors);
            }
        }
    }

    internal sealed class HeadOwnership
    {
        private readonly string _path;
        private readonly FileInfo _info;
        private string? _hash;
        private DateTime? _creationUtc;
        internal long Length { get; private set; }
        internal HeadOwnership(string path) { _path = Path.GetFullPath(path); _info = new FileInfo(_path); }
        internal void Acquire(string hash)
        {
            _hash = hash; // No allocation between the returned CreateNew identity and ownership registration.
            _info.Refresh();
            if (!_info.Exists) throw new IOException("The newly created diagnostic head disappeared before ownership capture.");
            Length = _info.Length; _creationUtc = _info.CreationTimeUtc;
        }
        internal void Cleanup(Action? externalCheck = null)
        {
            if (_hash is null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var watch = Stopwatch.StartNew();
            void Check()
            {
                externalCheck?.Invoke();
                CheckBudget(watch, TimeSpan.FromSeconds(5), timeout.Token);
            }
            Check(); CheckPath(_path, Check); _info.Refresh();
            if (!_info.Exists) return;
            if (_creationUtc is null || _info.CreationTimeUtc != _creationUtc || _info.Length != Length || Length is < 1 or > 128 * 1024)
                throw new IOException("Diagnostic head ownership changed; the path was preserved.");
            using (var lease = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (lease.Length != Length) throw new IOException("Diagnostic head size changed; the path was preserved.");
                var bytes = new byte[(int)Length]; lease.ReadExactly(bytes); Check();
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(_hash, StringComparison.Ordinal))
                    throw new IOException("Diagnostic head bytes changed; the path was preserved.");
            }
            _info.Refresh(); Check();
            if (!_info.Exists || _info.CreationTimeUtc != _creationUtc || _info.Length != Length)
                throw new IOException("Diagnostic head ownership changed before deletion; the path was preserved.");
            File.Delete(_path);
        }
    }

    private static DecisionException Invalid(string message) => new("diagnostic_preparation_arguments_invalid", message);
}

internal sealed record DiagnosticPackageProvenance(string PackagePath, string SourceModelId, string Revision, string License);

internal sealed class DiagnosticAssetPreparationManifest
{
    public int SchemaVersion { get; init; }
    public required string Contract { get; init; }
    public required string Status { get; init; }
    public required string HeadPath { get; init; }
    public long HeadBytes { get; init; }
    public required string HeadSha256 { get; init; }
    public required DiagnosticHeadIdentity DiagnosticHeadIdentity { get; init; }
    public required string Origin { get; init; }
    public int CompletedUpdates { get; init; }
    public double? ObservedLoss { get; init; }
    public int Seed { get; init; }
    public required string InitializationAlgorithm { get; init; }
    public int AlgorithmVersion { get; init; }
    public required string InitializationDescriptorDomain { get; init; }
    public required string InitializationDescriptorSha256 { get; init; }
    public required DiagnosticPackageProvenance PackageProvenance { get; init; }
    public bool EncoderForwardExecuted => false;
    public bool TrainingExecuted => false;
    public bool RealGpuAcceptance => false;
    public bool LanguageQualityAcceptance => false;
    public bool CalibrationAcceptance => false;
    public bool NativeAotAcceptance => false;
    public bool PerformanceAcceptance => false;
    public bool ReleaseAcceptance => false;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(DiagnosticAssetPreparationManifest))]
internal partial class DiagnosticPreparationJsonContext : JsonSerializerContext;
