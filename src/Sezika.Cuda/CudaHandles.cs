using Microsoft.Win32.SafeHandles;

namespace Sezika.Cuda;

internal sealed class CudaContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal CudaContextHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    // A native invocation that throws must not be reported as a successful release.
    internal int ReleaseResult { get; private set; } = -1;

    protected override bool ReleaseHandle()
    {
        ReleaseResult = CudaNative.ContextDestroy(handle);
        return ReleaseResult == CudaNative.Success;
    }
}

internal sealed class CudaModuleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly CudaDevice _owner;

    internal CudaModuleHandle(IntPtr handle, CudaDevice owner)
        : base(ownsHandle: true)
    {
        _owner = owner;
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => _owner.ReleaseModule(this, handle);
}

internal sealed class CudaEventHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly CudaDevice _owner;

    internal CudaEventHandle(IntPtr handle, CudaDevice owner)
        : base(ownsHandle: true)
    {
        _owner = owner;
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => _owner.ReleaseEvent(handle);
}

internal sealed class CudaDeviceBuffer : IDisposable
{
    private ulong _devicePointer;
    private readonly CudaDevice _owner;

    internal CudaDeviceBuffer(ulong devicePointer, nuint byteLength, CudaDevice owner)
    {
        _devicePointer = devicePointer;
        _owner = owner;
        ByteLength = byteLength;
    }

    internal nuint ByteLength { get; }

    internal ulong DevicePointer => _devicePointer != 0
        ? _devicePointer
        : throw new ObjectDisposedException(nameof(CudaDeviceBuffer));

    public void Dispose()
    {
        ulong devicePointer = _devicePointer;
        if (devicePointer != 0 && _owner.ReleaseBuffer(this, devicePointer))
        {
            _devicePointer = 0;
        }
    }
}
