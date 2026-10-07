using Sezika;

namespace Sezika.Cuda;

/// <summary>Explicit independent CUDA factory. Missing/incompatible CUDA or assets fail without backend fallback.</summary>
public static class IndependentCudaRuntime
{
    /// <remarks>The returned runtime owns both model and pipeline; the caller retains the supplied device.</remarks>
    public static IndependentDecisionV1Runtime Load(CudaDevice device, string packageDirectory, string headPath,
        MarkerFeatureIdentity identity, string featureSha256, string headSha256,
        IndependentDecisionRuntimeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var model = IndependentModelLoader.Load(packageDirectory, cancellationToken);
        IndependentCudaDecisionPipeline? pipeline = null;
        try
        {
            var head = IndependentMarkerHeadAssetStore.Load(headPath, identity, featureSha256, headSha256, cancellationToken);
            pipeline = new IndependentCudaDecisionPipeline(device, model, head, cancellationToken);
            return new IndependentDecisionV1Runtime(model, head, options, pipeline);
        }
        catch
        {
            try { pipeline?.Dispose(); }
            finally { model.Dispose(); }
            throw;
        }
    }
}
