using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.IndependentModelTool;

internal static class DiagnosticAssetPreparationChecks
{
    internal static void Run(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (arguments.Length != 5 || arguments[0] != "check-diagnostic-asset-preparation")
            throw new ArgumentException("Expected --max-checks and --artifact-directory exactly once.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var watch = Stopwatch.StartNew();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        token = lifetime.Token;
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new OperationCanceledException("Diagnostic fixture deadline expired.");
        }
        for (var index = 1; index < 5; index += 2)
        {
            Check();
            if (arguments[index] is not ("--max-checks" or "--artifact-directory") ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) || !options.TryAdd(arguments[index], arguments[index + 1]))
                throw new ArgumentException("Unknown, duplicate or missing diagnostic fixture option.");
        }
        if (!options.TryGetValue("--max-checks", out var maximumText) ||
            !int.TryParse(maximumText, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) || maximum is < 1 or > 8)
            throw new ArgumentException("max-checks must be 1-8.");
        if (!options.TryGetValue("--artifact-directory", out var artifactText) || artifactText.Length > 2048)
            throw new ArgumentException("An explicit artifact directory is required.");
        var artifact = Path.GetFullPath(artifactText);
        if (!Directory.Exists(artifact)) throw new IOException("The explicit artifact directory must exist.");
        DiagnosticAssetPreparationCommand.CheckPath(artifact, Check);
        var directory = Path.Combine(artifact, "synthetic-diagnostic-preparation-" + Guid.NewGuid().ToString("N"));
        var cleanup = new List<DiagnosticAssetPreparationCommand.HeadOwnership>(24);
        var directoryInfo = Directory.CreateDirectory(directory);
        var directoryCreated = directoryInfo.CreationTimeUtc;
        var identity = new DiagnosticHeadIdentity("sezika/synthetic-diagnostic-preparation-fixture", new string('A', 64), new string('B', 64), 4);
        var provenance = new DiagnosticPackageProvenance("synthetic-fixture-not-a-real-package", "synthetic-only", "synthetic-v1", "synthetic-not-license-evidence");
        var cases = new (string Name, Action Run)[]
        {
            ("strict-options-and-explicit-seed", Parsing),
            ("independent-json-zero-updates-unmeasured-loss", Manifest),
            ("seeded-asset-determinism-and-separate-descriptor", Determinism),
            ("new-output-and-package-path-conflict", Conflicts),
            ("cancellation-before-and-after-asset-publication", Cancellation),
            ("manifest-delivery-failure-rolls-back-owned-head", DeliveryFailure),
            ("changed-output-preserved-by-ownership-check", ChangedOutput),
            ("input-read-leases-and-partial-acquisition-release", InputLeases),
        };
        try
        {
            for (var index = 0; index < maximum && index < 8; index++)
            {
                Check();
                Console.WriteLine($"check-diagnostic-asset-preparation: {index + 1}/{maximum} {cases[index].Name}");
                cases[index].Run();
                Console.WriteLine($"PASS: {cases[index].Name}");
            }
            Console.WriteLine($"PASS: {maximum} bounded synthetic checks; no real model load, encoder forward, training, GPU, quality or AOT acceptance");
        }
        finally
        {
            // Independent cleanup budget remains available after the fixture token is cancelled.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var cleanupWatch = Stopwatch.StartNew();
            void CheckCleanup()
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (cleanupWatch.Elapsed > TimeSpan.FromSeconds(5)) throw new IOException("Fixture cleanup deadline expired; resources preserved.");
            }
            for (var index = 0; index < cleanup.Count && index < 24; index++)
            {
                CheckCleanup();
                cleanup[index].Cleanup(CheckCleanup);
            }
            CheckCleanup();
            directoryInfo.Refresh();
            if (!directoryInfo.Exists || directoryInfo.CreationTimeUtc != directoryCreated ||
                (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Fixture directory identity changed; resources preserved.");
            if (Directory.EnumerateFileSystemEntries(directory).Take(25).Any())
                throw new IOException("Fixture directory contains an unowned or leaked entry; resources preserved.");
            CheckCleanup();
            Directory.Delete(directory, recursive: false);
            CheckCleanup();
        }

        string PathFor(string name) => Path.Combine(directory, name);
        DiagnosticAssetPreparationCommand.HeadOwnership Register(string path)
        {
            Check();
            if (cleanup.Count >= 24 || !Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(directory, StringComparison.Ordinal))
                throw new IOException("Synthetic ownership bound or path is invalid.");
            var owner = new DiagnosticAssetPreparationCommand.HeadOwnership(path); cleanup.Add(owner); return owner;
        }
        DiagnosticAssetPreparationManifest Create(string name, int seed)
        {
            var path = PathFor(name); var owner = Register(path); DiagnosticAssetPreparationManifest? result = null;
            DiagnosticAssetPreparationCommand.PrepareHead(path, identity, seed, provenance, json =>
            {
                result = JsonSerializer.Deserialize(json, DiagnosticPreparationJsonContext.Default.DiagnosticAssetPreparationManifest)
                    ?? throw new InvalidDataException("Synthetic manifest is null.");
                owner.Acquire(result.HeadSha256);
            }, _ => { }, Check, token);
            return result ?? throw new InvalidDataException("Synthetic manifest was not delivered.");
        }
        void WriteFixture(string path, byte[] bytes)
        {
            var owner = Register(path);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes);
            owner.Acquire(Hash(bytes));
        }
        void Parsing()
        {
            var parsed = DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--package", directory, "--head", PathFor("parse.asset"), "--seed", "-17"], token);
            Assert(parsed.Seed == -17 && parsed.TimeoutSeconds == 300, "Explicit negative seed and default deadline retained.");
            var timed = DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--head", PathFor("parse.asset"), "--seed", int.MinValue.ToString(CultureInfo.InvariantCulture), "--package", directory, "--timeout-seconds", "600"], token);
            Assert(timed.Seed == int.MinValue && timed.TimeoutSeconds == 600, "Seed and deadline upper bound parsed.");
            Rejects(() => DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--package", directory, "--head", PathFor("parse.asset")], token));
            Rejects(() => DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--package", directory, "--head", PathFor("parse.asset"), "--seed", "oops"], token));
            Rejects(() => DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--package", directory, "--package", directory, "--seed", "1"], token));
            Rejects(() => DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--records", directory, "--head", PathFor("parse.asset"), "--seed", "1"], token));
            Rejects(() => DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--package", directory, "--head", PathFor("parse.asset"), "--seed", "1", "--timeout-seconds", "601"], token));
            Rejects(() => DiagnosticAssetPreparationCommand.Parse(["prepare-diagnostic-head", "--package", directory, "--head", PathFor("parse.asset"), "--seed", "2147483648"], token));
        }
        void Manifest()
        {
            var result = Create("manifest.asset", 17);
            Assert(result.SchemaVersion == 1 && result.Contract == DiagnosticAssetPreparationCommand.Contract && result.Status == "untrained_diagnostic_only", "Independent diagnostic contract.");
            Assert(result.Origin == "UntrainedDiagnostic" && result.CompletedUpdates == 0 && result.ObservedLoss is null, "No invented training update or observed loss.");
            Assert(result.InitializationAlgorithm == DiagnosticHeadAssetStore.Algorithm && result.AlgorithmVersion == 1 &&
                result.InitializationDescriptorDomain == DiagnosticHeadAssetStore.DescriptorDomain, "Domain-separated initialization metadata retained.");
            Assert(!result.EncoderForwardExecuted && !result.TrainingExecuted && !result.RealGpuAcceptance && !result.LanguageQualityAcceptance &&
                !result.CalibrationAcceptance && !result.NativeAotAcceptance && !result.PerformanceAcceptance && !result.ReleaseAcceptance, "All execution and formal acceptance flags false.");
            var bytes = File.ReadAllBytes(result.HeadPath);
            Assert(result.HeadBytes == bytes.Length && result.HeadSha256 == Hash(bytes), "Actual diagnostic asset bytes/hash reported.");
            var loaded = DiagnosticHeadAssetStore.Load(result.HeadPath, result.DiagnosticHeadIdentity, result.Seed,
                result.InitializationDescriptorSha256, result.HeadSha256, token);
            Assert(loaded.Identity == identity && loaded.CompletedUpdates == 0 && loaded.ObservedLoss is null, "Actual dedicated loader verifies reported identity.");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, DiagnosticPreparationJsonContext.Default.DiagnosticAssetPreparationManifest));
            Assert(json.RootElement.GetProperty("observed_loss").ValueKind == JsonValueKind.Null &&
                !json.RootElement.TryGetProperty("feature_sha256", out _) && !json.RootElement.TryGetProperty("data_manifest_sha256", out _) &&
                !json.RootElement.TryGetProperty("training_state", out _), "JSON does not manufacture formal feature/data/training identity.");
        }
        void Determinism()
        {
            var first = Create("deterministic-a.asset", 17); var second = Create("deterministic-b.asset", 17); var different = Create("deterministic-c.asset", 18);
            Assert(first.HeadSha256 == second.HeadSha256 && first.InitializationDescriptorSha256 == second.InitializationDescriptorSha256 &&
                File.ReadAllBytes(first.HeadPath).SequenceEqual(File.ReadAllBytes(second.HeadPath)), "Equal identity/seed reproduces exact SZDH1 bytes.");
            Assert(first.HeadSha256 != different.HeadSha256 && first.InitializationDescriptorSha256 != different.InitializationDescriptorSha256,
                "Changing seed changes asset and canonical initialization descriptor.");
            Rejects(() => DiagnosticHeadAssetStore.Load(first.HeadPath, identity, 18, first.InitializationDescriptorSha256, first.HeadSha256, token));
        }
        void Conflicts()
        {
            var path = PathFor("existing.asset"); var bytes = Encoding.UTF8.GetBytes("synthetic-existing-not-a-head"); WriteFixture(path, bytes);
            Rejects(() => DiagnosticAssetPreparationCommand.PrepareHead(path, identity, 17, provenance, _ => { }, _ => { }, Check, token));
            Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "Existing output bytes retained.");
            Rejects(() => DiagnosticAssetPreparationCommand.ValidatePaths(directory, PathFor("inside-package.asset"), Check));
            Rejects(() => DiagnosticAssetPreparationCommand.ValidatePaths(directory, directory, Check));
            Rejects(() => DiagnosticAssetPreparationCommand.ValidateOutput(Path.Combine(directory, "absent-directory", "head.asset"), Check));
        }
        void Cancellation()
        {
            var before = PathFor("cancel-before.asset");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Cancels(() => DiagnosticAssetPreparationCommand.PrepareHead(before, identity, 17, provenance, _ => { }, _ => { },
                () => cancelled.Token.ThrowIfCancellationRequested(), cancelled.Token));
            Assert(!File.Exists(before), "Already-cancelled preparation acquires no output.");
            var after = PathFor("cancel-after.asset"); var observedDelivery = false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Cancels(() => DiagnosticAssetPreparationCommand.PrepareHead(after, identity, 17, provenance, _ =>
            {
                observedDelivery = File.Exists(after); cancellation.Cancel();
            }, _ => { }, () => { Check(); cancellation.Token.ThrowIfCancellationRequested(); }, cancellation.Token));
            Assert(observedDelivery && !File.Exists(after), "Actual head existed before cancelled manifest delivery; owned file rolled back.");
        }
        void DeliveryFailure()
        {
            var path = PathFor("delivery-failure.asset"); var observed = false;
            try
            {
                DiagnosticAssetPreparationCommand.PrepareHead(path, identity, 17, provenance, _ =>
                {
                    observed = File.Exists(path); throw new SyntheticDeliveryException();
                }, _ => { }, Check, token);
                throw new InvalidOperationException("Synthetic delivery failure was ignored.");
            }
            catch (SyntheticDeliveryException) { }
            Assert(observed && !File.Exists(path), "Original delivery exception propagated after owned-head rollback.");
        }
        void ChangedOutput()
        {
            var path = PathFor("changed-output.asset"); var owner = Register(path); byte[]? foreign = null;
            try
            {
                DiagnosticAssetPreparationCommand.PrepareHead(path, identity, 17, provenance, _ =>
                {
                    foreign = File.ReadAllBytes(path); foreign[^1] ^= 1; File.WriteAllBytes(path, foreign); owner.Acquire(Hash(foreign));
                    throw new SyntheticDeliveryException();
                }, _ => { }, Check, token);
                throw new InvalidOperationException("Changed output cleanup should have failed.");
            }
            catch (AggregateException error)
            {
                Assert(error.InnerExceptions.Count == 2 && error.InnerExceptions[0] is SyntheticDeliveryException && error.InnerExceptions[1] is IOException,
                    "Original failure and cleanup refusal are both retained.");
            }
            Assert(foreign is not null && File.ReadAllBytes(path).SequenceEqual(foreign), "Modified output was preserved, then registered separately for fixture cleanup.");
        }
        void InputLeases()
        {
            var path = PathFor("lease-input.json"); var bytes = Encoding.UTF8.GetBytes("synthetic-fixed-input"); WriteFixture(path, bytes);
            using (var lease = new DiagnosticAssetPreparationCommand.ReadOnlyInputs([path], Check))
            {
                if (OperatingSystem.IsWindows())
                {
                    Rejects(() => { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
                    Rejects(() => File.Delete(path));
                }
                Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "Read lease retains input bytes.");
            }
            Rejects(() => { using var failed = new DiagnosticAssetPreparationCommand.ReadOnlyInputs([path, PathFor("missing-input.json")], Check); });
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                Assert(writer.Length == bytes.Length, "Partial acquisition releases all previously opened input handles.");
            Rejects(() => { using var failed = new DiagnosticAssetPreparationCommand.ReadOnlyInputs([path, path, path, path], Check); });
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Rejects(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is DecisionException or ArgumentException or IOException) { return; }
        throw new InvalidOperationException("Invalid synthetic input was not rejected.");
    }
    private static void Cancels(Action action)
    {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Synthetic cancellation was not observed.");
    }
    private sealed class SyntheticDeliveryException : Exception;
}
