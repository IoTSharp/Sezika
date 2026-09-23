namespace Sezika.Cuda;

/// <summary>Runs a fixed C#-owned vector-add probe through the CUDA Driver.</summary>
public sealed class CudaVectorAdd : IDisposable
{
    private readonly CudaDevice _device;
    private readonly CudaModuleHandle _module;
    private readonly IntPtr _function;
    private bool _disposed;

    internal CudaVectorAdd(CudaDevice device)
    {
        _device = device;
        _module = device.LoadModule(CudaPtx.VectorAdd);
        _function = device.GetFunction(_module, "vector_add");
    }

    public void Execute(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> destination)
    {
        ThrowIfDisposed();
        if (left.Length == 0 || left.Length != right.Length || left.Length != destination.Length)
        {
            throw new ArgumentException("Vector operands must have the same non-zero length.");
        }

        using CudaDeviceBuffer leftBuffer = _device.Allocate(checked((nuint)(left.Length * sizeof(float))));
        using CudaDeviceBuffer rightBuffer = _device.Allocate(checked((nuint)(right.Length * sizeof(float))));
        using CudaDeviceBuffer destinationBuffer = _device.Allocate(checked((nuint)(destination.Length * sizeof(float))));
        _device.CopyToDevice(leftBuffer, left);
        _device.CopyToDevice(rightBuffer, right);

        unsafe
        {
            ulong leftPointer = leftBuffer.DevicePointer;
            ulong rightPointer = rightBuffer.DevicePointer;
            ulong destinationPointer = destinationBuffer.DevicePointer;
            uint length = checked((uint)left.Length);
            nint* parameters = stackalloc nint[4];
            parameters[0] = (nint)(&leftPointer);
            parameters[1] = (nint)(&rightPointer);
            parameters[2] = (nint)(&destinationPointer);
            parameters[3] = (nint)(&length);
            _device.Launch(_function, checked((uint)((left.Length + 255L) / 256L)), 1, 256, 1, new ReadOnlySpan<nint>(parameters, 4));
        }

        _device.Synchronize();
        _device.CopyFromDevice(destination, destinationBuffer);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.ThrowIfDisposed();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _module.Dispose();
        }
    }
}
