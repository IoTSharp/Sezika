using System.Reflection;
using System.Runtime.InteropServices;

namespace Sezika.Cuda;

internal static partial class CudaNative
{
    private const string LibraryName = "nvcuda";
    internal const int Success = 0;
    internal const int ErrorNoDevice = 100;

    static CudaNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(CudaNative).Assembly, ResolveLibrary);
    }

    private static IntPtr ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(name, LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        string[] names = OperatingSystem.IsWindows()
            ? ["nvcuda.dll"]
            : OperatingSystem.IsLinux()
                ? ["libcuda.so.1", "libcuda.so"]
                : ["libcuda.dylib"];

        foreach (string candidate in names)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    internal static void Check(int result, string operation)
    {
        if (result == Success)
        {
            return;
        }

        throw new CudaException(
            $"cuda_{result}",
            $"CUDA Driver operation '{operation}' failed with CUresult {result}.",
            result);
    }

    internal static bool IsDriverAvailable()
    {
        try
        {
            Check(Init(0), "cuInit");
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (CudaException)
        {
            return false;
        }
    }

    [LibraryImport(LibraryName, EntryPoint = "cuInit")]
    internal static partial int Init(uint flags);

    [LibraryImport(LibraryName, EntryPoint = "cuDeviceGetCount")]
    internal static partial int DeviceGetCount(out int count);

    [LibraryImport(LibraryName, EntryPoint = "cuDeviceGet")]
    internal static partial int DeviceGet(out int device, int ordinal);

    [LibraryImport(LibraryName, EntryPoint = "cuCtxCreate_v2")]
    internal static partial int ContextCreate(out IntPtr context, uint flags, int device);

    [LibraryImport(LibraryName, EntryPoint = "cuCtxDestroy_v2")]
    internal static partial int ContextDestroy(IntPtr context);

    [LibraryImport(LibraryName, EntryPoint = "cuCtxSynchronize")]
    internal static partial int ContextSynchronize();

    [LibraryImport(LibraryName, EntryPoint = "cuModuleLoadData")]
    internal static unsafe partial int ModuleLoadData(out IntPtr module, nint image);

    [LibraryImport(LibraryName, EntryPoint = "cuModuleUnload")]
    internal static partial int ModuleUnload(IntPtr module);

    [LibraryImport(LibraryName, EntryPoint = "cuModuleGetFunction", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ModuleGetFunction(out IntPtr function, CudaModuleHandle module, string name);

    [LibraryImport(LibraryName, EntryPoint = "cuMemAlloc_v2")]
    internal static partial int MemAlloc(out ulong devicePointer, nuint bytesize);

    [LibraryImport(LibraryName, EntryPoint = "cuMemFree_v2")]
    internal static partial int MemFree(ulong devicePointer);

    [LibraryImport(LibraryName, EntryPoint = "cuMemcpyHtoD_v2")]
    internal static partial int MemcpyHtoD(ulong destinationDevice, nint sourceHost, nuint byteCount);

    [LibraryImport(LibraryName, EntryPoint = "cuMemcpyDtoH_v2")]
    internal static partial int MemcpyDtoH(nint destinationHost, ulong sourceDevice, nuint byteCount);

    [LibraryImport(LibraryName, EntryPoint = "cuLaunchKernel")]
    internal static unsafe partial int LaunchKernel(
        IntPtr function,
        uint gridDimX,
        uint gridDimY,
        uint gridDimZ,
        uint blockDimX,
        uint blockDimY,
        uint blockDimZ,
        uint sharedMemBytes,
        IntPtr stream,
        nint* kernelParams,
        nint* extra);
}
