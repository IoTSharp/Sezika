using System.Diagnostics;
using System.Text;

namespace Sezika.Cuda;

/// <summary>Owns one CUDA context for the lifetime of a managed session.</summary>
/// <remarks>Operations on one device must be serialized by the caller. Operations bind this context to the calling thread.</remarks>
public sealed class CudaDevice : IDisposable
{
    private readonly CudaContextHandle _context;
    private readonly List<CudaModuleHandle> _modules = [];
    private readonly HashSet<CudaDeviceBuffer> _buffers = [];
    private CudaEventHandle? _kernelStart;
    private CudaEventHandle? _kernelEnd;
    private bool _profilingEnabled;
    private ulong _ownedBytes, _peakOwnedBytes, _hostToDeviceBytes, _deviceToHostBytes;
    private double _hostToDeviceMilliseconds, _deviceToHostMilliseconds, _moduleLoadMilliseconds, _kernelMilliseconds;
    private long _kernelLaunchCount, _releaseFailureCount;
    private CudaException? _lastReleaseFailure;
    private bool _disposed;
    private bool _disposingResources;

    private CudaDevice(int deviceIndex, CudaContextHandle context, CudaDeviceInfo info, bool enableProfiling)
    {
        DeviceIndex = deviceIndex;
        _context = context;
        Info = info;
        _profilingEnabled = enableProfiling;
    }

    public int DeviceIndex { get; }

    public CudaDeviceInfo Info { get; }

    /// <summary>Enables host copy/module timers and CUDA-event kernel timers. Disabled by default.</summary>
    /// <remarks>Each profiled kernel is synchronized; use a separate unprofiled pass for end-to-end measurements.</remarks>
    public bool ProfilingEnabled
    {
        get => _profilingEnabled;
        set
        {
            ThrowIfDisposed();
            _profilingEnabled = value;
        }
    }

    public CudaTelemetrySnapshot GetTelemetry() => new(_profilingEnabled,
        _hostToDeviceMilliseconds, _hostToDeviceBytes, _deviceToHostMilliseconds, _deviceToHostBytes,
        _moduleLoadMilliseconds, _kernelMilliseconds, _kernelLaunchCount);

    /// <summary>Resets timing counters and the allocation peak to the currently owned byte count.</summary>
    public void ResetTelemetry()
    {
        ThrowIfDisposed();
        _hostToDeviceBytes = _deviceToHostBytes = 0;
        _hostToDeviceMilliseconds = _deviceToHostMilliseconds = _moduleLoadMilliseconds = _kernelMilliseconds = 0;
        _kernelLaunchCount = 0;
        _peakOwnedBytes = _ownedBytes;
    }

    public CudaMemorySnapshot GetMemorySnapshot()
    {
        MakeCurrent();
        CudaNative.Check(CudaNative.MemoryGetInfo(out nuint free, out nuint total), "cuMemGetInfo");
        return new((ulong)total, (ulong)free, _ownedBytes, _peakOwnedBytes,
            _buffers.Count, _modules.Count, _releaseFailureCount);
    }

    public static bool IsAvailable => CudaNative.IsDriverAvailable();

    public static int GetDeviceCount()
    {
        CudaNative.Check(CudaNative.Init(0), "cuInit");
        CudaNative.Check(CudaNative.DeviceGetCount(out int count), "cuDeviceGetCount");
        return count;
    }

    public static CudaDevice Open(int deviceIndex = 0) => Open(deviceIndex, enableProfiling: false);

    public static CudaDevice Open(bool enableProfiling) => Open(0, enableProfiling);

