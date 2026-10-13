using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

internal sealed class LegacyReceipt
{
    public int Pid { get; set; }
    public DateTimeOffset? Started { get; set; }
    public int ParentPid { get; set; }
    public ProcessRecord[]? Processes { get; set; }
}

internal sealed class ProcessRecord
{
    public int Pid { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public int ParentPid { get; set; }
    public string? CommandLine { get; set; }
    public string? Platform { get; set; }
    public ulong? StartTicks { get; set; }
    public string? BootId { get; set; }
}

internal sealed record ProcessObservation(string State, int Pid, int ParentPid, DateTimeOffset? Started,
    string? CommandLine, string Platform, ulong? StartTicks, string? Error = null, string? BootId = null);
internal sealed record AuditIdentity(int Pid, string State, string Reason, int ParentPid, string? CommandLineSha256);
internal sealed record AuditReport(int SchemaVersion, string Status, int Receipts, int IdentityRecords, int UniquePids,
    int MatchingLive, int Unknown, int AbsentRecords, int ReusedRecords, int HistoricallyUnobservedRootReceipts,
    double ElapsedSeconds, AuditIdentity[] Identities, bool TerminationPerformed = false,
    bool HistoricalIncompleteReceiptsPreserved = true);

internal static class ProcessAudit
{
    internal static AuditIdentity Compare(ProcessRecord record, ProcessObservation actual)
    {
        AuditIdentity Result(string state, string reason) => new(record.Pid, state, reason, record.ParentPid,
            record.CommandLine is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(record.CommandLine))));
        // Legacy receipts are Windows/CIM identities. A foreign OS cannot prove their PIDs absent or reused.
        var platform = record.Platform ?? "windows";
        if (platform != actual.Platform) return Result("UNKNOWN", "Receipt platform differs from observer platform.");
        if (actual.State == "Absent") return Result("ABSENT", "Recorded PID is absent or its retained Windows process handle is signalled exited.");
        if (actual.State != "Present") return Result("UNKNOWN", actual.Error ?? "Process cannot be observed.");
        if (actual.Pid != record.Pid) return Result("UNKNOWN", "Observer returned a different PID.");
        if (platform == "linux")
        {
            if (record.StartTicks is null || actual.StartTicks is null || string.IsNullOrEmpty(record.BootId) || string.IsNullOrEmpty(actual.BootId))
                return Result("UNKNOWN", "Linux identity requires start ticks and boot ID.");
            if (!Guid.TryParse(record.BootId, out _) || !Guid.TryParse(actual.BootId, out _))
                return Result("UNKNOWN", "Malformed Linux boot identity.");
            if (record.BootId != actual.BootId) return Result("REUSED", "Linux boot identity differs.");
            if (record.StartTicks != actual.StartTicks) return Result("REUSED", "PID start ticks differ.");
        }
        else
        {
            var started = record.Started ?? record.CreationDate;
            if (started is null || actual.Started is null) return Result("UNKNOWN", "Creation timestamp is missing.");
            // CIM historically rounded to microseconds. Never broaden this to millisecond PID equivalence.
            if (Math.Abs((started.Value - actual.Started.Value).Ticks) > 10) return Result("REUSED", "PID creation time differs.");
        }
        if (string.IsNullOrEmpty(record.CommandLine) || string.IsNullOrEmpty(actual.CommandLine))
            return Result("UNKNOWN", "Command line is missing.");
        if (record.ParentPid != actual.ParentPid || record.CommandLine != actual.CommandLine)
            return Result("UNKNOWN", "Parent PID or exact command line differs.");
        return Result("LIVE", "Creation identity, parent PID and command line match.");
    }

    internal static AuditReport Run(string[] directories, Budget budget, Func<int, ProcessObservation>? observe = null)
    {
        if (directories.Length is < 1 or > 12) throw new ArgumentException("Expected 1..12 log directories.");
        observe ??= ProcessObserver.Observe;
        var records = new List<ProcessRecord>();
        var receiptCount = 0;
        var incomplete = 0;
        var uniqueDirectories = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var value in directories)
        {
            budget.Check();
            var directory = Path.GetFullPath(value);
            SafePaths.NoLinks(directory, budget);
            if (!uniqueDirectories.Add(directory)) throw new ArgumentException("Duplicate log directory.");
            var count = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.result.json", SearchOption.TopDirectoryOnly))
            {
                budget.Check();
                if (++count > 256) throw new InvalidDataException("Receipt count exceeds 256 per directory.");
                SafePaths.NoLinks(path, budget);
                using var stream = File.OpenRead(path);
                if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Receipt exceeds 16 MiB.");
                var receipt = JsonSerializer.Deserialize(stream, ToolingJsonContext.Default.LegacyReceipt) ?? throw new InvalidDataException("Null receipt.");
                receiptCount++;
                var items = receipt.Processes;
                if (items is null || items.Length == 0)
                {
                    if (receipt.Pid <= 0) throw new InvalidDataException("Receipt has no recorded process identity.");
                    incomplete++;
                    items = [new ProcessRecord { Pid = receipt.Pid, Started = receipt.Started, ParentPid = receipt.ParentPid }];
                }
                if (items.Length > 2048 - records.Count) throw new InvalidDataException("Identity record bound 2048 exceeded.");
                foreach (var record in items)
                {
                    budget.Check();
                    if (record is null || record.Pid <= 0 || record.ParentPid < 0 || record.CommandLine?.Length > 32768 ||
                        record.Platform is not (null or "windows" or "linux")) throw new InvalidDataException("Malformed process identity.");
                    records.Add(record);
                }
            }
        }
        if (receiptCount == 0) throw new InvalidDataException("No process receipts found; empty input is not closure evidence.");
        var pids = records.Select(r => r.Pid).Distinct().ToArray();
        if (pids.Length > 1024) throw new InvalidDataException("Unique PID bound 1024 exceeded.");
        var observations = new Dictionary<int, ProcessObservation>();
        foreach (var pid in pids) { budget.Check(); observations.Add(pid, observe(pid)); }
        var result = new List<AuditIdentity>(records.Count);
        foreach (var record in records) { budget.Check(); result.Add(Compare(record, observations[record.Pid])); }
        var live = result.Count(r => r.State == "LIVE");
        var unknown = result.Count(r => r.State == "UNKNOWN");
        return new(1, live == 0 && unknown == 0 ? "PASS_current_recorded_task_identities_absent_or_reused" : "FAIL_current_live_or_unknown",
            receiptCount, records.Count, pids.Length, live, unknown, result.Count(r => r.State == "ABSENT"),
            result.Count(r => r.State == "REUSED"), incomplete, budget.ElapsedSeconds, result.ToArray());
    }
}
