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
        try
        {
            _function = device.GetFunction(_module, GeneratedKernelArtifacts.VectorAddDescriptor.Entry);
        }
        catch
        {
            _module.Dispose();
            throw;
        }
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
            CudaArrayView leftView = new(leftBuffer.DevicePointer, left.Length);
            CudaArrayView rightView = new(rightBuffer.DevicePointer, right.Length);
            CudaArrayView destinationView = new(destinationBuffer.DevicePointer, destination.Length);
            uint length = checked((uint)left.Length);
            nint* parameters = stackalloc nint[4];
            parameters[0] = (nint)(&leftView);
            parameters[1] = (nint)(&rightView);
            parameters[2] = (nint)(&destinationView);
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