    public static CudaDevice Open(int deviceIndex, bool enableProfiling)
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
            CudaDeviceInfo info = ReadDeviceInfo(device);
            CudaNative.Check(CudaNative.ContextCreate(out IntPtr context, 0, device), "cuCtxCreate");
            var handle = new CudaContextHandle(context);
            try
            {
                return new CudaDevice(deviceIndex, handle, info, enableProfiling);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
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

    private static unsafe CudaDeviceInfo ReadDeviceInfo(int device)
    {
        byte* name = stackalloc byte[256];
        new Span<byte>(name, 256).Clear();
        CudaNative.Check(CudaNative.DeviceGetName(name, 256, device), "cuDeviceGetName");
        var bytes = new ReadOnlySpan<byte>(name, 256);
        int terminator = bytes.IndexOf((byte)0);
        string deviceName = Encoding.UTF8.GetString(terminator < 0 ? bytes : bytes[..terminator]);
        CudaNative.Check(CudaNative.DriverGetVersion(out int driver), "cuDriverGetVersion");
        CudaNative.Check(CudaNative.DeviceGetAttribute(out int major, CudaNative.AttributeComputeCapabilityMajor, device), "cuDeviceGetAttribute(major)");
        CudaNative.Check(CudaNative.DeviceGetAttribute(out int minor, CudaNative.AttributeComputeCapabilityMinor, device), "cuDeviceGetAttribute(minor)");
        CudaNative.Check(CudaNative.DeviceTotalMemory(out nuint total, device), "cuDeviceTotalMem");
        return new(deviceName, driver, major, minor, (ulong)total);
    }

    public CudaVectorAdd CreateVectorAdd() => new(this);

    public CudaGemm CreateGemm() => new(this);

    public void Synchronize()
    {
        MakeCurrent();
        CudaNative.Check(CudaNative.ContextSynchronize(), "cuCtxSynchronize");
    }

    internal CudaModuleHandle LoadModule(ReadOnlySpan<byte> image)
    {
        MakeCurrent();
        if (image.IsEmpty || image[^1] != 0)
        {
            throw new ArgumentException("PTX image must be non-empty and NUL terminated.", nameof(image));
        }

        unsafe
        {
            fixed (byte* imagePointer = image)
            {
                long started = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
                CudaNative.Check(CudaNative.ModuleLoadData(out IntPtr module, (nint)imagePointer), "cuModuleLoadData");
                if (_profilingEnabled) _moduleLoadMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var handle = new CudaModuleHandle(module, this);
                _modules.Add(handle);
                return handle;
            }
        }
    }

    internal IntPtr GetFunction(CudaModuleHandle module, string name)
    {
        MakeCurrent();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CudaNative.Check(CudaNative.ModuleGetFunction(out IntPtr function, module, name), $"cuModuleGetFunction({name})");
        return function;
    }

    internal CudaDeviceBuffer Allocate(nuint bytes)
    {
        MakeCurrent();
        if (bytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "A CUDA allocation must be non-zero.");
        }

        CudaNative.Check(CudaNative.MemAlloc(out ulong pointer, bytes), "cuMemAlloc");
        var buffer = new CudaDeviceBuffer(pointer, bytes, this);
        _buffers.Add(buffer);
        _ownedBytes = checked(_ownedBytes + (ulong)bytes);
        _peakOwnedBytes = Math.Max(_peakOwnedBytes, _ownedBytes);
        return buffer;
    }

    internal unsafe void CopyToDevice(CudaDeviceBuffer destination, ReadOnlySpan<float> source)
    {
        MakeCurrent();
        nuint bytes = checked((nuint)(source.Length * sizeof(float)));
        EnsureFits(destination, bytes);
        fixed (float* sourcePointer = source)
        {
            long started = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
            CudaNative.Check(CudaNative.MemcpyHtoD(destination.DevicePointer, (nint)sourcePointer, bytes), "cuMemcpyHtoD");
            RecordHostToDevice(started, bytes);
        }
    }

    internal unsafe void CopyToDevice(CudaDeviceBuffer destination, ReadOnlySpan<int> source)
    {
        MakeCurrent();
        nuint bytes = checked((nuint)(source.Length * sizeof(int)));
        EnsureFits(destination, bytes);
        fixed (int* sourcePointer = source)
        {
            long started = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
            CudaNative.Check(CudaNative.MemcpyHtoD(destination.DevicePointer, (nint)sourcePointer, bytes), "cuMemcpyHtoD");
            RecordHostToDevice(started, bytes);
        }
    }

