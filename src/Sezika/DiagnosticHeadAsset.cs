using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Sezika;

/// <summary>Encoder binding only. No dataset, feature cache or training identity is asserted.</summary>
public sealed record DiagnosticHeadIdentity(string ModelId, string EncoderSha256, string TokenizerSha256,
    int HiddenSize, string Protocol = "decision-v1", string LengthPolicy = "strict");

public enum DiagnosticHeadOrigin { UntrainedDiagnostic = 1 }

/// <summary>A reproducible zero-update head; only the dedicated diagnostic store constructs this type.</summary>
public sealed class DiagnosticHeadAsset
{
    public DiagnosticHeadIdentity Identity { get; }
    public DiagnosticHeadOrigin Origin => DiagnosticHeadOrigin.UntrainedDiagnostic;
    public int CompletedUpdates => 0;
    public double? ObservedLoss => null;
    public int Seed { get; }
    public string InitializationAlgorithm => DiagnosticHeadAssetStore.Algorithm;
    public int AlgorithmVersion => DiagnosticHeadAssetStore.AlgorithmVersion;
    public string InitializationDescriptorDomain => DiagnosticHeadAssetStore.DescriptorDomain;
    public string InitializationDescriptorSha256 { get; }
    public string FileSha256 { get; }
    public LinearMarkerHead Head { get; }

    internal DiagnosticHeadAsset(DiagnosticHeadIdentity identity, int seed, string descriptorHash,
        string fileHash, LinearMarkerHead head)
    {
        Identity = identity; Seed = seed; InitializationDescriptorSha256 = descriptorHash;
        FileSha256 = fileHash; Head = head;
    }
}

/// <summary>Separate diagnostic wire format. Its descriptor hash is never a training feature-cache hash.</summary>
public static class DiagnosticHeadAssetStore
{
    public const string Algorithm = "xorshift32-f32-v1";
    public const int AlgorithmVersion = 1;
    public const string DescriptorDomain = "sezika/diagnostic-head-initialization/v1";
    private static readonly byte[] Magic = "SZDH1"u8.ToArray();
    private const int MaxBytes = 128 * 1024;
    private const int MaxHiddenSize = 2048;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static DiagnosticHeadAsset CreateNew(string path, DiagnosticHeadIdentity identity, int seed,
        CancellationToken cancellationToken = default) => CreateCore(path, identity, seed, null, cancellationToken);

    internal static DiagnosticHeadAsset CreateForPublicationChecks(string path, DiagnosticHeadIdentity identity,
        int seed, HeadAssetPublicationHooks hooks, CancellationToken cancellationToken = default)
        => CreateCore(path, identity, seed, hooks, cancellationToken);

