namespace Sezika.Cuda;

/// <summary>Static launch wrappers for the ILGPU-generated Transformer kernels.</summary>
internal sealed class CudaKernels : IDisposable
{
    private readonly CudaDevice _device;
    private readonly CudaModuleHandle[] _modules;
    private readonly IntPtr _vectorAdd, _linear, _embedding, _norm, _splitQkv, _rope;
    private readonly IntPtr _attentionScores, _softmax, _attentionContext, _add, _activate, _gatedGelu, _rowBias, _typeEmbeddingAdd;
    private bool _disposed;

    internal CudaKernels(CudaDevice device)
    {
        _device = device;
        var images = new (byte[] Image, string Entry)[]
        {
            (CudaPtx.VectorAdd.ToArray(), GeneratedKernelArtifacts.VectorAddDescriptor.Entry), (CudaPtx.Linear.ToArray(), GeneratedKernelArtifacts.LinearDescriptor.Entry), (CudaPtx.Embedding.ToArray(), GeneratedKernelArtifacts.EmbeddingDescriptor.Entry),
            (CudaPtx.Norm.ToArray(), GeneratedKernelArtifacts.NormDescriptor.Entry), (CudaPtx.SplitQkv.ToArray(), GeneratedKernelArtifacts.SplitQkvDescriptor.Entry), (CudaPtx.Rope.ToArray(), GeneratedKernelArtifacts.RopeDescriptor.Entry),
            (CudaPtx.AttentionScores.ToArray(), GeneratedKernelArtifacts.AttentionScoresDescriptor.Entry), (CudaPtx.Softmax.ToArray(), GeneratedKernelArtifacts.SoftmaxDescriptor.Entry), (CudaPtx.AttentionContext.ToArray(), GeneratedKernelArtifacts.AttentionContextDescriptor.Entry),
            (CudaPtx.Add.ToArray(), GeneratedKernelArtifacts.AddDescriptor.Entry), (CudaPtx.Activate.ToArray(), GeneratedKernelArtifacts.ActivateDescriptor.Entry), (CudaPtx.GatedGelu.ToArray(), GeneratedKernelArtifacts.GatedGeluDescriptor.Entry),
            (CudaPtx.RowBias.ToArray(), GeneratedKernelArtifacts.RowBiasDescriptor.Entry), (CudaPtx.TypeEmbeddingAdd.ToArray(), GeneratedKernelArtifacts.TypeEmbeddingAddDescriptor.Entry),
        };
        _modules = new CudaModuleHandle[images.Length];
        var functions = new IntPtr[images.Length];
        try
        {
            for (var i = 0; i < images.Length; i++)
            {
                _modules[i] = device.LoadModule(images[i].Image);
                functions[i] = device.GetFunction(_modules[i], images[i].Entry);
            }
        }
        catch
        {
            foreach (var module in _modules)
                module?.Dispose();
            throw;
        }
        (_vectorAdd, _linear, _embedding, _norm, _splitQkv, _rope, _attentionScores, _softmax, _attentionContext, _add, _activate, _gatedGelu, _rowBias, _typeEmbeddingAdd) =
            (functions[0], functions[1], functions[2], functions[3], functions[4], functions[5], functions[6], functions[7], functions[8], functions[9], functions[10], functions[11], functions[12], functions[13]);
    }

    internal void VectorAdd(CudaDeviceBuffer a, CudaDeviceBuffer b, CudaDeviceBuffer output, int count) => Launch(_vectorAdd, count, [KernelArg.View(a), KernelArg.View(b), KernelArg.View(output), KernelArg.Int(count)]);

    internal void Linear(CudaDeviceBuffer input, CudaDeviceBuffer weights, CudaDeviceBuffer output, int rows, int shared, int columns, bool rhsTransposed = true) =>
        Launch(_linear, checked(rows * columns), [KernelArg.View(input), KernelArg.View(weights), KernelArg.View(output), KernelArg.Int(rows), KernelArg.Int(shared), KernelArg.Int(columns), KernelArg.Int(rhsTransposed ? 1 : 0)]);

    internal void Embedding(CudaDeviceBuffer ids, CudaDeviceBuffer weights, CudaDeviceBuffer output, int rows, int hidden) =>
        Launch(_embedding, checked(rows * hidden), [KernelArg.View(ids), KernelArg.View(weights), KernelArg.View(output), KernelArg.Int(rows), KernelArg.Int(hidden)]);

    internal void Norm(CudaDeviceBuffer input, CudaDeviceBuffer gamma, CudaDeviceBuffer? beta, CudaDeviceBuffer output, int rows, int hidden, float epsilon) =>
        Launch(_norm, rows, [KernelArg.View(input), KernelArg.View(gamma), KernelArg.View(beta), KernelArg.View(output), KernelArg.Int(rows), KernelArg.Int(hidden), KernelArg.Float(epsilon), KernelArg.Int(beta is null ? 0 : 1)]);

    internal void SplitQkv(CudaDeviceBuffer fused, CudaDeviceBuffer q, CudaDeviceBuffer k, CudaDeviceBuffer v, int rows, int hidden) =>
        Launch(_splitQkv, checked(rows * hidden), [KernelArg.View(fused), KernelArg.View(q), KernelArg.View(k), KernelArg.View(v), KernelArg.Int(rows), KernelArg.Int(hidden)]);

