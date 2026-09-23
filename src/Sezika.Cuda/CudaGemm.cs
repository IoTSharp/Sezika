namespace Sezika.Cuda;

/// <summary>Runs row-major FP32 GEMM through the generated Linear PTX kernel.</summary>
public sealed class CudaGemm : IDisposable
{
    private readonly CudaDevice _device;
    private readonly CudaKernels _kernels;
    private bool _disposed;

    internal CudaGemm(CudaDevice device)
    {
        _device = device;
        _kernels = new CudaKernels(device);
    }

    public void Execute(ReadOnlySpan<float> left, int rows, int shared, ReadOnlySpan<float> right, int columns, Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (rows <= 0 || shared <= 0 || columns <= 0 || left.Length != checked(rows * shared) ||
            right.Length != checked(shared * columns) || destination.Length != checked(rows * columns))
            throw new ArgumentException("GEMM spans do not match the supplied dimensions.");
        using var leftBuffer = _device.Allocate(checked((nuint)left.Length * sizeof(float)));
        using var rightBuffer = _device.Allocate(checked((nuint)right.Length * sizeof(float)));
        using var outputBuffer = _device.Allocate(checked((nuint)destination.Length * sizeof(float)));
        _device.CopyToDevice(leftBuffer, left);
        _device.CopyToDevice(rightBuffer, right);
        _kernels.Linear(leftBuffer, rightBuffer, outputBuffer, rows, shared, columns, rhsTransposed: false);
        _device.CopyFromDevice(destination, outputBuffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _kernels.Dispose();
    }
}
