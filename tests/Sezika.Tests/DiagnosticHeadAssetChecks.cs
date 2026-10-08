using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Sezika;

namespace Sezika.Tests;

internal static class DiagnosticHeadAssetChecks
{
    private static readonly DiagnosticHeadIdentity Identity = new("sezika/diagnostic-asset-fixture",
        new string('A', 64), new string('B', 64), 2);

    public static void Run(Action<bool, string> check, bool tiny = false)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var clock = Stopwatch.StartNew();
        Action<Fixture>[] cases = [Deterministic, ExpectedBindings, ProvenanceAndMagic, InitializationTampering,
            DecoderBounds, Cancellation, ExistingTarget, LegacyRejection];
        var count = tiny ? 1 : cases.Length;
        for (var index = 0; index < count && index < 8; index++)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Diagnostic asset contract deadline.");
            var fixture = new Fixture(cancellation.Token);
            Exception? original = null, cleanup = null;
            try { cases[index](fixture); }
            catch (Exception error) { original = error; }
            finally { try { fixture.Cleanup(); } catch (Exception error) { cleanup = error; } }
            if (original is not null && cleanup is not null) throw new AggregateException(original, cleanup);
            if (original is not null) ExceptionDispatchInfo.Capture(original).Throw();
            if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
            check(true, $"diagnostic head asset case {index + 1}/{count}");
        }
    }

    private static void Deterministic(Fixture fixture)
    {
        var path = fixture.PrepareOutput("head.asset");
        var first = DiagnosticHeadAssetStore.CreateNew(path, Identity, 17, fixture.Token);
        fixture.Register(path);
        var second = fixture.Create("same-seed.asset", 17);
        var loaded = Load(path, first, fixture.Token);
        Require(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(fixture.Path("same-seed.asset"))) &&
            first.FileSha256 == second.FileSha256 && first.InitializationDescriptorSha256 == second.InitializationDescriptorSha256,
            "Same initialization did not produce identical bytes and hashes.");
        Require(loaded.Origin == DiagnosticHeadOrigin.UntrainedDiagnostic && loaded.CompletedUpdates == 0 &&
            loaded.ObservedLoss is null && loaded.InitializationDescriptorDomain == "sezika/diagnostic-head-initialization/v1" &&
            loaded.Head.CopyWeights().SequenceEqual(new float[] { -0.00460000010f, 0.00139999995f, 0.00499999989f, 0.00490000006f, 0.00219999999f, 0.00000000f }),
            "Diagnostic provenance, absent loss or frozen seed-17 reference differs.");
        Require(loaded.Identity == Identity && loaded.Head.CopyWeights().SequenceEqual(first.Head.CopyWeights()),
            "Typed diagnostic round trip differs.");
    }

    private static void ExpectedBindings(Fixture fixture)
    {
        var asset = fixture.Create("head.asset", 17); var path = fixture.Path("head.asset");
        Require(DiagnosticHeadAssetStore.Load(path, Identity with
            { EncoderSha256 = Identity.EncoderSha256.ToLowerInvariant(), TokenizerSha256 = Identity.TokenizerSha256.ToLowerInvariant() },
            17, asset.InitializationDescriptorSha256.ToLowerInvariant(), asset.FileSha256.ToLowerInvariant(), fixture.Token).Identity == Identity,
            "Hex source/hash spelling did not canonicalize consistently.");
        Expect(() => DiagnosticHeadAssetStore.Load(path, Identity with { EncoderSha256 = new string('C', 64) },
            17, asset.InitializationDescriptorSha256, asset.FileSha256, fixture.Token), "diagnostic_head_identity_mismatch");
        Expect(() => DiagnosticHeadAssetStore.Load(path, Identity, 17, asset.InitializationDescriptorSha256,
            new string('0', 64), fixture.Token), "diagnostic_head_hash_invalid");
        Expect(() => DiagnosticHeadAssetStore.Load(path, Identity, 17, new string('0', 64),
            asset.FileSha256, fixture.Token), "diagnostic_head_initialization_mismatch");
        Expect(() => DiagnosticHeadAssetStore.Load(path, Identity, 18, asset.InitializationDescriptorSha256,
            asset.FileSha256, fixture.Token), "diagnostic_head_initialization_mismatch");
    }

    private static void ProvenanceAndMagic(Fixture fixture)
    {
        var asset = fixture.Create("head.asset", 17); var bytes = File.ReadAllBytes(fixture.Path("head.asset"));
        Action<byte[]>[] mutations = [b => b[0] ^= 1, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), 2),
            b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(9), 2),
            b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(13), 1), b => b[17] = 1];
        for (var index = 0; index < mutations.Length && index < 5; index++)
        {
            fixture.Token.ThrowIfCancellationRequested();
            var changed = (byte[])bytes.Clone(); mutations[index](changed);
            RejectMutation(fixture, asset, $"provenance-{index}.asset", changed, "diagnostic_head_asset_invalid");
        }
    }

    private static void InitializationTampering(Fixture fixture)
    {
        var asset = fixture.Create("head.asset", 17); var bytes = File.ReadAllBytes(fixture.Path("head.asset"));
        var offsets = Offsets(bytes);
        Action<byte[]>[] mutations = [b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offsets.Seed), 18),
            b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offsets.Weights), BitConverter.SingleToInt32Bits(float.NaN)),
            b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offsets.Weights), BitConverter.SingleToInt32Bits(0.25f)),
            b => b[offsets.Descriptor] ^= 1, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offsets.AlgorithmVersion), 2),
            b => b[offsets.DomainText] ^= 1];
        string[] codes = ["diagnostic_head_initialization_mismatch", "diagnostic_head_asset_invalid",
            "diagnostic_head_initialization_mismatch", "diagnostic_head_initialization_mismatch",
            "diagnostic_head_asset_invalid", "diagnostic_head_asset_invalid"];
        for (var index = 0; index < mutations.Length && index < 6; index++)
        {
            fixture.Token.ThrowIfCancellationRequested();
            var changed = (byte[])bytes.Clone(); mutations[index](changed);
            RejectMutation(fixture, asset, $"initialization-{index}.asset", changed, codes[index],
                expectedSeed: index == 0 ? 18 : asset.Seed,
                expectedDescriptor: index == 3 ? Convert.ToHexString(changed.AsSpan(offsets.Descriptor, 32)) : asset.InitializationDescriptorSha256);
        }
    }

    private static void DecoderBounds(Fixture fixture)
    {
        var asset = fixture.Create("head.asset", 17); var bytes = File.ReadAllBytes(fixture.Path("head.asset"));
        var offsets = Offsets(bytes);
        var shape = (byte[])bytes.Clone(); BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(offsets.Count), 5);
        RejectMutation(fixture, asset, "shape.asset", shape, "diagnostic_head_asset_invalid");
        var text = (byte[])bytes.Clone(); BinaryPrimitives.WriteInt32LittleEndian(text.AsSpan(18), 257);
        RejectMutation(fixture, asset, "text-bound.asset", text, "diagnostic_head_asset_invalid");
        var utf8 = (byte[])bytes.Clone(); utf8[22] = 0xFF;
        RejectMutation(fixture, asset, "utf8.asset", utf8, "diagnostic_head_asset_invalid");
        byte[] trailing = [.. bytes.AsSpan(0, bytes.Length - 32).ToArray(), 7, .. new byte[32]];
        RejectMutation(fixture, asset, "trailing.asset", trailing, "diagnostic_head_asset_invalid");
        var truncated = bytes.AsSpan(0, offsets.Descriptor + 4).ToArray();
        byte[] shortPayload = [.. truncated, .. new byte[32]];
        RejectMutation(fixture, asset, "truncated.asset", shortPayload, "diagnostic_head_asset_invalid");
    }

    private static void Cancellation(Fixture fixture)
    {
        using var pre = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token); pre.Cancel();
        var prePath = fixture.PrepareOutput("pre-canceled.asset");
        Require(Failure(() => DiagnosticHeadAssetStore.CreateNew(prePath, Identity, 17, pre.Token)) is OperationCanceledException &&
            !File.Exists(prePath), "Pre-canceled diagnostic initialization created a file.");
        using var writing = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var output = fixture.PrepareOutput("after-write.asset"); string? temporary = null;
        var error = Failure(() => DiagnosticHeadAssetStore.CreateForPublicationChecks(output, Identity, 17,
            new HeadAssetPublicationHooks { Observe = (stage, path) =>
            {
                if (stage == HeadAssetPublicationStage.Created) { temporary = path; fixture.TrackPending(path); }
                if (stage == HeadAssetPublicationStage.AfterFlush) writing.Cancel();
            } }, writing.Token));
        Require(error is OperationCanceledException && temporary is not null && !File.Exists(temporary) && !File.Exists(output),
            "Write-canceled diagnostic initialization did not reclaim its verified prefix.");
    }

    private static void ExistingTarget(Fixture fixture)
    {
        var asset = fixture.Create("head.asset", 17); var path = fixture.Path("head.asset");
        var bytes = File.ReadAllBytes(path);
        Require(Failure(() => fixture.Create("head.asset", 18)) is IOException && File.ReadAllBytes(path).SequenceEqual(bytes) &&
            Convert.ToHexString(SHA256.HashData(bytes)) == asset.FileSha256, "Existing diagnostic target was overwritten.");
    }

    private static void LegacyRejection(Fixture fixture)
    {
        var asset = fixture.Create("head.asset", 17); var path = fixture.Path("head.asset");
        var fileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var legacy = new MarkerFeatureIdentity(Identity.ModelId, Identity.EncoderSha256, Identity.TokenizerSha256,
            new string('C', 64), 2);
        Expect(() => IndependentMarkerHeadAssetStore.Load(path, legacy, new string('F', 64), fileHash, fixture.Token),
            "head_asset_invalid");
        var set = new MarkerFeatureSet(legacy, [new MarkerFeatureExample("diagnostic-magic-negative",
            MarkerQuestionType.Choice, "en", [1, 2, 3, 4], [1, 2], [[0f, 0f], [0f, 0f]], 0)]);
        Expect(() => MarkerTrainingCheckpoint.Load(path, set, fileHash, fixture.Token), "training_checkpoint_invalid");
        Require(fileHash == asset.FileSha256, "Legacy negatives did not use the current file hash.");
    }

    private static DiagnosticHeadAsset Load(string path, DiagnosticHeadAsset asset, CancellationToken token)
        => DiagnosticHeadAssetStore.Load(path, asset.Identity, asset.Seed,
            asset.InitializationDescriptorSha256, asset.FileSha256, token);

    private static void RejectMutation(Fixture fixture, DiagnosticHeadAsset asset, string name, byte[] bytes, string code,
        int? expectedSeed = null, string? expectedDescriptor = null)
    {
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
        var path = fixture.WriteOwned(name, bytes);
        // Actual tampered-file SHA and a valid checksum ensure the intended parser gate is exercised.
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Expect(() => DiagnosticHeadAssetStore.Load(path, asset.Identity, expectedSeed ?? asset.Seed,
            expectedDescriptor ?? asset.InitializationDescriptorSha256, hash, fixture.Token), code);
    }

    private static WireOffsets Offsets(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream, Encoding.UTF8);
        stream.Position = 18;
        void SkipText() { var length = reader.ReadInt32(); if (length is < 1 or > 256) throw new InvalidDataException(); stream.Position += length; }
        for (var index = 0; index < 3; index++) SkipText();
        _ = reader.ReadInt32(); SkipText(); SkipText(); SkipText();
        var algorithm = (int)stream.Position; _ = reader.ReadInt32();
        var seed = (int)stream.Position; _ = reader.ReadInt32();
        var domain = (int)stream.Position + 4; SkipText();
        var descriptor = (int)stream.Position; stream.Position += 32;
        var count = (int)stream.Position; _ = reader.ReadInt32();
        return new(algorithm, seed, domain, descriptor, count, (int)stream.Position);
    }

    private static void Expect(Action action, string code)
    {
        try { action(); }
        catch (DecisionException error) when (error.Code == code) { return; }
        throw new InvalidOperationException($"Expected {code}.");
    }

    private static Exception Failure(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected diagnostic asset failure.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record WireOffsets(int AlgorithmVersion, int Seed, int DomainText, int Descriptor, int Count, int Weights);

    private sealed class Fixture
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Dictionary<string, Owned?> _owned = new(StringComparer.Ordinal);
        private readonly string _directory;
        private readonly DateTime _creationUtc;
        internal CancellationToken Token => _cancellation.Token;

        internal Fixture(CancellationToken token)
        {
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sezika-diagnostic-head-{Guid.NewGuid():N}"));
            if (Directory.Exists(directory)) throw new IOException($"Existing fixture directory preserved: {directory}");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var created = false; var known = false; var creation = default(DateTime);
            try
            {
                Directory.CreateDirectory(directory); created = true;
                creation = Directory.GetCreationTimeUtc(directory); known = true;
                _directory = directory; _creationUtc = creation; _cancellation = cancellation;
            }
            catch (Exception original)
            {
                var clock = Stopwatch.StartNew(); Exception? tokenError = null, directoryError = null;
                try { cancellation.Dispose(); } catch (Exception error) { tokenError = error; }
                try
                {
                    if (Directory.Exists(directory))
                    {
                        var info = new DirectoryInfo(directory);
                        if (!created || !known || info.LinkTarget is not null || info.CreationTimeUtc != creation)
                            throw new IOException($"Unknown constructor directory preserved: {directory}");
                        if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException($"Constructor cleanup preserved {directory}");
                        Directory.Delete(directory, recursive: false);
                    }
                }
                catch (Exception error) { directoryError = error; }
                if (tokenError is not null || directoryError is not null)
                    throw new AggregateException($"Diagnostic fixture constructor/cleanup failed: {directory}",
                        new[] { original, tokenError, directoryError }.Where(error => error is not null).Cast<Exception>());
                throw;
            }
        }

        internal string Path(string name)
        {
            if (System.IO.Path.GetFileName(name) != name) throw new IOException("Fixture filename is not local.");
            return System.IO.Path.Combine(_directory, name);
        }

        internal string PrepareOutput(string name) { var path = Path(name); TrackPending(path); return path; }

        internal void TrackPending(string path)
        {
            CheckPath(path);
            if (_owned.ContainsKey(path)) return;
            if (_owned.Count >= 8) throw new IOException("Diagnostic fixture owner count exceeds eight.");
            _owned.Add(path, null);
        }

        internal DiagnosticHeadAsset Create(string name, int seed)
        {
            var path = PrepareOutput(name);
            var asset = DiagnosticHeadAssetStore.CreateForPublicationChecks(path, Identity, seed,
                new HeadAssetPublicationHooks { Observe = (stage, temporary) =>
                {
                    if (stage == HeadAssetPublicationStage.Created) TrackPending(temporary);
                } }, Token);
            Register(path); return asset;
        }

        internal string WriteOwned(string name, byte[] bytes)
        {
            Token.ThrowIfCancellationRequested();
            if (bytes.Length > 128 * 1024) throw new IOException("Fixture file bound.");
            var path = PrepareOutput(name);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            Register(path); return path;
        }

        internal void Register(string path)
        {
            CheckPath(path);
            if (!_owned.ContainsKey(path)) throw new IOException($"Unregistered fixture file preserved: {path}");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 128 * 1024 || info.LinkTarget is not null ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"Unknown fixture file preserved: {path}");
            _owned[path] = new(info.CreationTimeUtc, info.Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }

        internal void Cleanup()
        {
            var clock = Stopwatch.StartNew(); var errors = new List<Exception>(9);
            try
            {
                foreach (var entry in _owned)
                {
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5)) { errors.Add(new TimeoutException($"Fixture cleanup preserved {_directory}")); break; }
                    try
                    {
                        CheckPath(entry.Key); if (!File.Exists(entry.Key)) continue;
                        var info = new FileInfo(entry.Key); var expected = entry.Value;
                        if (expected is null || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                            info.CreationTimeUtc != expected.CreationUtc || info.Length != expected.Length || info.Length > 128 * 1024 ||
                            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.Key))) != expected.Sha256)
                            throw new IOException($"Unknown fixture file preserved: {entry.Key}");
                        if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException($"Fixture deletion budget; preserved {entry.Key}");
                        File.Delete(entry.Key);
                    }
                    catch (Exception error) { errors.Add(error); }
                }
                if (errors.Count == 0)
                {
                    var directory = new DirectoryInfo(_directory);
                    if (directory.LinkTarget is not null || directory.CreationTimeUtc != _creationUtc)
                        throw new IOException($"Unknown fixture directory preserved: {_directory}");
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException($"Fixture directory budget; preserved {_directory}");
                    Directory.Delete(_directory, recursive: false);
                }
            }
            catch (Exception error) { errors.Add(error); }
            finally { _cancellation.Dispose(); }
            if (errors.Count > 0) throw new AggregateException($"Diagnostic fixture cleanup retained {_directory}", errors);
        }

        private void CheckPath(string path)
        {
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) != _directory)
                throw new IOException($"Fixture path outside task directory: {path}");
        }

        private sealed record Owned(DateTime CreationUtc, long Length, string Sha256);
    }
}
