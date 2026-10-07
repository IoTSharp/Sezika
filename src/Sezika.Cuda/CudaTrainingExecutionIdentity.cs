using System.Diagnostics;
using System.Security.Cryptography;

namespace Sezika.Cuda;

public sealed record CudaKernelArtifactIdentity(string Name, string Entry, int BlockSize, int ParameterCount, string Sha256);
public sealed record CudaTrainingArtifactsIdentity(string SourceSha256, string ManifestSha256, string Compiler,
    string CompilerVersion, string Target, string PtxVersion, IReadOnlyList<CudaKernelArtifactIdentity> Kernels);

/// <summary>Reads build identities and hashes the exact static PTX spans loaded by CudaKernels. It never opens a CUDA device.</summary>
public static class CudaTrainingExecutionIdentity
{
    public static CudaTrainingArtifactsIdentity Read(CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        void Check() { cancellationToken.ThrowIfCancellationRequested(); if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Static CUDA identity deadline."); }
        var artifacts = new List<CudaKernelArtifactIdentity>(17);
        void Add(string name, GeneratedKernelArtifacts.Descriptor descriptor, ReadOnlySpan<byte> ptx)
        {
            Check();
            if (artifacts.Count >= 17 || ptx.Length is < 1 or > 1_048_576 || ptx[^1] != 0) throw new InvalidDataException("Static PTX identity bound or termination is invalid.");
            artifacts.Add(new(name, descriptor.Entry, descriptor.BlockSize, descriptor.ParameterCount, Convert.ToHexString(SHA256.HashData(ptx))));
        }
        Check();
        Add("VectorAdd", GeneratedKernelArtifacts.VectorAddDescriptor, CudaPtx.VectorAdd);
        Add("Linear", GeneratedKernelArtifacts.LinearDescriptor, CudaPtx.Linear);
        Add("Embedding", GeneratedKernelArtifacts.EmbeddingDescriptor, CudaPtx.Embedding);
        Add("Norm", GeneratedKernelArtifacts.NormDescriptor, CudaPtx.Norm);
        Add("SplitQkv", GeneratedKernelArtifacts.SplitQkvDescriptor, CudaPtx.SplitQkv);
        Add("Rope", GeneratedKernelArtifacts.RopeDescriptor, CudaPtx.Rope);
        Add("AttentionScores", GeneratedKernelArtifacts.AttentionScoresDescriptor, CudaPtx.AttentionScores);
        Add("Softmax", GeneratedKernelArtifacts.SoftmaxDescriptor, CudaPtx.Softmax);
        Add("AttentionContext", GeneratedKernelArtifacts.AttentionContextDescriptor, CudaPtx.AttentionContext);
        Add("Add", GeneratedKernelArtifacts.AddDescriptor, CudaPtx.Add);
        Add("Activate", GeneratedKernelArtifacts.ActivateDescriptor, CudaPtx.Activate);
        Add("GatedGelu", GeneratedKernelArtifacts.GatedGeluDescriptor, CudaPtx.GatedGelu);
        Add("RowBias", GeneratedKernelArtifacts.RowBiasDescriptor, CudaPtx.RowBias);
        Add("TypeEmbeddingAdd", GeneratedKernelArtifacts.TypeEmbeddingAddDescriptor, CudaPtx.TypeEmbeddingAdd);
        Add("MarkerCrossEntropy", GeneratedKernelArtifacts.MarkerCrossEntropyDescriptor, CudaPtx.MarkerCrossEntropy);
        Add("MarkerGradientSgd", GeneratedKernelArtifacts.MarkerGradientSgdDescriptor, CudaPtx.MarkerGradientSgd);
        Add("MarkerUpdateStatus", GeneratedKernelArtifacts.MarkerUpdateStatusDescriptor, CudaPtx.MarkerUpdateStatus);
        Check();
        return new(GeneratedKernelArtifacts.SourceSha256, GeneratedKernelArtifacts.ManifestSha256, "ILGPU", "1.5.3", "sm_70", "7.0", artifacts.AsReadOnly());
    }
}
