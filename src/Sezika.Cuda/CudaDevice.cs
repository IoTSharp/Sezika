using System.Runtime.CompilerServices;

namespace Sezika.Cuda;

/// <summary>Owns one CUDA primary context for the lifetime of a managed session.</summary>
public sealed class CudaDevice : IDisposable
{
    private readonly CudaContextHandle _context;
    private readonly List<CudaModuleHandle> _modules = [];
    private bool _disposed;

    private CudaDevice(int deviceIndex, CudaContextHandle context)
    {
        DeviceIndex = deviceIndex;
        _context = context;
    }

    public int DeviceIndex { get; }

    public static bool IsAvailable => CudaNative.IsDriverAvailable();

    public static int GetDeviceCount()
    {
        CudaNative.Check(CudaNative.Init(0), "cuInit");
        CudaNative.Check(CudaNative.DeviceGetCount(out int count), "cuDeviceGetCount");
        return count;
    }

    public static CudaDevice Open(int deviceIndex = 0)
    {
        if (deviceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        }

        try
        {
            CudaNative.Check(CudaNative.Init(0), "cuInit");
            CudaNative.Check(CudaNative.DeviceGetCount(out int count), "cuDeviceGetCount");
            if (count == 0)
            {
                throw new CudaException("cuda_no_device", "CUDA Driver reported no CUDA devices.", CudaNative.ErrorNoDevice);
            }

            if (deviceIndex >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(deviceIndex), deviceIndex, $"CUDA device count is {count}.");
            }

            CudaNative.Check(CudaNative.DeviceGet(out int device, deviceIndex), "cuDeviceGet");
            CudaNative.Check(CudaNative.ContextCreate(out IntPtr context, 0, device), "cuCtxCreate");
            return new CudaDevice(deviceIndex, new CudaContextHandle(context));
        }
        catch (DllNotFoundException ex)
        {
            throw new CudaException("cuda_driver_missing", "The NVIDIA CUDA Driver library could not be loaded.", innerException: ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new CudaException("cuda_driver_incompatible", "The NVIDIA CUDA Driver does not expose the required entry point.", innerException: ex);
        }
    }

    public CudaVectorAdd CreateVectorAdd() => new(this);

    public CudaGemm CreateGemm() => new(this);

    public void Synchronize()
    {
        ThrowIfDisposed();
        CudaNative.Check(CudaNative.ContextSynchronize(), "cuCtxSynchronize");
    }

    internal CudaModuleHandle LoadModule(ReadOnlySpan<byte> image)
    {
        ThrowIfDisposed();
        if (image.IsEmpty || image[^1] != 0)
        {
            throw new ArgumentException("PTX image must be non-empty and NUL terminated.", nameof(image));
        }

        unsafe
        {
            fixed (byte* imagePointer = image)
            {
                CudaNative.Check(CudaNative.ModuleLoadData(out IntPtr module, (nint)imagePointer), "cuModuleLoadData");
                var handle = new CudaModuleHandle(module);
                _modules.Add(handle);
                return handle;
            }
        }
    }

    internal IntPtr GetFunction(CudaModuleHandle module, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CudaNative.Check(CudaNative.ModuleGetFunction(out IntPtr function, module, name), $"cuModuleGetFunction({name})");
        return function;
    }

    internal CudaDeviceBuffer Allocate(nuint bytes)
    {
        ThrowIfDisposed();
        if (bytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "A CUDA allocation must be non-zero.");
        }

        CudaNative.Check(CudaNative.MemAlloc(out ulong pointer, bytes), "cuMemAlloc");
        return new CudaDeviceBuffer(pointer, bytes);
    }

    internal unsafe void CopyToDevice(CudaDeviceBuffer destination, ReadOnlySpan<float> source)
    {
        ThrowIfDisposed();
        nuint bytes = checked((nuint)(source.Length * sizeof(float)));
        EnsureFits(destination, bytes);
        fixed (float* sourcePointer = source)
        {
            CudaNative.Check(CudaNative.MemcpyHtoD(destination.DevicePointer, (nint)sourcePointer, bytes), "cuMemcpyHtoD");
        }
    }

    internal unsafe void CopyFromDevice(Span<float> destination, CudaDeviceBuffer source)
    {
        ThrowIfDisposed();
        nuint bytes = checked((nuint)(destination.Length * sizeof(float)));
        EnsureFits(source, bytes);
        fixed (float* destinationPointer = destination)
        {
            CudaNative.Check(CudaNative.MemcpyDtoH((nint)destinationPointer, source.DevicePointer, bytes), "cuMemcpyDtoH");
        }
    }

    internal unsafe void Launch(
        IntPtr function,
        uint gridX,
        uint gridY,
        uint blockX,
        uint blockY,
        ReadOnlySpan<nint> parameterPointers)
    {
        ThrowIfDisposed();
        if (parameterPointers.IsEmpty)
        {
            throw new ArgumentException("At least one kernel parameter is required.", nameof(parameterPointers));
        }

        nint* parameters = stackalloc nint[parameterPointers.Length];
        parameterPointers.CopyTo(new Span<nint>(parameters, parameterPointers.Length));
        CudaNative.Check(
            CudaNative.LaunchKernel(function, gridX, gridY, 1, blockX, blockY, 1, 0, IntPtr.Zero, parameters, null),
            "cuLaunchKernel");
    }

    private static void EnsureFits(CudaDeviceBuffer buffer, nuint bytes)
    {
        if (bytes > buffer.ByteLength)
        {
            throw new ArgumentException("The host buffer is larger than the CUDA allocation.", nameof(bytes));
        }
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || _context.IsClosed || _context.IsInvalid, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (CudaModuleHandle module in _modules)
        {
            module.Dispose();
        }

        _modules.Clear();
        _context.Dispose();
    }
}
