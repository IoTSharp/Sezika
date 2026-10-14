using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed class ExecutionRequest
{
    public string Executable { get; set; } = "";
    public string[] ArgumentList { get; set; } = [];
    public string WorkingDirectory { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public Dictionary<string, string?> Environment { get; set; } = new(StringComparer.Ordinal);
    public byte[] StandardInput { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 60;
    public int CleanupSeconds { get; set; } = 10;
    public int MaximumProcesses { get; set; } = 64;
    public int MaximumInputBytes { get; set; } = 1_048_576;
    public int MaximumOutputBytes { get; set; } = 1_048_576;
    public string WindowsConsoleMode { get; set; } = "detached";
    public string? CancelFile { get; set; }
    public string? StopFile { get; set; }
    // Linux uses the C# broker to register identity before execve, including
    // immediate-exit native programs. These are explicit apphost arguments.
    public string? BrokerExecutable { get; set; }
    public string[] BrokerArgumentPrefix { get; set; } = ["process", "broker"];
    public string[] WatchdogArgumentPrefix { get; set; } = ["process", "watchdog"];
    public NestedBrokerPin[] NestedBrokers { get; set; } = [];
    public AdmissionObserver[] LinuxObservers { get; set; } = [];
}

internal sealed class AdmissionObserver
{
    public string OutputDirectory { get; set; } = "";
    public string OwnershipId { get; set; } = "";
    public ExecutionIdentity NativeIdentity { get; set; } = new();
}

internal sealed class AdmissionSignal
{
    public string ObserverOwnershipId { get; set; } = "";
    public ExecutionIdentity NativeIdentity { get; set; } = new();
}

internal sealed class NestedBrokerPin
{
    public string Executable { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string[] ArgumentPrefix { get; set; } = ["process", "broker"];
}

internal sealed class ExecutionIdentity
{
    public int Pid { get; set; }
    public string Platform { get; set; } = "";
    public int ParentPid { get; set; }
    public int[] ParentChain { get; set; } = [];
    public DateTimeOffset? Started { get; set; }
    public ulong? StartTicks { get; set; }
    public string? BootId { get; set; }
    public string CommandLine { get; set; } = "";
    public string? AfterExecCommandLine { get; set; }
    public string? ExpectedExecCommandLine { get; set; }
    public string State { get; set; } = "REGISTERED";
    public string Ownership { get; set; } = "";
    public string? ScopeId { get; set; }
    public string[] CommandLineRevisions { get; set; } = [];
    public string? ExitProof { get; set; }
    public string? PendingObservationError { get; set; }
    public string[] ObservationNotes { get; set; } = [];
}

internal sealed class ExecutionResult
{
    public int SchemaVersion { get; set; } = 1;
    public string Status { get; set; } = "FAIL";
    public string ExecutionStatus { get; set; } = "NOT_STARTED";
    public string CleanupStatus { get; set; } = "NOT_NEEDED";
    public int? ExitCode { get; set; }
    public int? RootPid { get; set; }
    public string Platform { get; set; } = "";
    public string Executable { get; set; } = "";
    public string[] ArgumentList { get; set; } = [];
    public string WorkingDirectory { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string OwnershipId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public double ElapsedSeconds { get; set; }
    public long StandardOutputBytes { get; set; }
    public long StandardErrorBytes { get; set; }
    public long StandardInputBytes { get; set; }
    public bool InputComplete { get; set; }
    public bool OutputComplete { get; set; }
    public bool OutputLimitExceeded { get; set; }
    public int RemainingProcesses { get; set; }
    public ExecutionIdentity? Launcher { get; set; }
    public ExecutionIdentity[] Processes { get; set; } = [];
    public string[] Errors { get; set; } = [];
}

internal sealed class BrokerConfiguration
{
    public string Executable { get; set; } = "";
    public string[] ArgumentList { get; set; } = [];
    public string OwnershipId { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string WatchdogExecutable { get; set; } = "";
    public string[] WatchdogArgumentPrefix { get; set; } = [];
    public int ParentPid { get; set; }
    public int MaximumSeconds { get; set; }
    public int CleanupSeconds { get; set; }
    public int MaximumProcesses { get; set; }
    public ExecutionIdentity? Launcher { get; set; }
}

internal sealed class WatchdogConfiguration
{
    public BrokerConfiguration Broker { get; set; } = new();
    public ExecutionIdentity Root { get; set; } = new();
}

internal sealed class WatchdogResult
{
    public string Status { get; set; } = "UNKNOWN";
    public string Reason { get; set; } = "";
    public int Pid { get; set; }
    public int RemainingLive { get; set; }
    public int ZombiesPendingReap { get; set; }
    public string[] Errors { get; set; } = [];
    public string[] RootExitProofs { get; set; } = [];
}

[JsonSerializable(typeof(ExecutionRequest))]
[JsonSerializable(typeof(ExecutionResult))]
[JsonSerializable(typeof(ExecutionIdentity[]))]
[JsonSerializable(typeof(BrokerConfiguration))]
[JsonSerializable(typeof(WatchdogConfiguration))]
[JsonSerializable(typeof(WatchdogResult))]
[JsonSerializable(typeof(AdmissionObserver))]
[JsonSerializable(typeof(AdmissionObserver[]))]
[JsonSerializable(typeof(AdmissionSignal))]
internal partial class ExecutionJsonContext : JsonSerializerContext;

internal sealed class ExecutionStartupException(string message, int pid, bool cleanupProven, Exception inner)
    : IOException(message, inner)
{
    internal int Pid { get; } = pid;
    internal bool CleanupProven { get; } = cleanupProven;
}
