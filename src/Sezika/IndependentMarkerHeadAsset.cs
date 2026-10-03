using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(state);
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
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes); file.Flush(flushToDisk: true);
            }
            Check();
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return hash;
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
