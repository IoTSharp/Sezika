using Microsoft.Win32.SafeHandles;

namespace Sezika.Cuda;

internal sealed class CudaContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal CudaContextHandle()
        : base(ownsHandle: true)
    {
    }

    // Called exactly once on a fresh, open placeholder under the caller's serial contract.
    // Direct assignment is the no-allocation/no-throw native-success adoption boundary.
    internal void Adopt(IntPtr value) => handle = value;

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

    internal CudaModuleHandle(CudaDevice owner)
        : base(ownsHandle: true)
    {
        _owner = owner;
    }

    internal void Adopt(IntPtr value) => handle = value;

    protected override bool ReleaseHandle() => _owner.ReleaseModule(this, handle);
}

internal sealed class CudaEventHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly CudaDevice _owner;

    internal CudaEventHandle(CudaDevice owner)
        : base(ownsHandle: true)
    {
        _owner = owner;
    }

    internal void Adopt(IntPtr value) => handle = value;

    protected override bool ReleaseHandle() => _owner.ReleaseEvent(this, handle);
}

internal sealed class CudaDeviceBuffer : IDisposable
{
    private ulong _devicePointer;
    private readonly CudaDevice _owner;

    internal CudaDeviceBuffer(nuint byteLength, CudaDevice owner)
    {
        _owner = owner;
        ByteLength = byteLength;
    }

    internal nuint ByteLength { get; }

    internal bool HasNativePointer => _devicePointer != 0;

    internal void Adopt(ulong value) => _devicePointer = value;

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
