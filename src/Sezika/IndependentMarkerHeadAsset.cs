using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.ExceptionServices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sezika.Tests")]

namespace Sezika;

/// <summary>Independent inference head asset with training provenance metadata; optimizer slots are not exported.</summary>
public sealed record IndependentMarkerHeadAsset(
    MarkerFeatureIdentity Identity,
    string FeatureSha256,
    int Seed,
    float LearningRate,
    int CompletedSteps,
    double LastLoss,
    LinearMarkerHead Head,
    string FileSha256);

public static class IndependentMarkerHeadAssetStore
{
    private static readonly byte[] Magic = "SZMH1"u8.ToArray();
    private const int MaxBytes = 128 * 1024;
    private static readonly TimeSpan MaxIoDuration = TimeSpan.FromSeconds(30);

    public static string SaveNew(string path, MarkerTrainingState state, CancellationToken cancellationToken = default)
        => SaveCore(path, state, cancellationToken, null);

    internal static string SaveForPublicationChecks(string path, MarkerTrainingState state,
        HeadAssetPublicationHooks hooks, CancellationToken cancellationToken = default)
        => SaveCore(path, state, cancellationToken, hooks);

    private static string SaveCore(string path, MarkerTrainingState state, CancellationToken cancellationToken,
        HeadAssetPublicationHooks? hooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(state);
        var cleanupDuration = hooks?.CleanupDuration ?? TimeSpan.FromSeconds(5);
        if (cleanupDuration < TimeSpan.Zero || cleanupDuration > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(hooks));
        var clock = Stopwatch.StartNew();
        void Check() { cancellationToken.ThrowIfCancellationRequested(); if (clock.Elapsed > MaxIoDuration) throw new DecisionException("head_asset_deadline_exceeded", "Head asset I/O deadline expired."); }
        Check();
        IndependentMarkerHeadTrainer.ValidateIdentity(state.Identity, "head_asset_invalid");
        if (!IndependentMarkerHeadTrainer.IsHash(state.FeatureSha256) || state.CompletedSteps is < 0 or > 10_000 ||
            !float.IsFinite(state.LearningRate) || state.LearningRate <= 0 || state.LearningRate > 1 ||
            !double.IsFinite(state.LastLoss) || state.LastLoss < 0)
            throw new DecisionException("head_asset_invalid", "Head asset training metadata is invalid.");
        _ = new LinearMarkerHead(state.Identity.HiddenSize, state.Weights);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic); writer.Write(1);
            IndependentMarkerHeadTrainer.WriteIdentity(writer, state.Identity);
            IndependentMarkerHeadTrainer.WriteText(writer, state.FeatureSha256);
            writer.Write(state.Seed); writer.Write(state.LearningRate); writer.Write(state.CompletedSteps); writer.Write(state.LastLoss);
            writer.Write(state.Weights.Length);
            for (var index = 0; index < state.Weights.Length; index++) { if ((index & 255) == 0) Check(); writer.Write(state.Weights[index]); }
        }
        if (stream.Length + 32 > MaxBytes) throw new DecisionException("head_asset_invalid", "Head asset exceeds its size bound.");
        var payload = stream.ToArray(); var checksum = SHA256.HashData(payload); var bytes = new byte[payload.Length + checksum.Length];
        payload.CopyTo(bytes, 0); checksum.CopyTo(bytes, payload.Length);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        // Publish atomically so cancellation or process interruption cannot
        // create a truncated asset at the requested destination.
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        // Prepare owner storage before acquiring a file. After CreateNew
        // returns, registration is only assignment and cannot allocate.
        var owner = new HeadAssetTemporaryOwner(temporary);
        FileStream? file = null;
        var acquired = false;
        Stopwatch? cleanupClock = null;
        Exception? executionError = null, prefixError = null, disposalError = null, cleanupError = null;
        var published = false;
        void CheckCleanup()
        {
            if (cleanupClock!.Elapsed >= cleanupDuration)
                throw new IOException($"Head asset cleanup budget expired; retained task path: {temporary}");
        }
        try
        {
            Check();
            // Disable the managed write buffer: a failed write is audited from
            // the real handle, rather than from the intended payload.
            file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1);
            acquired = true;
            owner.CaptureCreation();
            hooks?.Observe?.Invoke(HeadAssetPublicationStage.Created, temporary);
            for (var offset = 0; offset < bytes.Length; offset += 256)
            {
                Check();
                file.Write(bytes, offset, Math.Min(256, bytes.Length - offset));
                if (offset == 0) hooks?.Observe?.Invoke(HeadAssetPublicationStage.AfterPrefix, temporary);
            }
            file.Flush(flushToDisk: true);
            hooks?.Observe?.Invoke(HeadAssetPublicationStage.AfterFlush, temporary);
            Check();
        }
        catch (Exception error) { executionError = error; }
        finally
        {
            // One independent reserve covers prefix capture, disposal and
            // cleanup, regardless of cancellation or the expired save budget.
            cleanupClock = Stopwatch.StartNew();
            if (acquired)
            {
                try { owner.CapturePrefix(file!, CheckCleanup); }
                catch (Exception error) { prefixError = error; }
                try { file!.Dispose(); }
                catch (Exception error) { disposalError = error; }
            }
        }
        try
        {
            if (executionError is null && prefixError is null && disposalError is null)
            {
                hooks?.Observe?.Invoke(HeadAssetPublicationStage.BeforePublish, temporary);
                Check();
                if (owner.Length != bytes.Length || !owner.Sha256.Equals(hash, StringComparison.Ordinal))
                    throw new IOException($"Head asset written bytes differ from the payload; retained path: {temporary}");
                owner.Verify(Check);
                Check();
                File.Move(temporary, destination); // No overwrite, including an existing target.
                published = true;
            }
        }
        catch (Exception error) { executionError = error; }
        finally
        {
            if (!published && acquired)
            {
                try
                {
                    hooks?.Observe?.Invoke(HeadAssetPublicationStage.BeforeCleanup, temporary);
                    CheckCleanup();
                    owner.DeleteVerified(CheckCleanup);
                }
                catch (Exception error) { cleanupError = error; }
            }
        }
        var errors = new[] { executionError, prefixError, disposalError, cleanupError }
            .Where(error => error is not null).Cast<Exception>().ToArray();
        if (errors.Length == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Length > 1)
            throw new AggregateException($"Head asset publication/cleanup failed; inspect retained task path: {temporary}", errors);
        return hash;
    }

    private sealed class HeadAssetTemporaryOwner(string path)
    {
        private readonly string _path = Path.GetFullPath(path);
        private DateTime _creationUtc;
        private bool _creationKnown, _prefixKnown;
        internal long Length { get; private set; }
        internal string Sha256 { get; private set; } = string.Empty;

        internal void CaptureCreation()
        {
            var info = Inspect();
            _creationUtc = info.CreationTimeUtc;
            _creationKnown = true;
        }

        internal void CapturePrefix(FileStream file, Action check)
        {
            check();
            RequireCreation();
            var length = file.Length;
            if (length is < 0 or > MaxBytes)
                throw new IOException($"Head asset actual prefix is outside its bound; retained path: {_path}");
            var hash = HashHandle(file, length, check);
            RequireCreation();
            if (file.Length != length)
                throw new IOException($"Head asset prefix changed during capture; retained path: {_path}");
            Length = length;
            Sha256 = hash;
            _prefixKnown = true;
        }

        internal void Verify(Action check)
        {
            check();
            if (!_prefixKnown)
                throw new IOException($"Head asset actual prefix ownership is unknown; retained path: {_path}");
            RequireCreation();
            using var lease = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1);
            if (lease.Length != Length || !HashHandle(lease, Length, check).Equals(Sha256, StringComparison.Ordinal))
                throw new IOException($"Head asset prefix ownership changed; retained path: {_path}");
            RequireCreation();
            check();
        }

        internal void DeleteVerified(Action check)
        {
            check();
            if (!File.Exists(_path)) return;
            Verify(check);
            // Recheck metadata immediately before the path operation. These
            // synchronous local-file checks cooperate with the wall budget;
            // they do not offer OS-level isolation from hostile directory races.
            RequireCreation();
            check();
            File.Delete(_path);
            check();
        }

        private FileInfo Inspect()
        {
            if (!Path.GetFullPath(_path).Equals(_path, StringComparison.Ordinal))
                throw new IOException($"Head asset path ownership changed; retained path: {_path}");
            var info = new FileInfo(_path);
            info.Refresh();
            if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Head asset path identity is unknown; retained path: {_path}");
            return info;
        }

        private void RequireCreation()
        {
            if (!_creationKnown || Inspect().CreationTimeUtc != _creationUtc)
                throw new IOException($"Head asset creation identity changed; retained path: {_path}");
        }

        private static string HashHandle(FileStream file, long length, Action check)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[4096];
            file.Position = 0;
            long consumed = 0;
            for (var block = 0; consumed < length && block < MaxBytes / 4096; block++)
            {
                check();
                var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - consumed));
                if (count == 0) throw new EndOfStreamException("Head asset prefix ended during ownership capture.");
                hash.AppendData(buffer, 0, count);
                consumed += count;
            }
            if (consumed != length) throw new IOException("Head asset prefix read exceeded its block bound.");
            check();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    public static IndependentMarkerHeadAsset Load(string path, MarkerFeatureIdentity expectedIdentity,
        string expectedFeatureSha256, string expectedFileSha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(expectedIdentity);
        if (!IndependentMarkerHeadTrainer.IsHash(expectedFeatureSha256) || !IndependentMarkerHeadTrainer.IsHash(expectedFileSha256))
            throw new DecisionException("head_asset_hash_invalid", "Head asset hashes are required.");
        var clock = Stopwatch.StartNew();
        void Check() { cancellationToken.ThrowIfCancellationRequested(); if (clock.Elapsed > MaxIoDuration) throw new DecisionException("head_asset_deadline_exceeded", "Head asset I/O deadline expired."); }
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 64 or > MaxBytes) throw new DecisionException("head_asset_invalid", "Head asset size is invalid.");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); Check();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedFileSha256, StringComparison.OrdinalIgnoreCase) ||
            !SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
            throw new DecisionException("head_asset_hash_invalid", "Head asset checksum does not match.");
        try
        {
            using var stream = new MemoryStream(bytes, 0, bytes.Length - 32, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic) || reader.ReadInt32() != 1)
                throw new DecisionException("head_asset_invalid", "Head asset magic or version is invalid.");
            var identity = new MarkerFeatureIdentity(IndependentMarkerHeadTrainer.ReadText(reader), IndependentMarkerHeadTrainer.ReadText(reader),
                IndependentMarkerHeadTrainer.ReadText(reader), IndependentMarkerHeadTrainer.ReadText(reader), reader.ReadInt32(),
                IndependentMarkerHeadTrainer.ReadText(reader), IndependentMarkerHeadTrainer.ReadText(reader));
            var featureHash = IndependentMarkerHeadTrainer.ReadText(reader);
            if (!IndependentMarkerHeadTrainer.IdentityMatches(identity, expectedIdentity) ||
                !featureHash.Equals(expectedFeatureSha256, StringComparison.OrdinalIgnoreCase))
                throw new DecisionException("head_asset_identity_mismatch", "Head asset identity differs from the requested model or features.");
            var seed = reader.ReadInt32(); var rate = reader.ReadSingle(); var steps = reader.ReadInt32(); var loss = reader.ReadDouble(); var count = reader.ReadInt32();
            if (!float.IsFinite(rate) || rate <= 0 || rate > 1 || steps is < 0 or > 10_000 ||
                !double.IsFinite(loss) || loss < 0 || count != checked(3 * identity.HiddenSize))
                throw new DecisionException("head_asset_invalid", "Head asset optimizer metadata or shape is invalid.");
            var weights = new float[count];
            for (var index = 0; index < count; index++) { if ((index & 255) == 0) Check(); weights[index] = reader.ReadSingle(); }
            if (stream.Position != stream.Length || Array.Exists(weights, value => !float.IsFinite(value)))
                throw new DecisionException("head_asset_invalid", "Head asset contains invalid tensor data.");
            return new IndependentMarkerHeadAsset(identity, featureHash, seed, rate, steps, loss,
                new LinearMarkerHead(identity.HiddenSize, weights), expectedFileSha256);
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException or OverflowException)
        {
            throw new DecisionException("head_asset_invalid", "Head asset is malformed.");
        }
    }
}

internal enum HeadAssetPublicationStage { Created, AfterPrefix, AfterFlush, BeforePublish, BeforeCleanup }

// Per-call internal seams; production SaveNew never installs hooks or mutable global state.
internal sealed record HeadAssetPublicationHooks
{
    internal Action<HeadAssetPublicationStage, string>? Observe { get; init; }
    internal TimeSpan CleanupDuration { get; init; } = TimeSpan.FromSeconds(5);
}
