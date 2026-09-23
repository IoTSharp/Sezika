using Microsoft.Win32.SafeHandles;

namespace Sezika.Cuda;

internal sealed class CudaContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal CudaContextHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => CudaNative.ContextDestroy(handle) == CudaNative.Success;
}

internal sealed class CudaModuleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal CudaModuleHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => CudaNative.ModuleUnload(handle) == CudaNative.Success;
}

internal sealed class CudaDeviceBuffer : IDisposable
{
    private ulong _devicePointer;

    internal CudaDeviceBuffer(ulong devicePointer, nuint byteLength)
    {
        _devicePointer = devicePointer;
        ByteLength = byteLength;
    }

    internal nuint ByteLength { get; }

    internal ulong DevicePointer => _devicePointer != 0
        ? _devicePointer
        : throw new ObjectDisposedException(nameof(CudaDeviceBuffer));

    public void Dispose()
    {
        ulong devicePointer = Interlocked.Exchange(ref _devicePointer, 0);
        if (devicePointer != 0)
        {
            _ = CudaNative.MemFree(devicePointer);
        }
    }
}
