using System.Diagnostics;
using System.Text;

namespace Sezika.Cuda;

/// <summary>Owns one CUDA context for the lifetime of a managed session.</summary>
/// <remarks>All operations, snapshots and disposal on one device must be serialized by the caller. Operations bind this context to the calling thread.</remarks>
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
    private bool _contextAdopted, _timingEventsReady, _releaseDiagnosticUnavailable;
    private int _pendingBuffers, _pendingModules, _ownedEventCount;
    private int _lastReleaseResult;
    private ResourceReleaseOperation _lastReleaseOperation;

    private CudaDevice(int deviceIndex, CudaContextHandle context, CudaDeviceInfo info, bool enableProfiling)
    {
        DeviceIndex = deviceIndex;
        _context = context;
        Info = info;
        _profilingEnabled = enableProfiling;
    }

    public int DeviceIndex { get; }

    public CudaDeviceInfo Info { get; }

    // Pure managed ownership evidence remains readable after native context destruction.
    internal CudaOwnedResources OwnedResources => new(_ownedBytes, _buffers.Count - _pendingBuffers,
        _modules.Count - _pendingModules, _releaseFailureCount);
    internal ResourceAdoptionSnapshot AdoptionSnapshot => new(_contextAdopted, _pendingBuffers,
        _pendingModules, _ownedEventCount, _lastReleaseResult, _lastReleaseOperation, _releaseDiagnosticUnavailable);
    internal ResourceAdoptionFaults? AdoptionFaults { get; private set; }
    internal void InstallAdoptionFaults(ResourceAdoptionFaults faults)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(faults);
        if (AdoptionFaults is not null) throw new InvalidOperationException("An adoption diagnostic fault is already installed.");
        AdoptionFaults = faults;
        faults.Observe(this);
    }
    internal void ResetAdoptionFaults() => AdoptionFaults = null;
    internal bool ContextDestroySucceeded => _context.IsClosed && _context.ReleaseResult == CudaNative.Success;
    internal TrainingCleanupResult? LastTrainingCleanup { get; set; }
    internal TrainingCleanupFaults? TrainingFaults { get; private set; }
    internal void InstallTrainingFaults(TrainingCleanupFaults faults)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(faults);
        if (TrainingFaults is not null) throw new InvalidOperationException("A diagnostic fault is already installed.");
        TrainingFaults = faults;
    }
    internal void ResetTrainingFaults() => TrainingFaults = null;

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
            _buffers.Count - _pendingBuffers, _modules.Count - _pendingModules, _releaseFailureCount);
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

    public static CudaDevice Open(int deviceIndex, bool enableProfiling) => OpenCore(deviceIndex, enableProfiling, null);

    internal static CudaDevice OpenForAdoptionDiagnostics(int deviceIndex, bool enableProfiling, ResourceAdoptionFaults faults)
    {
        ArgumentNullException.ThrowIfNull(faults);
        return OpenCore(deviceIndex, enableProfiling, faults);
    }

    private static CudaDevice OpenCore(int deviceIndex, bool enableProfiling, ResourceAdoptionFaults? faults)
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
            var handle = new CudaContextHandle();
            var owner = new CudaDevice(deviceIndex, handle, info, enableProfiling);
            owner.AdoptionFaults = faults;
            faults?.Observe(owner);
            try
            {
                faults?.Reach(ResourceAdoptionPoint.ContextPending);
                int result = CudaNative.ContextCreate(out IntPtr context, 0, device);
                if (result == CudaNative.Success)
                {
                    handle.Adopt(context);
                    owner._contextAdopted = true;
                }
                else CudaNative.Check(result, "cuCtxCreate"); // Failure out-values have no ownership semantics.
                faults?.Reach(ResourceAdoptionPoint.ContextAdopted);
                return owner;
            }
            catch
            {
                // Cleanup may fail; retain the exact primary exception and managed ownership evidence.
                try { owner.Dispose(); } catch { }
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
        byte* uuid = stackalloc byte[16];
        new Span<byte>(uuid, 16).Clear();
        CudaNative.Check(CudaNative.DeviceGetUuid(uuid, device), "cuDeviceGetUuid");
        return new(deviceName, driver, major, minor, (ulong)total)
        {
            Uuid = Convert.ToHexString(new ReadOnlySpan<byte>(uuid, 16)),
        };
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

        int previousPending = _pendingModules;
        int nextPending = checked(previousPending + 1);
        _modules.EnsureCapacity(checked(_modules.Count + 1));
        var handle = new CudaModuleHandle(this);
        _modules.Add(handle);
        _pendingModules = nextPending;
        bool adopted = false;
        try
        {
            AdoptionFaults?.Reach(ResourceAdoptionPoint.ModulePending);
            unsafe
            {
                fixed (byte* imagePointer = image)
                {
                    long started = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
                    int result = CudaNative.ModuleLoadData(out IntPtr module, (nint)imagePointer);
                    if (result == CudaNative.Success)
                    {
                        handle.Adopt(module);
                        _pendingModules = previousPending;
                        adopted = true;
                    }
                    else CudaNative.Check(result, "cuModuleLoadData");
                    AdoptionFaults?.Reach(ResourceAdoptionPoint.ModuleAdopted);
                    if (_profilingEnabled) _moduleLoadMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    return handle;
                }
            }
        }
        catch
        {
            if (!adopted) { _modules.Remove(handle); _pendingModules = previousPending; }
            try { handle.Dispose(); } catch { }
            throw;
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

        ulong nextBytes = ResourceAdoption.CheckedOwnedBytes(_ownedBytes, bytes);
        ulong nextPeak = Math.Max(_peakOwnedBytes, nextBytes);
        int previousPending = _pendingBuffers;
        int nextPending = checked(previousPending + 1);
        _buffers.EnsureCapacity(checked(_buffers.Count + 1));
        var buffer = new CudaDeviceBuffer(bytes, this);
        _buffers.Add(buffer);
        _pendingBuffers = nextPending;
        bool adopted = false;
        try
        {
            AdoptionFaults?.Reach(ResourceAdoptionPoint.BufferPending);
            int result = CudaNative.MemAlloc(out ulong pointer, bytes);
            if (result == CudaNative.Success)
            {
                buffer.Adopt(pointer);
                _pendingBuffers = previousPending;
                _ownedBytes = nextBytes;
                _peakOwnedBytes = nextPeak;
                adopted = true;
            }
            else CudaNative.Check(result, "cuMemAlloc");
            AdoptionFaults?.Reach(ResourceAdoptionPoint.BufferAdopted);
            return buffer;
        }
        catch
        {
            if (!adopted) { _buffers.Remove(buffer); _pendingBuffers = previousPending; }
            try { buffer.Dispose(); } catch { }
            throw;
        }
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
        if (_timingEventsReady) return;
        if (_kernelStart is not null || _kernelEnd is not null)
            throw new CudaException("cuda_profiling_resources_retained", "A failed profiling-event release requires context teardown.");
        var startHandle = new CudaEventHandle(this);
        var endHandle = new CudaEventHandle(this);
        _kernelStart = startHandle;
        _kernelEnd = endHandle;
        try
        {
            AdoptionFaults?.Reach(ResourceAdoptionPoint.TimingEventsPending);
            int startResult = CudaNative.EventCreate(out IntPtr start, 0);
            if (startResult == CudaNative.Success)
            {
                startHandle.Adopt(start);
                _ownedEventCount = 1;
            }
            else CudaNative.Check(startResult, "cuEventCreate(start)");
            AdoptionFaults?.Reach(ResourceAdoptionPoint.StartEventAdopted);
            int endResult = CudaNative.EventCreate(out IntPtr end, 0);
            if (endResult == CudaNative.Success)
            {
                endHandle.Adopt(end);
                _ownedEventCount = 2;
                _timingEventsReady = true;
            }
            else CudaNative.Check(endResult, "cuEventCreate(end)");
            AdoptionFaults?.Reach(ResourceAdoptionPoint.EndEventAdopted);
        }
        catch
        {
            _timingEventsReady = false;
            try { startHandle.Dispose(); } catch { }
            try { endHandle.Dispose(); } catch { }
            if (startHandle.IsInvalid) _kernelStart = null;
            if (endHandle.IsInvalid) _kernelEnd = null;
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
        if (result == CudaNative.Success)
            result = AdoptionFaults is { } adoption && adoption.TakeBufferFailure(out int adoptionResult) ? adoptionResult :
                TrainingFaults is { } faults && faults.TakeBufferFailure(out int injected) ? injected : CudaNative.MemFree(pointer);
        RecordReleaseResult(result, ResourceReleaseOperation.Buffer);
        if (result != CudaNative.Success) return false;
        bool tracked = _disposingResources ? _buffers.Contains(buffer) : _buffers.Remove(buffer);
        if (tracked) _ownedBytes -= (ulong)buffer.ByteLength;
        return true;
    }

    internal bool ReleaseModule(CudaModuleHandle module, IntPtr pointer)
    {
        if (_context.IsClosed || _context.IsInvalid) return true;
        int result = CudaNative.ContextSetCurrent(_context.DangerousGetHandle());
        if (result == CudaNative.Success)
            result = AdoptionFaults is { } adoption && adoption.TakeModuleFailure(out int adoptionResult) ? adoptionResult :
                TrainingFaults is { } faults && faults.TakeModuleFailure(out int injected) ? injected : CudaNative.ModuleUnload(pointer);
        RecordReleaseResult(result, ResourceReleaseOperation.Module);
        if (result == CudaNative.Success && !_disposingResources) _modules.Remove(module);
        return result == CudaNative.Success;
    }

    internal bool ReleaseEvent(CudaEventHandle owner, IntPtr pointer)
    {
        if (_context.IsClosed || _context.IsInvalid) return true;
        int result = CudaNative.ContextSetCurrent(_context.DangerousGetHandle());
        if (result == CudaNative.Success)
            result = AdoptionFaults is { } faults && faults.TakeEventFailure(out int injected) ? injected : CudaNative.EventDestroy(pointer);
        RecordReleaseResult(result, ResourceReleaseOperation.Event);
        if (result == CudaNative.Success)
        {
            _ownedEventCount--;
            if (ReferenceEquals(_kernelStart, owner)) _kernelStart = null;
            if (ReferenceEquals(_kernelEnd, owner)) _kernelEnd = null;
        }
        return result == CudaNative.Success;
    }

    private void RecordReleaseResult(int result, ResourceReleaseOperation operation)
    {
        if (result == CudaNative.Success) return;
        // Persist primitive evidence first. Diagnostic allocation must never decide whether an owner is retained.
        _lastReleaseResult = result;
        _lastReleaseOperation = operation;
        if (_releaseFailureCount != long.MaxValue) _releaseFailureCount++;
        _lastReleaseFailure = null;
        try
        {
            AdoptionFaults?.Reach(ResourceAdoptionPoint.ReleaseDiagnostic);
            _lastReleaseFailure = CreateReleaseFailure();
        }
        catch (OutOfMemoryException) { _releaseDiagnosticUnavailable = true; }
    }

    private CudaException CreateReleaseFailure()
    {
        string operation = _lastReleaseOperation switch
        {
            ResourceReleaseOperation.Buffer => "cuMemFree", ResourceReleaseOperation.Module => "cuModuleUnload",
            ResourceReleaseOperation.Event => "cuEventDestroy", ResourceReleaseOperation.Context => "cuCtxDestroy",
            _ => "unknown",
        };
        return new CudaException("cuda_resource_release_failed",
            $"CUDA resource release '{operation}' failed with CUresult {_lastReleaseResult}.",
            _lastReleaseResult, operation: operation);
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
                        if (!_contextAdopted || _context.ReleaseResult == CudaNative.Success)
                        {
                            _buffers.Clear();
                            _modules.Clear();
                            _ownedBytes = 0;
                            _ownedEventCount = 0;
                            _kernelStart = _kernelEnd = null;
                            _timingEventsReady = false;
                        }
                        if (_contextAdopted) RecordReleaseResult(_context.ReleaseResult, ResourceReleaseOperation.Context);
                    }
                }
            }
        }
        if (_releaseFailureCount != 0) throw _lastReleaseFailure ?? CreateReleaseFailure();
    }
}