    private static DiagnosticHeadAsset CreateCore(string path, DiagnosticHeadIdentity identity, int seed,
        HeadAssetPublicationHooks? hooks, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(identity);
        var cleanupDuration = hooks?.CleanupDuration ?? TimeSpan.FromSeconds(5);
        if (cleanupDuration < TimeSpan.Zero || cleanupDuration > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(hooks));
        var clock = Stopwatch.StartNew();
        void Check() => CheckBudget(clock, cancellationToken);
        Check();
        var canonical = Normalize(identity);
        var weights = Initialize(canonical.HiddenSize, seed, Check);
        var descriptorHash = Descriptor(canonical, seed, Check);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            writer.Write(Magic); writer.Write(1);
            writer.Write((int)DiagnosticHeadOrigin.UntrainedDiagnostic);
            writer.Write(0); // CompletedUpdates.
            writer.Write((byte)0); // ObservedLoss absent; no numeric sentinel is presented as an observation.
            WriteIdentity(writer, canonical);
            WriteText(writer, Algorithm); writer.Write(AlgorithmVersion); writer.Write(seed);
            WriteText(writer, DescriptorDomain); writer.Write(Convert.FromHexString(descriptorHash));
            writer.Write(weights.Length);
            for (var index = 0; index < weights.Length && index < 3 * MaxHiddenSize; index++)
            {
                if ((index & 255) == 0) Check();
                writer.Write(weights[index]);
            }
        }
        Check();
        if (stream.Length + 32 > MaxBytes) throw Invalid("Diagnostic asset exceeds its file bound.");
        var payload = stream.ToArray();
        byte[] bytes = [.. payload, .. SHA256.HashData(payload)];
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
        // Prepare the complete return value before the publisher acquires files.
        var result = new DiagnosticHeadAsset(canonical, seed, descriptorHash, fileHash,
            new LinearMarkerHead(canonical.HiddenSize, weights));
        HeadAssetFilePublisher.PublishNew(path, bytes, fileHash, Check, cleanupDuration, hooks);
        return result;
    }

    public static DiagnosticHeadAsset Load(string path, DiagnosticHeadIdentity expectedIdentity, int expectedSeed,
        string expectedInitializationDescriptorSha256, string expectedFileSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(expectedIdentity);
        if (!IsHash(expectedInitializationDescriptorSha256) || !IsHash(expectedFileSha256))
            throw new DecisionException("diagnostic_head_hash_invalid", "Diagnostic descriptor and file SHA256 are required.");
        var clock = Stopwatch.StartNew();
        void Check() => CheckBudget(clock, cancellationToken);
        Check();
        var expected = Normalize(expectedIdentity);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 96 or > MaxBytes) throw Invalid("Diagnostic asset size is invalid.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes); Check();
        var actualFileHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!actualFileHash.Equals(expectedFileSha256, StringComparison.OrdinalIgnoreCase) ||
            !SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
            throw new DecisionException("diagnostic_head_hash_invalid", "Diagnostic file hash or checksum differs.");
        try
        {
            using var stream = new MemoryStream(bytes, 0, bytes.Length - 32, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic) || reader.ReadInt32() != 1)
                throw Invalid("Diagnostic asset magic or schema version is invalid.");
            if (reader.ReadInt32() != (int)DiagnosticHeadOrigin.UntrainedDiagnostic ||
                reader.ReadInt32() != 0 || reader.ReadByte() != 0)
                throw Invalid("Diagnostic origin, zero updates or absent observed loss differs.");
            var identity = new DiagnosticHeadIdentity(ReadText(reader), ReadText(reader), ReadText(reader),
                reader.ReadInt32(), ReadText(reader), ReadText(reader));
            var canonical = Normalize(identity);
            if (canonical != identity) throw Invalid("Diagnostic encoder binding is not in canonical form.");
            if (canonical != expected)
                throw new DecisionException("diagnostic_head_identity_mismatch", "Diagnostic encoder binding differs from the expected identity.");
            var algorithm = ReadText(reader); var algorithmVersion = reader.ReadInt32(); var seed = reader.ReadInt32();
            var domain = ReadText(reader);
            if (algorithm != Algorithm || algorithmVersion != AlgorithmVersion || domain != DescriptorDomain)
                throw Invalid("Diagnostic initialization algorithm/version/hash domain is unsupported.");
            if (seed != expectedSeed)
                throw new DecisionException("diagnostic_head_initialization_mismatch", "Diagnostic seed differs from the expected initialization.");
            var descriptorBytes = reader.ReadBytes(32);
            if (descriptorBytes.Length != 32) throw new EndOfStreamException();
            var descriptorHash = Convert.ToHexString(descriptorBytes);
            if (!descriptorHash.Equals(expectedInitializationDescriptorSha256, StringComparison.OrdinalIgnoreCase) ||
                !descriptorHash.Equals(Descriptor(canonical, seed, Check), StringComparison.Ordinal))
                throw new DecisionException("diagnostic_head_initialization_mismatch", "Diagnostic canonical descriptor differs.");
            var count = reader.ReadInt32();
            if (count != checked(3 * canonical.HiddenSize)) throw Invalid("Diagnostic head shape is invalid.");
            var weights = new float[count];
            var regenerated = Initialize(canonical.HiddenSize, seed, Check);
            for (var index = 0; index < count && index < 3 * MaxHiddenSize; index++)
            {
                if ((index & 255) == 0) Check();
                weights[index] = reader.ReadSingle();
                if (!float.IsFinite(weights[index])) throw Invalid("Diagnostic head weight is non-finite.");
                if (BitConverter.SingleToInt32Bits(weights[index]) != BitConverter.SingleToInt32Bits(regenerated[index]))
                    throw new DecisionException("diagnostic_head_initialization_mismatch", "Diagnostic weight differs from its seeded initialization.");
            }
            if (stream.Position != stream.Length) throw Invalid("Diagnostic asset has trailing payload.");
            Check();
            return new DiagnosticHeadAsset(canonical, seed, descriptorHash, actualFileHash,
                new LinearMarkerHead(canonical.HiddenSize, weights));
        }
        catch (Exception error) when (error is EndOfStreamException or DecoderFallbackException or OverflowException)
        {
            throw Invalid("Diagnostic asset is malformed.");
        }
    }

    private static DiagnosticHeadIdentity Normalize(DiagnosticHeadIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.ModelId) || identity.ModelId.Length > 128 ||
            !identity.ModelId.StartsWith("sezika/", StringComparison.Ordinal) ||
            StrictUtf8.GetByteCount(identity.ModelId) > 256 ||
            !IsHash(identity.EncoderSha256) || !IsHash(identity.TokenizerSha256) ||
            identity.HiddenSize is < 1 or > MaxHiddenSize || identity.Protocol != "decision-v1" || identity.LengthPolicy != "strict")
            throw new DecisionException("diagnostic_head_identity_invalid", "Diagnostic encoder binding is invalid.");
        return identity with { EncoderSha256 = identity.EncoderSha256.ToUpperInvariant(), TokenizerSha256 = identity.TokenizerSha256.ToUpperInvariant() };
    }

    private static float[] Initialize(int hiddenSize, int seed, Action check)
    {
        var weights = new float[checked(3 * hiddenSize)];
        uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
        if (state == 0) state = 0xA341316Cu;
        for (var index = 0; index < weights.Length && index < 3 * MaxHiddenSize; index++)
        {
            if ((index & 255) == 0) check();
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            weights[index] = ((int)(state % 101u) - 50) / 10000f;
        }
        check();
        return weights;
    }

    private static string Descriptor(DiagnosticHeadIdentity identity, int seed, Action check)
    {
        check();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            // Domain-separated canonical UTF8/little-endian descriptor. No dataset/feature digest exists here.
            WriteText(writer, DescriptorDomain); writer.Write(1);
            WriteIdentity(writer, identity);
            writer.Write((int)DiagnosticHeadOrigin.UntrainedDiagnostic); writer.Write(0); writer.Write((byte)0);
            WriteText(writer, Algorithm); writer.Write(AlgorithmVersion); writer.Write(seed);
            writer.Write(3); writer.Write(identity.HiddenSize);
        }
        if (stream.Length > 4096) throw Invalid("Diagnostic descriptor exceeds its bound.");
        check();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }

    private static void WriteIdentity(BinaryWriter writer, DiagnosticHeadIdentity identity)
    {
        WriteText(writer, identity.ModelId); WriteText(writer, identity.EncoderSha256); WriteText(writer, identity.TokenizerSha256);
        writer.Write(identity.HiddenSize); WriteText(writer, identity.Protocol); WriteText(writer, identity.LengthPolicy);
    }

    private static void WriteText(BinaryWriter writer, string text)
    {
        var bytes = StrictUtf8.GetBytes(text);
        if (bytes.Length is < 1 or > 256) throw Invalid("Diagnostic text exceeds its byte bound.");
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    private static string ReadText(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 1 or > 256) throw Invalid("Diagnostic text length is invalid.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return StrictUtf8.GetString(bytes);
    }

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void CheckBudget(Stopwatch clock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clock.Elapsed > MaxDuration)
            throw new DecisionException("diagnostic_head_deadline_exceeded", "Diagnostic head I/O/initialization deadline expired.");
    }

    private static DecisionException Invalid(string message) => new("diagnostic_head_asset_invalid", message);
}