    internal unsafe void CopyFromDevice(Span<float> destination, CudaDeviceBuffer source)
    {
        MakeCurrent();
        nuint bytes = checked((nuint)(destination.Length * sizeof(float)));
        EnsureFits(source, bytes);
        fixed (float* destinationPointer = destination)
        {
            long started = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
            CudaNative.Check(CudaNative.MemcpyDtoH((nint)destinationPointer, source.DevicePointer, bytes), "cuMemcpyDtoH");
            if (_profilingEnabled)
            {
                _deviceToHostMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _deviceToHostBytes += (ulong)bytes;
            }
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
        MakeCurrent();
        if (parameterPointers.IsEmpty)
        {
            throw new ArgumentException("At least one kernel parameter is required.", nameof(parameterPointers));
        }

        nint* parameters = stackalloc nint[parameterPointers.Length];
        parameterPointers.CopyTo(new Span<nint>(parameters, parameterPointers.Length));
        if (_profilingEnabled)
        {
            EnsureTimingEvents();
            CudaNative.Check(CudaNative.EventRecord(_kernelStart!, IntPtr.Zero), "cuEventRecord(start)");
        }
        CudaNative.Check(
            CudaNative.LaunchKernel(function, gridX, gridY, 1, blockX, blockY, 1, 0, IntPtr.Zero, parameters, null),
            "cuLaunchKernel");
        if (_profilingEnabled)
        {
            CudaNative.Check(CudaNative.EventRecord(_kernelEnd!, IntPtr.Zero), "cuEventRecord(end)");
            CudaNative.Check(CudaNative.EventSynchronize(_kernelEnd!), "cuEventSynchronize");
            CudaNative.Check(CudaNative.EventElapsedTime(out float milliseconds, _kernelStart!, _kernelEnd!), "cuEventElapsedTime");
            _kernelMilliseconds += milliseconds;
            _kernelLaunchCount++;
        }
    }

    private void RecordHostToDevice(long started, nuint bytes)
    {
        if (!_profilingEnabled) return;
        _hostToDeviceMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _hostToDeviceBytes += (ulong)bytes;
    }

    private void EnsureTimingEvents()
    {
        if (_kernelStart is not null) return;
        CudaNative.Check(CudaNative.EventCreate(out IntPtr start, 0), "cuEventCreate(start)");
        var startHandle = new CudaEventHandle(start, this);
        try
        {
            CudaNative.Check(CudaNative.EventCreate(out IntPtr end, 0), "cuEventCreate(end)");
            _kernelEnd = new CudaEventHandle(end, this);
            _kernelStart = startHandle;
        }
        catch
        {
            startHandle.Dispose();
            throw;
        }
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

    private void MakeCurrent()
    {
        ThrowIfDisposed();
        CudaNative.Check(CudaNative.ContextSetCurrent(_context.DangerousGetHandle()), "cuCtxSetCurrent");
    }

    internal bool ReleaseBuffer(CudaDeviceBuffer buffer, ulong pointer)
    {
        if (_context.IsClosed || _context.IsInvalid) return true;
        int result = CudaNative.ContextSetCurrent(_context.DangerousGetHandle());
        if (result == CudaNative.Success) result = CudaNative.MemFree(pointer);
        RecordReleaseResult(result, "cuMemFree");
        if (result != CudaNative.Success) return false;
        bool tracked = _disposingResources ? _buffers.Contains(buffer) : _buffers.Remove(buffer);
        if (tracked) _ownedBytes -= (ulong)buffer.ByteLength;
        return true;
    }

    internal bool ReleaseModule(CudaModuleHandle module, IntPtr pointer)
    {
        if (_context.IsClosed || _context.IsInvalid) return true;
        int result = CudaNative.ContextSetCurrent(_context.DangerousGetHandle());
        if (result == CudaNative.Success) result = CudaNative.ModuleUnload(pointer);
        RecordReleaseResult(result, "cuModuleUnload");
        if (result == CudaNative.Success && !_disposingResources) _modules.Remove(module);
        return result == CudaNative.Success;
    }

    internal bool ReleaseEvent(IntPtr pointer)
    {
        if (_context.IsClosed || _context.IsInvalid) return true;
        int result = CudaNative.ContextSetCurrent(_context.DangerousGetHandle());
        if (result == CudaNative.Success) result = CudaNative.EventDestroy(pointer);
        RecordReleaseResult(result, "cuEventDestroy");
        return result == CudaNative.Success;
    }

    private void RecordReleaseResult(int result, string operation)
    {
        if (result == CudaNative.Success) return;
        _releaseFailureCount++;
        _lastReleaseFailure = new CudaException("cuda_resource_release_failed",
            $"CUDA resource release '{operation}' failed with CUresult {result}.", result, operation: operation);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposingResources = true;
        try
        {
            // Release callbacks leave these finite collections intact during teardown; no snapshot allocation is needed.
            foreach (CudaDeviceBuffer buffer in _buffers) buffer.Dispose();
            foreach (CudaModuleHandle module in _modules) module.Dispose();
        }
        finally
        {
            // Every subsequent owner is still released when an earlier cleanup throws, including under managed OOM.
            try
            {
                _kernelStart?.Dispose();
            }
            finally
            {
                try
                {
                    _kernelEnd?.Dispose();
                }
                finally
                {
                    try
                    {
                        _context.Dispose();
                    }
                    finally
                    {
                        _disposingResources = false;
                        if (_context.ReleaseResult == CudaNative.Success)
                        {
                            _buffers.Clear();
                            _modules.Clear();
                            _ownedBytes = 0;
                        }
                        RecordReleaseResult(_context.ReleaseResult, "cuCtxDestroy");
                    }
                }
            }
        }
        if (_lastReleaseFailure is not null) throw _lastReleaseFailure;
    }
}
