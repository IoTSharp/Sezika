using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Sezika;

namespace Sezika.Tests;

internal static class HeadAssetPublicationChecks
{
    public static void Run(Action<bool, string> check, bool tiny = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var clock = Stopwatch.StartNew();
        Action<Scope>[] cases = [Compatibility, ExistingTarget, PreCanceled, CancelAfterWrite,
            PartialFailure, ChangedIdentity, DoubleFailure, CleanupBudget];
        var count = tiny ? 1 : cases.Length;
        for (var index = 0; index < count && index < 8; index++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Head publication contract deadline.");
            var scope = new Scope(deadline.Token);
            Exception? original = null, cleanup = null;
            try { cases[index](scope); }
            catch (Exception error) { original = error; }
            finally { try { scope.Cleanup(); } catch (Exception error) { cleanup = error; } }
            if (original is not null && cleanup is not null) throw new AggregateException(original, cleanup);
            if (original is not null) ExceptionDispatchInfo.Capture(original).Throw();
            if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
            check(true, $"head asset publication case {index + 1}/{count}");
        }
    }

    private static void Compatibility(Scope scope)
    {
        var state = State();
        var expected = LegacyBytes(state);
        var hash = Save(scope, state);
        scope.Register(scope.Target);
        Require(File.ReadAllBytes(scope.Target).SequenceEqual(expected), "Legacy SZMH1 bytes changed.");
        Require(hash == Convert.ToHexString(SHA256.HashData(expected)), "Legacy file hash changed.");
        var loaded = IndependentMarkerHeadAssetStore.Load(scope.Target, state.Identity, state.FeatureSha256, hash, scope.Token);
        Require(loaded.Head.CopyWeights().SequenceEqual(state.Head.CopyWeights()) && loaded.CompletedSteps == 0,
            "Legacy asset Load compatibility failed.");
    }

