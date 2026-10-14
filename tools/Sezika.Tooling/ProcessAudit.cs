using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    bool HistoricalIncompleteReceiptsPreserved = true)
{
    // Omitted for legacy inputs, preserving their existing JSON contract. These
    // are the original execution conclusions, never a new execution verdict.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeReceiptAudit[]? NativeReceipts { get; init; }
}

internal sealed record NativeReceiptAudit(string Path, string OriginalStatus, string ExecutionStatus,
    string CleanupStatus, int? ExitCode, int RootPid, string Platform, string OwnershipId, string[] Errors);

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
        var nativeReceipts = new List<NativeReceiptAudit>();
        var nativeRecords = new HashSet<ProcessRecord>();
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
                using var document = JsonDocument.Parse(stream);
                ValidateLegacyReceiptShape(document.RootElement, budget);
                var receipt = document.RootElement.Deserialize(ToolingJsonContext.Default.LegacyReceipt) ?? throw new InvalidDataException("Null receipt.");
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
            ReadNative(directory, records, nativeRecords, nativeReceipts, ref receiptCount, ref count, budget);
        }
        if (receiptCount == 0) throw new InvalidDataException("No process receipts found; empty input is not closure evidence.");
        var pids = records.Select(r => r.Pid).Distinct().ToArray();
        if (pids.Length > 1024) throw new InvalidDataException("Unique PID bound 1024 exceeded.");
        var observations = new Dictionary<int, ProcessObservation>();
        foreach (var pid in pids) { budget.Check(); observations.Add(pid, observe(pid)); }
        var result = new List<AuditIdentity>(records.Count);
        foreach (var record in records)
        {
            budget.Check();
            var actual = observations[record.Pid];
            // The historical comparer intentionally retains its old behavior.
            // Native observations must bind even an absent answer to this PID.
            result.Add(nativeRecords.Contains(record) && actual.Pid != record.Pid
                ? new(record.Pid, "UNKNOWN", "Observer returned a different PID.", record.ParentPid,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(record.CommandLine!))))
                : Compare(record, actual));
        }
        var live = result.Count(r => r.State == "LIVE");
        var unknown = result.Count(r => r.State == "UNKNOWN");
        return new(1, live == 0 && unknown == 0 ? "PASS_current_recorded_task_identities_absent_or_reused" : "FAIL_current_live_or_unknown",
            receiptCount, records.Count, pids.Length, live, unknown, result.Count(r => r.State == "ABSENT"),
            result.Count(r => r.State == "REUSED"), incomplete, budget.ElapsedSeconds, result.ToArray())
        { NativeReceipts = nativeReceipts.Count == 0 ? null : nativeReceipts.ToArray() };
    }

    private static void ValidateLegacyReceiptShape(JsonElement element, Budget budget, bool identity = false)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Legacy receipt or identity must be an object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            budget.Check();
            if (names.Count >= 256 || !names.Add(property.Name))
                throw new InvalidDataException("Legacy receipt has ambiguous JSON fields or exceeds 256 fields.");
            // These fields describe native execution/ownership, not historical
            // Status, CleanupStatus or other ordinary legacy report metadata.
            if (property.Name.Equals("ExecutionStatus", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("OwnershipId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Launcher", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Ownership", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("ScopeId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("CommandLineRevisions", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("ExpectedExecCommandLine", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("AfterExecCommandLine", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native evidence requires canonical result.json and its original registrations; it cannot be audited as legacy.");
            if (!identity && property.Name.Equals("Processes", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Name != "Processes")
                    throw new InvalidDataException("Legacy Processes field spelling is ambiguous.");
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 2048)
                    throw new InvalidDataException("Legacy Processes field is ambiguous or exceeds 2048 identities.");
                foreach (var process in property.Value.EnumerateArray())
                    ValidateLegacyReceiptShape(process, budget, identity: true);
            }
        }
    }

    private static void ReadNative(string directory, List<ProcessRecord> records, HashSet<ProcessRecord> nativeRecords,
        List<NativeReceiptAudit> receipts, ref int receiptCount, ref int fileCount, Budget budget)
    {
        var resultPath = Path.Combine(directory, "result.json");
        var registeredPath = Path.Combine(directory, "registered.json");
        if (!File.Exists(resultPath))
        {
            if (File.Exists(registeredPath)) throw new InvalidDataException("Native registration has no final result.json; incomplete evidence is preserved.");
            return;
        }
        if (++fileCount > 256) throw new InvalidDataException("Receipt count exceeds 256 per directory.");
        using var document = ReadNativeJson(resultPath, budget);
        var element = document.RootElement;
        RequireFields(element, ["SchemaVersion", "Status", "ExecutionStatus", "CleanupStatus", "ExitCode", "RootPid", "Platform",
            "Executable", "ArgumentList", "OwnershipId", "StartedUtc", "RemainingProcesses", "InputComplete", "OutputComplete",
            "OutputLimitExceeded", "Launcher", "Processes", "Errors"], budget);
        RequireIdentityArray(element.GetProperty("Processes"), budget);
        RequireIdentityFields(element.GetProperty("Launcher"), budget);
        var receipt = element.Deserialize(ExecutionJsonContext.Default.ExecutionResult) ?? throw new InvalidDataException("Null native result.");
        if (receipt.SchemaVersion != 1 || receipt.Status is not ("PASS" or "FAIL" or "UNKNOWN") ||
            receipt.ExecutionStatus is not ("NOT_STARTED" or "RUNNING" or "SUCCEEDED" or "FAILED" or "TIMED_OUT" or "CANCELLED") ||
            receipt.CleanupStatus is not ("PASS" or "UNKNOWN" or "NOT_NEEDED") || receipt.Platform is not ("windows" or "linux") ||
            receipt.RootPid is null or <= 0 || !Guid.TryParseExact(receipt.OwnershipId, "N", out _) ||
            receipt.StartedUtc == default || string.IsNullOrEmpty(receipt.Executable) || receipt.Executable.Length > 32768 ||
            receipt.ArgumentList is null || receipt.ArgumentList.Length > 256 || receipt.ArgumentList.Any(a => a is null || a.Length > 32768) ||
            receipt.Errors is null || receipt.Errors.Length > 2048 || receipt.Errors.Any(e => e is null || e.Length > 32768) ||
            receipt.RemainingProcesses < 0 || receipt.Launcher is null)
            throw new InvalidDataException("Malformed native execution result.");
        if (receipt.Status == "PASS" && (receipt.ExecutionStatus != "SUCCEEDED" || receipt.CleanupStatus != "PASS" ||
            receipt.ExitCode != 0 || receipt.RemainingProcesses != 0 || receipt.Errors.Length != 0 ||
            !receipt.InputComplete || !receipt.OutputComplete || receipt.OutputLimitExceeded))
            throw new InvalidDataException("Native PASS contradicts its original execution or cleanup evidence.");
        ValidateIdentity(receipt.Launcher, receipt.Platform, true, budget);
        var final = IdentityMap(receipt.Processes, receipt.Platform, budget);
        if (!final.TryGetValue(receipt.RootPid.Value, out var root) || root.ParentPid != receipt.Launcher.Pid ||
            !root.ParentChain.SequenceEqual(new[] { receipt.Launcher.Pid }) || root.ScopeId != receipt.OwnershipId ||
            final.ContainsKey(receipt.Launcher.Pid))
            throw new InvalidDataException("Native root, launcher, parent chain or ownership scope differs.");
        if (receipt.Platform == "linux" && root.ExpectedExecCommandLine !=
            string.Join('\0', new[] { receipt.Executable }.Concat(receipt.ArgumentList)) + '\0')
            throw new InvalidDataException("Native Linux root exec argv differs from its declared argument list.");
        foreach (var identity in final.Values)
        {
            budget.Check();
            if (identity.Pid == root.Pid) continue;
            if (!final.TryGetValue(identity.ParentPid, out var parent) ||
                !identity.ParentChain.SequenceEqual(parent.ParentChain.Append(parent.Pid)) ||
                (identity.ScopeId != parent.ScopeId && (receipt.Platform != "linux" || identity.ExpectedExecCommandLine is null)))
                throw new InvalidDataException("Native descendant parent chain or scope differs.");
        }
        if (!File.Exists(registeredPath)) throw new InvalidDataException("Native result has no registered.json birth receipt.");
        var registrations = 0; var scannedRegistrations = 0;
        var registeredPids = new HashSet<int>();
        foreach (var path in Directory.EnumerateFiles(directory, "registered*.json", SearchOption.TopDirectoryOnly))
        {
            budget.Check();
            if (++scannedRegistrations > 256) throw new InvalidDataException("Native registration scan exceeds 256 files.");
            var name = Path.GetFileName(path);
            if (name != "registered.json" && !(name.Length == 19 && name.StartsWith("registered-", StringComparison.Ordinal) &&
                char.IsAsciiDigit(name[11]) && char.IsAsciiDigit(name[12]) && char.IsAsciiDigit(name[13]) && name.EndsWith(".json", StringComparison.Ordinal))) continue;
            if (++fileCount > 256) throw new InvalidDataException("Receipt count exceeds 256 per directory.");
            using var snapshot = ReadNativeJson(path, budget);
            RequireIdentityArray(snapshot.RootElement, budget);
            var registered = snapshot.RootElement.Deserialize(ExecutionJsonContext.Default.ExecutionIdentityArray) ?? throw new InvalidDataException("Null native registration.");
            var births = IdentityMap(registered, receipt.Platform, budget);
            if (!births.ContainsKey(root.Pid)) throw new InvalidDataException("Native registration has no root birth identity.");
            foreach (var birth in births.Values)
            {
                budget.Check();
                if (!final.TryGetValue(birth.Pid, out var completed) || !SameBirth(birth, completed))
                    throw new InvalidDataException("Native registration and final PID birth, original argv, parent or scope differ.");
                registeredPids.Add(birth.Pid);
            }
            registrations++;
        }
        if (registrations == 0) throw new InvalidDataException("No native birth registrations found.");
        if (final.Keys.Any(pid => !registeredPids.Contains(pid)))
            throw new InvalidDataException("Native final process identity has no matching birth registration.");
        if (final.Count > 2048 - records.Count) throw new InvalidDataException("Identity record bound 2048 exceeded.");
        foreach (var identity in final.Values)
        {
            budget.Check();
            var record = new ProcessRecord
            {
                Pid = identity.Pid, Platform = identity.Platform, Started = identity.Started, StartTicks = identity.StartTicks,
                BootId = identity.BootId, ParentPid = identity.ParentPid,
                CommandLine = identity.AfterExecCommandLine ?? identity.CommandLineRevisions.LastOrDefault() ?? identity.CommandLine
            };
            records.Add(record); nativeRecords.Add(record);
        }
        receiptCount++;
        receipts.Add(new(resultPath, receipt.Status, receipt.ExecutionStatus, receipt.CleanupStatus, receipt.ExitCode,
            receipt.RootPid.Value, receipt.Platform, receipt.OwnershipId, receipt.Errors.ToArray()));
    }

    private static JsonDocument ReadNativeJson(string path, Budget budget)
    {
        budget.Check(); SafePaths.NoLinks(path, budget);
        using var stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Receipt exceeds 16 MiB.");
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static void RequireFields(JsonElement element, string[] fields, Budget budget)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Native receipt object shape differs.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            budget.Check();
            if (names.Count >= 64 || !names.Add(property.Name)) throw new InvalidDataException("Native receipt duplicate field or field count bound exceeded.");
        }
        if (fields.Any(field => !names.Contains(field))) throw new InvalidDataException("Native receipt required fields are missing.");
    }

    private static void RequireIdentityFields(JsonElement element, Budget budget)
    {
        RequireFields(element, ["Pid", "Platform", "ParentPid", "ParentChain", "CommandLine", "State", "Ownership"], budget);
        var platform = element.GetProperty("Platform").GetString();
        if (platform == "windows" && !element.TryGetProperty("Started", out _) ||
            platform == "linux" && (!element.TryGetProperty("StartTicks", out _) || !element.TryGetProperty("BootId", out _)))
            throw new InvalidDataException("Native receipt birth fields are missing.");
        if (element.GetProperty("Ownership").GetString() != "launcher" && !element.TryGetProperty("ScopeId", out _))
            throw new InvalidDataException("Native receipt scope field is missing.");
    }

    private static void RequireIdentityArray(JsonElement element, Budget budget)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() is < 1 or > 2048)
            throw new InvalidDataException("Native identity array is empty, malformed or exceeds 2048.");
        foreach (var identity in element.EnumerateArray()) { budget.Check(); RequireIdentityFields(identity, budget); }
    }

    private static Dictionary<int, ExecutionIdentity> IdentityMap(ExecutionIdentity[] identities, string platform, Budget budget)
    {
        if (identities.Length is < 1 or > 2048) throw new InvalidDataException("Native identity count bound differs.");
        var result = new Dictionary<int, ExecutionIdentity>();
        foreach (var identity in identities)
        {
            budget.Check(); ValidateIdentity(identity, platform, false, budget);
            if (!result.TryAdd(identity.Pid, identity)) throw new InvalidDataException("Native receipt contains duplicate PID births.");
        }
        return result;
    }

    private static void ValidateIdentity(ExecutionIdentity identity, string platform, bool launcher, Budget budget)
    {
        budget.Check();
        if (identity is null || identity.Pid <= 0 || identity.ParentPid <= 0 || identity.Platform != platform ||
            string.IsNullOrEmpty(identity.CommandLine) || identity.CommandLine.Length > 32768 ||
            string.IsNullOrEmpty(identity.State) || identity.State.Length > 128 || string.IsNullOrEmpty(identity.Ownership) || identity.Ownership.Length > 128 ||
            identity.ParentChain is null || identity.ParentChain.Length > 64 || identity.ParentChain.Any(p => p <= 0 || p == identity.Pid) ||
            identity.ParentChain.Distinct().Count() != identity.ParentChain.Length ||
            platform == "windows" && (identity.Started is null || identity.Started == default(DateTimeOffset)) ||
            platform == "linux" && (identity.StartTicks is null or 0 || !Guid.TryParse(identity.BootId, out _)) ||
            identity.AfterExecCommandLine?.Length > 32768 || identity.ExpectedExecCommandLine?.Length > 32768 ||
            identity.CommandLineRevisions is null || identity.CommandLineRevisions.Length > 4 ||
            identity.CommandLineRevisions.Any(line => string.IsNullOrEmpty(line) || line.Length > 32768))
            throw new InvalidDataException("Malformed native birth, argv or parent identity.");
        if (launcher ? identity.Ownership != "launcher" || identity.ParentChain.Length != 0 || identity.ScopeId is not null :
            identity.Ownership == "launcher" || identity.ParentChain.Length == 0 || identity.ParentChain[^1] != identity.ParentPid ||
            !Guid.TryParseExact(identity.ScopeId, "N", out _))
            throw new InvalidDataException("Malformed native ownership scope or parent chain.");
        if (identity.AfterExecCommandLine is not null && (platform != "linux" || string.IsNullOrEmpty(identity.ExpectedExecCommandLine) ||
            identity.AfterExecCommandLine != identity.ExpectedExecCommandLine) ||
            platform == "windows" && identity.ExpectedExecCommandLine is not null ||
            identity.CommandLineRevisions.Length > 0 && (platform != "windows" || identity.Ownership != "windows-job-console-host"))
            throw new InvalidDataException("Unproven native argv transition.");
    }

    private static bool SameBirth(ExecutionIdentity first, ExecutionIdentity second) =>
        first.Pid == second.Pid && first.Platform == second.Platform && first.ParentPid == second.ParentPid &&
        first.ParentChain.SequenceEqual(second.ParentChain) && first.Started == second.Started && first.StartTicks == second.StartTicks &&
        first.BootId == second.BootId && first.CommandLine == second.CommandLine && first.Ownership == second.Ownership &&
        first.ScopeId == second.ScopeId && first.ExpectedExecCommandLine == second.ExpectedExecCommandLine;
}
