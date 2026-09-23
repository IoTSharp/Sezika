namespace Sezika.Cuda;

/// <summary>Build-generated PTX images. The compiler and manifest are in Generated.</summary>
internal static class CudaPtx
{
    internal static ReadOnlySpan<byte> VectorAdd => GeneratedKernelArtifacts.VectorAdd;
    internal static ReadOnlySpan<byte> Linear => GeneratedKernelArtifacts.Linear;
    internal static ReadOnlySpan<byte> Embedding => GeneratedKernelArtifacts.Embedding;
    internal static ReadOnlySpan<byte> Norm => GeneratedKernelArtifacts.Norm;
    internal static ReadOnlySpan<byte> SplitQkv => GeneratedKernelArtifacts.SplitQkv;
    internal static ReadOnlySpan<byte> Rope => GeneratedKernelArtifacts.Rope;
    internal static ReadOnlySpan<byte> AttentionScores => GeneratedKernelArtifacts.AttentionScores;
    internal static ReadOnlySpan<byte> Softmax => GeneratedKernelArtifacts.Softmax;
    internal static ReadOnlySpan<byte> AttentionContext => GeneratedKernelArtifacts.AttentionContext;
    internal static ReadOnlySpan<byte> Add => GeneratedKernelArtifacts.Add;
    internal static ReadOnlySpan<byte> Activate => GeneratedKernelArtifacts.Activate;
    internal static ReadOnlySpan<byte> GatedGelu => GeneratedKernelArtifacts.GatedGelu;
    internal static ReadOnlySpan<byte> RowBias => GeneratedKernelArtifacts.RowBias;
    internal static ReadOnlySpan<byte> TypeEmbeddingAdd => GeneratedKernelArtifacts.TypeEmbeddingAdd;
}
