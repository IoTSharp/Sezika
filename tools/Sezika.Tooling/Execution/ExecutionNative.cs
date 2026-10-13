using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sezika.Tooling;

internal sealed class ExecutionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ExecutionHandle() : base(true) { }
    internal ExecutionHandle(nint value) : base(true) => SetHandle(value);
    protected override bool ReleaseHandle() => ExecutionNative.CloseHandle(handle) != 0;
}

internal sealed class ExecutionFd : SafeHandleMinusOneIsInvalid
{
    internal ExecutionFd(int value) : base(true) => SetHandle(value);
    internal int Value => checked((int)handle);
    protected override bool ReleaseHandle() => ExecutionNative.close(checked((int)handle)) == 0;
}

internal static partial class ExecutionNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct JobCompletionPort { internal nint Key, Port; }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial ExecutionHandle CreateIoCompletionPort(nint file, nint existing, nuint key, uint threads);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int GetQueuedCompletionStatus(ExecutionHandle port, out uint message, out nuint key, out nint value, uint milliseconds);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int PostQueuedCompletionStatus(ExecutionHandle port, uint message, nuint key, nint value);
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes { internal uint Length; internal nint Descriptor; internal int Inherit; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        internal uint Size; internal nint Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, CountX, CountY, Fill, Flags;
        internal ushort Show, ReservedSize; internal nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx { internal StartupInfo Startup; internal nint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation { internal nint Process, Thread; internal uint Pid, Tid; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct JobBasicLimits
    {
        internal long ProcessTime, JobTime; internal uint Flags;
        internal nuint MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcessLimit; internal nuint Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct JobLimits
    {
        internal JobBasicLimits Basic;
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial ExecutionHandle CreateJobObjectW(nint security, nint name);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int SetInformationJobObject(ExecutionHandle job, int informationClass, void* information, uint bytes);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int QueryInformationJobObject(ExecutionHandle job, int informationClass, void* information, uint bytes, out uint returned);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int AssignProcessToJobObject(ExecutionHandle job, ExecutionHandle process);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int IsProcessInJob(ExecutionHandle process, ExecutionHandle job, out int member);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial ExecutionHandle OpenProcess(uint access, int inherit, uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int GetProcessTimes(ExecutionHandle process, out long creation, out long exit, out long kernel, out long user);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int QueryFullProcessImageNameW(ExecutionHandle process, uint flags, char* path, ref uint count);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial uint GetSystemDirectoryW(char* path, uint count);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int TerminateJobObject(ExecutionHandle job, uint code);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int TerminateProcess(ExecutionHandle process, uint code);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int GetExitCodeProcess(ExecutionHandle process, out uint code);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial uint WaitForSingleObject(ExecutionHandle process, uint milliseconds);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial uint ResumeThread(ExecutionHandle thread);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int CreatePipe(out nint read, out nint write, ref SecurityAttributes security, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int SetHandleInformation(ExecutionHandle handle, uint mask, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int PeekNamedPipe(ExecutionHandle pipe, void* buffer, uint size, nint read, out uint available, nint left);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int ReadFile(ExecutionHandle file, void* buffer, uint count, out uint read, nint overlapped);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int WriteFile(ExecutionHandle file, void* buffer, uint count, out uint written, nint overlapped);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int InitializeProcThreadAttributeList(void* list, uint count, uint flags, ref nuint bytes);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static unsafe partial int UpdateProcThreadAttribute(void* list, uint flags, nuint attribute, void* value, nuint size, nint previous, nint returned);
    [LibraryImport("kernel32.dll")] internal static unsafe partial void DeleteProcThreadAttributeList(void* list);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static unsafe partial int CreateProcess(string executable, char* command, nint processSecurity, nint threadSecurity, int inherit,
        uint flags, char* environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);

    [LibraryImport("libc", SetLastError = true)] internal static unsafe partial int pipe2(int* pipes, int flags);
    [LibraryImport("libc", SetLastError = true)] internal static partial int close(int fd);
    [LibraryImport("libc", SetLastError = true)] internal static partial int fcntl(int fd, int operation, int value);
    [LibraryImport("libc", SetLastError = true)] internal static unsafe partial nint read(int fd, void* bytes, nuint count);
    [LibraryImport("libc", SetLastError = true)] internal static unsafe partial nint write(int fd, void* bytes, nuint count);
    [LibraryImport("libc", SetLastError = true)] internal static partial int kill(int pid, int signal);
    [LibraryImport("libc", SetLastError = true)] internal static partial int getpgid(int pid);
    [LibraryImport("libc")] internal static partial int getppid();
    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd { internal int Descriptor; internal short Events, Returned; }
    [LibraryImport("libc", SetLastError = true)] internal static partial int poll(ref PollFd fd, nuint count, int milliseconds);
    [LibraryImport("libc", SetLastError = true)] internal static unsafe partial int waitid(int type, uint id, void* info, int options);
    [LibraryImport("libc", SetLastError = true)] internal static partial int waitpid(int pid, out int status, int options);
    [LibraryImport("libc", SetLastError = true)] internal static unsafe partial int prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);
    // syscall's C long and pointers are pointer-sized on the supported Linux
    // x64/arm64 ABIs. The executor explicitly rejects 32-bit Linux.
    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)] internal static partial nint PidFdOpen(nint number, int pid, uint flags);
    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)] internal static partial nint PidFdSignal(nint number, int fd, int signal, nint info, uint flags);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawnattr_init(void* attributes);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawnattr_destroy(void* attributes);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawnattr_setflags(void* attributes, short flags);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawnattr_setpgroup(void* attributes, int group);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawn_file_actions_init(void* actions);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawn_file_actions_destroy(void* actions);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawn_file_actions_adddup2(void* actions, int fd, int target);
    [LibraryImport("libc")] internal static unsafe partial int posix_spawn_file_actions_addclose(void* actions, int fd);
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] internal static unsafe partial int posix_spawn_file_actions_addchdir_np(void* actions, string path);
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] internal static unsafe partial int posix_spawn(out int pid, string path, void* actions, void* attributes, nint* arguments, nint* environment);
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] internal static unsafe partial int execve(string path, nint* arguments, nint* environment);
}
