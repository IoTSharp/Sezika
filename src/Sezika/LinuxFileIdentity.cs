using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sezika;

// Linux statx has a fixed 256-byte ABI on both x64 and arm64. Identity excludes
// ctime/mtime: writes legitimately change them, and Unix CreationTime can use them.
internal readonly record struct LinuxFileIdentity(uint DeviceMajor, uint DeviceMinor, ulong Inode,
    bool BirthTimeKnown, long BirthSeconds, uint BirthNanoseconds)
{
    internal static LinuxFileIdentity ReadPath(string path) => Read(-100, path, 0x100);

    internal static LinuxFileIdentity ReadHandle(SafeFileHandle handle)
    {
        var acquired = false;
        try
        {
            handle.DangerousAddRef(ref acquired);
            return Read(checked((int)handle.DangerousGetHandle()), string.Empty, 0x1000 | 0x100);
        }
        finally { if (acquired) handle.DangerousRelease(); }
    }

    private static LinuxFileIdentity Read(int directoryFd, string path, int flags)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (LinuxFileNative.Statx(directoryFd, path, flags, 0x101 | 0x800, out var value) != 0)
            throw new IOException($"Cannot verify Linux file identity (errno {Marshal.GetLastPInvokeError()}).");
        if ((value.Mask & 0x101) != 0x101 || (value.Mode & 0xF000) != 0x8000)
            throw new IOException("Linux file identity is missing or is not a regular file.");
        var birth = (value.Mask & 0x800) != 0;
        return new(value.DeviceMajor, value.DeviceMinor, value.Inode, birth,
            birth ? value.BirthSeconds : 0, birth ? value.BirthNanoseconds : 0);
    }
}

internal static partial class LinuxFileNative
{
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Statx(int directoryFd, string path, int flags, uint mask, out StatxBuffer result);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct StatxBuffer
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(80)] internal long BirthSeconds;
        [FieldOffset(88)] internal uint BirthNanoseconds;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
    }
}