    internal void Rope(CudaDeviceBuffer q, CudaDeviceBuffer k, int rows, int heads, int headDimension, float theta) =>
        Launch(_rope, checked(rows * heads * (headDimension / 2)), [KernelArg.View(q), KernelArg.View(k), KernelArg.Int(rows), KernelArg.Int(heads), KernelArg.Int(headDimension), KernelArg.Float(theta)]);

    internal void AttentionScores(CudaDeviceBuffer q, CudaDeviceBuffer k, CudaDeviceBuffer scores, int rows, int heads, int headDimension, int localWindow) =>
        Launch(_attentionScores, checked(heads * rows * rows), [KernelArg.View(q), KernelArg.View(k), KernelArg.View(scores), KernelArg.Int(rows), KernelArg.Int(heads), KernelArg.Int(headDimension), KernelArg.Int(localWindow)]);

    internal void Softmax(CudaDeviceBuffer scores, int rows, int columns) => Launch(_softmax, rows, [KernelArg.View(scores), KernelArg.Int(rows), KernelArg.Int(columns)]);

    internal void AttentionContext(CudaDeviceBuffer scores, CudaDeviceBuffer v, CudaDeviceBuffer output, int rows, int heads, int headDimension) =>
        Launch(_attentionContext, checked(rows * heads * headDimension), [KernelArg.View(scores), KernelArg.View(v), KernelArg.View(output), KernelArg.Int(rows), KernelArg.Int(heads), KernelArg.Int(headDimension)]);

    internal void Add(CudaDeviceBuffer input, CudaDeviceBuffer update, int count) => Launch(_add, count, [KernelArg.View(input), KernelArg.View(update), KernelArg.Int(count)]);

    internal void Activate(CudaDeviceBuffer input, CudaDeviceBuffer output, int count, bool relu = false) => Launch(_activate, count, [KernelArg.View(input), KernelArg.View(output), KernelArg.Int(count), KernelArg.Int(relu ? 1 : 0)]);

    internal void GatedGelu(CudaDeviceBuffer input, CudaDeviceBuffer output, int rows, int intermediate) => Launch(_gatedGelu, checked(rows * intermediate), [KernelArg.View(input), KernelArg.View(output), KernelArg.Int(rows), KernelArg.Int(intermediate)]);

    internal void RowBias(CudaDeviceBuffer input, CudaDeviceBuffer bias, int rows, int hidden) => Launch(_rowBias, checked(rows * hidden), [KernelArg.View(input), KernelArg.View(bias), KernelArg.Int(rows), KernelArg.Int(hidden)]);

    internal void TypeEmbeddingAdd(CudaDeviceBuffer input, CudaDeviceBuffer weights, int rows, int hidden, int typeId) => Launch(_typeEmbeddingAdd, checked(rows * hidden), [KernelArg.View(input), KernelArg.View(weights), KernelArg.Int(rows), KernelArg.Int(hidden), KernelArg.Int(typeId)]);

    private unsafe void Launch(IntPtr function, int work, ReadOnlySpan<KernelArg> arguments)
    {
        ThrowIfDisposed();
        if (work <= 0) throw new ArgumentOutOfRangeException(nameof(work));
        Span<byte> storage = stackalloc byte[checked(arguments.Length * 16)];
        nint* parameterPointers = stackalloc nint[arguments.Length];
        fixed (byte* storagePointer = storage)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                byte* address = storagePointer + i * 16;
                parameterPointers[i] = (nint)address;
                arguments[i].Write(address);
            }
            _device.Launch(function, checked((uint)((work + 255L) / 256L)), 1, 256, 1, new ReadOnlySpan<nint>(parameterPointers, arguments.Length));
        }
        _device.Synchronize();
    }

    private void ThrowIfDisposed() { ObjectDisposedException.ThrowIf(_disposed, this); _device.ThrowIfDisposed(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var module in _modules) module.Dispose();
    }

    private readonly struct KernelArg
    {
        private readonly CudaArrayView _view;
        private readonly int _integer;
        private readonly float _float;
        private readonly byte _kind;
        private KernelArg(CudaArrayView view) { _view = view; _integer = 0; _float = 0; _kind = 0; }
        private KernelArg(int integer) { _view = default; _integer = integer; _float = 0; _kind = 1; }
        private KernelArg(float value) { _view = default; _integer = 0; _float = value; _kind = 2; }
        internal static KernelArg View(CudaDeviceBuffer? buffer) => new(buffer is null ? default : new CudaArrayView(buffer.DevicePointer, checked((long)(buffer.ByteLength / (nuint)sizeof(float)))));
        internal static KernelArg Int(int value) => new(value);
        internal static KernelArg Float(float value) => new(value);
        internal unsafe void Write(byte* destination)
        {
            if (_kind == 0) *(CudaArrayView*)destination = _view;
            else if (_kind == 1) *(int*)destination = _integer;
            else *(float*)destination = _float;
        }
    }
}