    private static void ExistingTarget(Scope scope)
    {
        byte[] existing = [7, 8, 9];
        using (var file = new FileStream(scope.Target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(existing);
        scope.Register(scope.Target);
        Require(Failure(() => Save(scope, State())) is IOException, "Existing target did not fail.");
        Require(File.ReadAllBytes(scope.Target).SequenceEqual(existing) && !File.Exists(scope.Temporary),
            "Existing target changed or owned temporary leaked.");
    }

    private static void PreCanceled(Scope scope)
    {
        scope.Cancel();
        Require(Failure(() => Save(scope, State())) is OperationCanceledException && scope.Created == 0 && !File.Exists(scope.Target),
            "Pre-canceled publication created a file.");
    }

    private static void CancelAfterWrite(Scope scope)
    {
        Require(Failure(() => Save(scope, State(), (stage, _) =>
        {
            if (stage == HeadAssetPublicationStage.AfterFlush) scope.Cancel();
        })) is OperationCanceledException, "Write cancellation was lost.");
        Require(scope.Created == 1 && !File.Exists(scope.Temporary) && !File.Exists(scope.Target),
            "Canceled publication did not reclaim its verified actual bytes.");
    }

    private static void PartialFailure(Scope scope)
    {
        var injected = new IOException("original_partial_write_fault");
        long actualPrefix = -1;
        var error = Failure(() => Save(scope, State(), (stage, path) =>
        {
            if (stage == HeadAssetPublicationStage.AfterPrefix) throw injected;
            if (stage == HeadAssetPublicationStage.BeforeCleanup) actualPrefix = new FileInfo(path).Length;
        }));
        Require(ReferenceEquals(error, injected) && actualPrefix == 256 && !File.Exists(scope.Temporary) && !File.Exists(scope.Target),
            "Real failed prefix was not reclaimed or original exception was lost.");
    }

    private static void ChangedIdentity(Scope scope)
    {
        var error = Failure(() => Save(scope, State(), (stage, path) =>
        {
            if (stage != HeadAssetPublicationStage.BeforePublish) return;
            scope.ReplaceTemporary(path);
        }));
        Require(File.Exists(scope.Temporary) && !File.Exists(scope.Target) && error.ToString().Contains(scope.Temporary, StringComparison.Ordinal),
            "Changed creation identity was deleted or not reported.");
        Require(File.ReadAllBytes(scope.Temporary).SequenceEqual(new byte[] { 61, 62, 63 }),
            "Unknown replacement bytes were changed.");
    }

    private static void DoubleFailure(Scope scope)
    {
        var error = Failure(() => Save(scope, State(), (stage, path) =>
        {
            if (stage == HeadAssetPublicationStage.AfterPrefix) throw new IOException("original_partial_write_fault");
            if (stage == HeadAssetPublicationStage.BeforeCleanup)
            {
                scope.Register(path);
                throw new IOException("cleanup_injected_fault");
            }
        }));
        Require(error is AggregateException both && both.Flatten().InnerExceptions.Count == 2 &&
            both.ToString().Contains("original_partial_write_fault", StringComparison.Ordinal) &&
            both.ToString().Contains("cleanup_injected_fault", StringComparison.Ordinal) && File.Exists(scope.Temporary),
            "Original and cleanup failures were not both retained.");
    }

    private static void CleanupBudget(Scope scope)
    {
        var clock = Stopwatch.StartNew();
        var error = Failure(() => Save(scope, State(), (stage, path) =>
        {
            if (stage == HeadAssetPublicationStage.BeforeCleanup) scope.Register(path);
        }, TimeSpan.Zero));
        Require(clock.Elapsed < TimeSpan.FromSeconds(2) && !File.Exists(scope.Target) && File.Exists(scope.Temporary) &&
            error.ToString().Contains("cleanup budget expired", StringComparison.OrdinalIgnoreCase),
            "Expired owner reserve deleted unknown bytes, lost its path or waited unboundedly.");
    }

    private static string Save(Scope scope, MarkerTrainingState state,
        Action<HeadAssetPublicationStage, string>? observe = null, TimeSpan? cleanupDuration = null)
        => IndependentMarkerHeadAssetStore.SaveForPublicationChecks(scope.Target, state,
            new HeadAssetPublicationHooks
            {
                CleanupDuration = cleanupDuration ?? TimeSpan.FromSeconds(5),
                Observe = (stage, path) =>
                {
                    if (stage == HeadAssetPublicationStage.Created) scope.ObserveCreated(path);
                    observe?.Invoke(stage, path);
                },
            }, scope.Token);

    private static MarkerTrainingState State() => new(
        new MarkerFeatureIdentity("sezika/publication-fixture", new string('A', 64), new string('B', 64), new string('C', 64), 2),
        new string('F', 64), 17, 0.05f, 0, 0, [0.25f, -0.1f, -0.2f, 0.3f, 0.15f, -0.05f]);

    // Frozen legacy v1 wire contract, serialized independently of SaveNew.
    private static byte[] LegacyBytes(MarkerTrainingState state)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write("SZMH1"u8); writer.Write(1);
            void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
            Text(state.Identity.ModelId); Text(state.Identity.EncoderSha256); Text(state.Identity.TokenizerSha256);
            Text(state.Identity.DataManifestSha256); writer.Write(2); Text("decision-v1"); Text("strict");
            Text(state.FeatureSha256); writer.Write(17); writer.Write(0.05f); writer.Write(0); writer.Write(0d);
            var weights = state.Head.CopyWeights(); writer.Write(6);
            for (var index = 0; index < 6; index++) writer.Write(weights[index]);
        }
        var payload = stream.ToArray();
        return [.. payload, .. SHA256.HashData(payload)];
    }

