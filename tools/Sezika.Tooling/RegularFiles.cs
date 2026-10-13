using System.Runtime.InteropServices;

namespace Sezika.Tooling;

internal static partial class RegularFiles
{
    internal static unsafe void Require(string path, Budget budget)
    {
        budget.Check(); SafePaths.NoLinks(path, budget);
        if (OperatingSystem.IsLinux())
        {
            var information = stackalloc byte[256]; new Span<byte>(information, 256).Clear();
            // Linux statx fixed UAPI: stx_mode at28; no symlink following and
            // no automount. FIFO/socket/device payloads must fail before open.
            if (Statx(-100, path, 0x100 | 0x800, 1, information) != 0) throw new IOException("Regular file identity unavailable; errno " + Marshal.GetLastPInvokeError());
            if ((*(uint*)information & 1) == 0 || (*(ushort*)(information + 28) & 0xf000) != 0x8000) throw new InvalidDataException("Regular file required; linked, device, FIFO, socket and directory payloads are rejected.");
        }
        else
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0) throw new InvalidDataException("Regular file required.");
        }
        budget.Check();
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int Statx(int directory, string path, int flags, uint mask, byte* information);
}
