using System.Diagnostics;

namespace Sezika.Cuda;

internal readonly record struct CudaOwnedResources(ulong Bytes, int Buffers, int Modules, long ReleaseFailures);

internal readonly record struct TrainingCleanupResult(CudaOwnedResources Before, CudaOwnedResources After,
    int AttemptedOwners, int AttemptedModules, bool DeadlineReached, bool DeadlineInjected, Exception? FirstException,
    bool RestoreCallerBaseline, bool OwnerLimitReached)
{
    internal bool Incomplete => DeadlineReached || OwnerLimitReached || FirstException is not null ||
        Before.ReleaseFailures != After.ReleaseFailures || (RestoreCallerBaseline &&
        (Before.Bytes != After.Bytes || Before.Buffers != After.Buffers || Before.Modules != After.Modules));
}

/// <summary>One recovery deadline for buffers and nested modules; native calls remain non-preemptible.</summary>
internal sealed class TrainingCleanup
{
    private readonly CudaDevice _device;
    private readonly TrainingCleanupFaults? _faults;
    private readonly CudaOwnedResources _before;
    private readonly bool _restoreCallerBaseline;
    private long _started;
    private bool _clockStarted;
    private int _owners, _modules;
    private bool _expired, _injected, _ownerLimitReached;
    private Exception? _firstException;

    internal TrainingCleanup(CudaDevice device, bool restoreCallerBaseline = true)
    {
        _device = device;
        _faults = device.TrainingFaults;
        _before = device.OwnedResources;
        _restoreCallerBaseline = restoreCallerBaseline;
    }

    internal TrainingCleanupResult Result
    {
        get
        {
            // A final non-preemptible call may itself overrun the budget; observe that on return too.
            if (_clockStarted) WithinDeadline();
            return new(_before, _device.OwnedResources,
                _owners, _modules, _expired, _injected, _firstException, _restoreCallerBaseline, _ownerLimitReached);
        }
    }

    private bool WithinDeadline()
    {
        if (!_clockStarted) { _started = Stopwatch.GetTimestamp(); _clockStarted = true; }
        if (_faults?.ExpireCleanup(_owners) == true) _injected = true;
        if (_injected || Stopwatch.GetElapsedTime(_started) >= TimeSpan.FromSeconds(5)) _expired = true;
        return !_expired;
    }

    private bool NextOwner()
    {
        if (!WithinDeadline()) return false;
        if (_owners >= 40) { _ownerLimitReached = true; return false; }
        _owners++;
        return true;
    }

    internal void Buffers(List<CudaDeviceBuffer>? buffers)
    {
        if (buffers is null) return;
        // Allocate rejects a 40th buffer. If internal corruption violates that invariant,
        // still attempt at most 39 known owners and report the excess to the device.
        if (buffers.Count > 39) _ownerLimitReached = true;
        for (var index = Math.Min(buffers.Count, 39) - 1; index >= 0; index--)
        {
            if (!NextOwner()) break;
            try { buffers[index].Dispose(); }
            catch (Exception error) { Record(error); }
        }
    }

    internal void Kernels(CudaKernels? kernels)
    {
        if (kernels is null || !NextOwner()) return;
        try { kernels.Dispose(this); }
        catch (Exception error) { Record(error); }
    }

    internal void PartialKernels(CudaModuleHandle[] modules)
    {
        if (NextOwner()) Modules(modules);
    }

    internal void Modules(CudaModuleHandle[] modules)
    {
        if (modules.Length > 17) _ownerLimitReached = true;
        for (var index = 0; index < modules.Length && index < 17; index++)
        {
            if (modules[index] is not { } module) continue;
            if (!WithinDeadline()) break;
            if (_modules >= 17) { _ownerLimitReached = true; break; }
            _modules++;
            try { module.Dispose(); }
            catch (Exception error) { Record(error); }
        }
    }

    internal void Record(Exception error) => _firstException ??= error;
    internal void Publish() => _device.LastTrainingCleanup = Result;
}

internal enum TrainingFaultPoint { None, InitialCreated, PackedBeforeAllocate, ModuleLoaded }

/// <summary>Internal, per-device, single-use diagnostics. Production never installs this object.</summary>
internal sealed class TrainingCleanupFaults
{
    private readonly TrainingFaultPoint _point;
    private readonly Exception? _failure;
    private readonly int _hit;
    private int _hits;
    private int? _bufferResult, _moduleResult, _expireAfterOwners;
    internal float[]? ObservedInitial { get; private set; }
    internal float[]? ObservedPacked { get; private set; }
    internal bool BufferFailureConsumed { get; private set; }
    internal bool ModuleFailureConsumed { get; private set; }

    internal TrainingCleanupFaults(TrainingFaultPoint point = TrainingFaultPoint.None, Exception? failure = null,
        int hit = 1, int? bufferResult = null, int? moduleResult = null, int? expireAfterOwners = null)
    {
        if ((uint)point > (uint)TrainingFaultPoint.ModuleLoaded || hit is < 1 or > 17 ||
            (point != TrainingFaultPoint.None && failure is null) ||
            bufferResult is <= 0 or > 1_000_000 || moduleResult is <= 0 or > 1_000_000 ||
            expireAfterOwners is < 0 or > 40)
            throw new ArgumentException("Diagnostic faults require bounded stages and non-success CUresults.");
        _point = point; _failure = failure; _hit = hit;
        _bufferResult = bufferResult; _moduleResult = moduleResult; _expireAfterOwners = expireAfterOwners;
    }

    internal void Reach(TrainingFaultPoint point, float[]? array = null)
    {
        if (point == TrainingFaultPoint.InitialCreated) ObservedInitial = array;
        if (point == TrainingFaultPoint.PackedBeforeAllocate) ObservedPacked = array;
        if (_point == point && ++_hits == _hit) throw _failure!;
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

    internal bool ExpireCleanup(int owners)
    {
        if (_expireAfterOwners is not { } maximum || owners < maximum) return false;
        _expireAfterOwners = null; return true;
    }
}