    private static Exception Failure(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected publication failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scope
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Dictionary<string, OwnedFile?> _owned = new(StringComparer.Ordinal);
        private readonly string _directory;
        private readonly DateTime _directoryCreationUtc;
        internal string Target { get; }
        internal string Temporary { get; private set; } = string.Empty;
        internal int Created { get; private set; }
        internal CancellationToken Token => _cancellation.Token;

        internal Scope(CancellationToken token)
        {
            // Compute paths before acquiring the linked token source.
            var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"sezika-head-publication-{Guid.NewGuid():N}"));
            var target = Path.Combine(directory, "head.asset");
            if (Directory.Exists(directory)) throw new IOException($"Existing fixture directory preserved: {directory}");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var created = false;
            var creationKnown = false;
            var creation = default(DateTime);
            try
            {
                Directory.CreateDirectory(directory);
                created = true;
                creation = Directory.GetCreationTimeUtc(directory);
                creationKnown = true;
                _directory = directory;
                _directoryCreationUtc = creation;
                Target = target;
                _cancellation = cancellation;
            }
            catch (Exception original)
            {
                Exception? cancellationError = null, directoryError = null;
                var cleanupClock = Stopwatch.StartNew();
                try { cancellation.Dispose(); } catch (Exception error) { cancellationError = error; }
                try
                {
                    if (Directory.Exists(directory))
                    {
                        var info = new DirectoryInfo(directory);
                        if (!created || !creationKnown || info.LinkTarget is not null || info.CreationTimeUtc != creation)
                            throw new IOException($"Unknown constructor directory preserved: {directory}");
                        if (cleanupClock.Elapsed >= TimeSpan.FromSeconds(5))
                            throw new TimeoutException($"Constructor cleanup budget; preserved {directory}");
                        Directory.Delete(directory, recursive: false);
                    }
                }
                catch (Exception error) { directoryError = error; }
                if (cancellationError is not null || directoryError is not null)
                    throw new AggregateException($"Fixture constructor/cleanup failed; inspect {directory}",
                        new[] { original, cancellationError, directoryError }.Where(error => error is not null).Cast<Exception>());
                throw;
            }
        }

        internal void Cancel() => _cancellation.Cancel();

        internal void ObserveCreated(string path)
        {
            CheckPath(path); Created++; Temporary = path;
            if (_owned.Count >= 3 || Created > 1) throw new InvalidOperationException("Fixture owner count exceeded.");
            _owned.Add(path, null);
        }

        internal void Register(string path)
        {
            CheckPath(path);
            if (!_owned.ContainsKey(path) && _owned.Count >= 3) throw new InvalidOperationException("Fixture owner count exceeded.");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 128 * 1024 || info.LinkTarget is not null)
                throw new IOException($"Fixture cannot establish owned file identity: {path}");
            _owned[path] = new(info.CreationTimeUtc, info.Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }

        internal void ReplaceTemporary(string path)
        {
            // Preserve the known original, then create a genuinely different
            // file at its former name. Both files have explicit fixture owners.
            Register(path);
            var original = Path.Combine(_directory, "moved-original.asset");
            CheckPath(original);
            File.Move(path, original);
            Register(original);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(new byte[] { 61, 62, 63 });
            File.SetCreationTimeUtc(path, _owned[original]!.CreationUtc.AddMinutes(1));
            Register(path);
        }

        internal void Cleanup()
        {
            var clock = Stopwatch.StartNew();
            var errors = new List<Exception>(4);
            try
            {
                foreach (var entry in _owned)
                {
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5)) { errors.Add(new TimeoutException($"Fixture cleanup budget; preserved {_directory}")); break; }
                    try
                    {
                        CheckPath(entry.Key);
                        if (!File.Exists(entry.Key)) continue;
                        var info = new FileInfo(entry.Key);
                        var identity = entry.Value;
                        if (identity is null || info.LinkTarget is not null || info.CreationTimeUtc != identity.CreationUtc ||
                            info.Length != identity.Length || info.Length > 128 * 1024 ||
                            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.Key))).Equals(identity.Sha256, StringComparison.Ordinal))
                            throw new IOException($"Unknown fixture file preserved: {entry.Key}");
                        File.Delete(entry.Key);
                    }
                    catch (Exception error) { errors.Add(error); }
                }
                // Non-recursive removal preserves any unregistered object.
                if (errors.Count == 0)
                {
                    var directory = new DirectoryInfo(_directory);
                    if (directory.LinkTarget is not null || directory.CreationTimeUtc != _directoryCreationUtc)
                        throw new IOException($"Unknown fixture directory preserved: {_directory}");
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5))
                        throw new TimeoutException($"Fixture directory cleanup budget; preserved {_directory}");
                    Directory.Delete(_directory, recursive: false);
                }
            }
            catch (Exception error) { errors.Add(error); }
            finally { _cancellation.Dispose(); }
            if (errors.Count > 0) throw new AggregateException($"Fixture cleanup retained {_directory}", errors);
        }

        private void CheckPath(string path)
        {
            if (!Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(_directory, StringComparison.Ordinal))
                throw new IOException($"Fixture path outside its task directory: {path}");
        }

        private sealed record OwnedFile(DateTime CreationUtc, long Length, string Sha256);
    }
}
