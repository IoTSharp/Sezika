using System.Runtime.CompilerServices;

namespace Sezika.Cuda;

internal enum ResourceAdoptionPoint
{
    None, ContextPending, ContextAdopted, BufferPending, BufferAdopted,
    ModulePending, ModuleAdopted, TimingEventsPending, StartEventAdopted, EndEventAdopted,
    ReleaseDiagnostic,
}

internal enum ResourceReleaseOperation { None, Buffer, Module, Event, Context }

internal readonly record struct ResourceAdoptionSnapshot(bool ContextAdopted, int PendingBuffers,
    int PendingModules, int OwnedEvents, int LastReleaseResult, ResourceReleaseOperation LastReleaseOperation,
    bool ReleaseDiagnosticUnavailable);

internal static class ResourceAdoption
{
    internal static ulong CheckedOwnedBytes(ulong current, nuint bytes) => checked(current + (ulong)bytes);
}

/// <summary>Per-device, finite, single-use diagnostic injection; absent in production.</summary>
/// <remarks>A prebuilt OOM sentinel does not demonstrate actual managed heap exhaustion.</remarks>
internal sealed class ResourceAdoptionFaults
{
    private readonly ResourceAdoptionPoint _point;
    private readonly Exception? _failure;
    private int? _bufferResult, _moduleResult, _eventResult;

    internal ResourceAdoptionFaults(ResourceAdoptionPoint point = ResourceAdoptionPoint.None,
        Exception? failure = null, int? bufferResult = null, int? moduleResult = null, int? eventResult = null)
    {
        if ((uint)point > (uint)ResourceAdoptionPoint.ReleaseDiagnostic ||
            (point == ResourceAdoptionPoint.None) != (failure is null) ||
            (point == ResourceAdoptionPoint.ReleaseDiagnostic && failure is not OutOfMemoryException) ||
            bufferResult is <= 0 or > 1_000_000 || moduleResult is <= 0 or > 1_000_000 ||
            eventResult is <= 0 or > 1_000_000)
            throw new ArgumentException("Adoption diagnostics require a bounded point, matching exception, and non-success CUresults.");
        _point = point; _failure = failure;
        _bufferResult = bufferResult; _moduleResult = moduleResult; _eventResult = eventResult;
    }

    internal CudaDevice? ObservedDevice { get; private set; }
    internal bool FaultConsumed { get; private set; }
    internal bool BufferFailureConsumed { get; private set; }
    internal bool ModuleFailureConsumed { get; private set; }
    internal bool EventFailureConsumed { get; private set; }
    internal void Observe(CudaDevice device) => ObservedDevice = device;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Reach(ResourceAdoptionPoint point)
    {
        if (FaultConsumed || point != _point || point == ResourceAdoptionPoint.None) return;
        FaultConsumed = true;
        throw _failure!;
    }

    internal bool TakeBufferFailure(out int result)
    {
        result = _bufferResult ?? 0;
        if (_bufferResult is null) return false;
        _bufferResult = null; BufferFailureConsumed = true; return true;
    }

    internal bool TakeModuleFailure(out int result)
    {
        result = _moduleResult ?? 0;
        if (_moduleResult is null) return false;
        _moduleResult = null; ModuleFailureConsumed = true; return true;
    }

    internal bool TakeEventFailure(out int result)
    {
        result = _eventResult ?? 0;
        if (_eventResult is null) return false;
        _eventResult = null; EventFailureConsumed = true; return true;
    }
}
