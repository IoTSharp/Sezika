using System.Runtime.InteropServices;

namespace Sezika.Cuda;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct CudaArrayView
{
    internal CudaArrayView(ulong pointer, long length)
    {
        Pointer = pointer;
        Length = length;
    }

    internal readonly ulong Pointer;
    internal readonly long Length;
}
