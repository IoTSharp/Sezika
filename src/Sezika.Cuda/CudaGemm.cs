namespace Sezika.Cuda;

/// <summary>Runs a fixed row-major FP32 GEMM probe through the CUDA Driver.</summary>
public sealed class CudaGemm : IDisposable
{
    private readonly CudaDevice _device;
    private readonly CudaModuleHandle _module;
    private readonly IntPtr _function;
    private bool _disposed;

    internal CudaGemm(CudaDevice device)
    {
        _device = device;
        _module = device.LoadModule(CudaPtx.Gemm);
        _function = device.GetFunction(_module, "gemm");
    }

    /// <summary>Computes C = A[M,K] x B[K,N] with row-major FP32 tensors.</summary>
    public void Execute(
        ReadOnlySpan<float> left,
        int rows,
        int shared,
        ReadOnlySpan<float> right,
        int columns,
        Span<float> destination)
    {
        ThrowIfDisposed();
        if (rows <= 0 || shared <= 0 || columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "GEMM dimensions must be positive.");
        }

        int leftLength = checked(rows * shared);
        int rightLength = checked(shared * columns);
        int destinationLength = checked(rows * columns);
        if (left.Length != leftLength || right.Length != rightLength || destination.Length != destinationLength)
        {
            throw new ArgumentException("GEMM spans do not match the supplied dimensions.");
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
            uint rowCount = checked((uint)rows);
            uint columnCount = checked((uint)columns);
            uint sharedCount = checked((uint)shared);
            nint* parameters = stackalloc nint[6];
            parameters[0] = (nint)(&leftPointer);
            parameters[1] = (nint)(&rightPointer);
            parameters[2] = (nint)(&destinationPointer);
            parameters[3] = (nint)(&rowCount);
            parameters[4] = (nint)(&columnCount);
            parameters[5] = (nint)(&sharedCount);
            const uint blockSize = 16;
            _device.Launch(
                _function,
                checked((uint)((rows + (long)blockSize - 1L) / blockSize)),
                checked((uint)((columns + (long)blockSize - 1L) / blockSize)),
                blockSize,
                blockSize,
                new ReadOnlySpan<nint>(parameters, 6));
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
