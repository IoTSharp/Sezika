namespace Sezika.Cuda;

/// <summary>Identity reported by the installed CUDA Driver, independent of model metadata.</summary>
public sealed record CudaDeviceInfo(string Name, int DriverVersion, int ComputeCapabilityMajor,
    int ComputeCapabilityMinor, ulong TotalMemoryBytes);

/// <summary>Device-wide free memory and allocations owned by this context. Driver caches and other processes affect free memory.</summary>
public sealed record CudaMemorySnapshot(ulong TotalBytes, ulong FreeBytes, ulong OwnedBytes,
    ulong PeakOwnedBytes, int OwnedAllocationCount, int LoadedModuleCount, long ReleaseFailureCount);

/// <summary>Successful operations since the last reset. Copy/module durations use the host clock; kernel durations use CUDA events.</summary>
/// <remarks>Profiling synchronizes each launch and changes scheduling. Measure ordinary end-to-end latency with profiling disabled.</remarks>
public sealed record CudaTelemetrySnapshot(bool ProfilingEnabled, double HostToDeviceMilliseconds,
    ulong HostToDeviceBytes, double DeviceToHostMilliseconds, ulong DeviceToHostBytes,
    double ModuleLoadMilliseconds, double KernelMilliseconds, long KernelLaunchCount);
