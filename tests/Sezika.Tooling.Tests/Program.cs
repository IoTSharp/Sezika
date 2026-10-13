using System.Diagnostics;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

if (args.Length != 4 || args[0] != "--output" || args[2] != "--maximum-checks" || !int.TryParse(args[3], out var maximumChecks) || maximumChecks is < 1 or > 64)
    throw new ArgumentException("--output <new-owned-directory> --maximum-checks 1..64");
using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(45));
StartupGate.Wait(cancel.Token);
var root = Path.GetFullPath(args[1]);
if (Path.Exists(root)) throw new IOException("Test output exists; preserve evidence.");
Directory.CreateDirectory(root);
var watch = Stopwatch.StartNew();
var checks = 0;
var names = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
Budget Budget() => new(30, null, cancel.Token);
void Test(string name, Action action)
{
    if (checks >= maximumChecks) return;
    if (watch.Elapsed.TotalSeconds >= 40) throw new TimeoutException("Check suite deadline40s.");
    cancel.Token.ThrowIfCancellationRequested();
    action();
    checks++; names.Add(name);
    Console.WriteLine($"PASS {checks}/{maximumChecks}: {name}");
}
void Reject(Action action)
{
    var rejected = false;
    try { action(); }
    catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { rejected = true; }
    Check(rejected, "Expected rejection did not occur.");
}
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
string MakeTar(string name, (string Name, byte[] Data, TarEntryType Type)[] entries)
{
    if (entries.Length > 16) throw new InvalidOperationException("Fixture entry bound.");
    var path = Path.Combine(root, name + ".tar");
    using var stream = new FileStream(path, FileMode.CreateNew);
    using var writer = new TarWriter(stream);
    foreach (var item in entries)
    {
        cancel.Token.ThrowIfCancellationRequested();
        var entry = new PaxTarEntry(item.Type, item.Name);
        using var data = new MemoryStream(item.Data);
        if (item.Type == TarEntryType.RegularFile) entry.DataStream = data;
        if (item.Type is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = "../outside";
        writer.WriteEntry(entry);
    }
    return path;
}
var data = Encoding.UTF8.GetBytes("Sezika 中文 fixture\n");
var manifest = Encoding.UTF8.GetBytes(Hash(data) + "  tokenizer/data.json\n");
(string, byte[], TarEntryType) Member(string name, byte[] value) => (name, value, TarEntryType.RegularFile);
ArchiveReport Extract(string path, string folder) => ArchiveOperations.Extract(path, Hash(File.ReadAllBytes(path)), Path.Combine(root, folder), Budget());

// First check is deliberately tiny. Run --maximum-checks 1 before the complete bounded suite.
Test("single-file hash with EOF", () =>
{
    using var stream = new MemoryStream(data);
    Check(Budget().Hash(stream, data.Length) == Hash(data), "Streaming SHA differs.");
});
Test("zero-length hash", () => { using var s = new MemoryStream(); Check(Budget().Hash(s, 0) == Hash([]), "Empty SHA differs."); });
Test("exact block boundary SHA", () => { var b = new byte[1024 * 1024]; using var s = new MemoryStream(b); Check(Budget().Hash(s, b.Length) == Hash(b), "Block-boundary SHA differs."); });
Test("hash truncated input", () => { using var s = new MemoryStream(data); Reject(() => Budget().Hash(s, data.Length + 1)); });
Test("hash extra input", () => { using var s = new MemoryStream(data); Reject(() => Budget().Hash(s, data.Length - 1)); });
Test("real TAR and complete extracted manifest", () =>
{
    var path = MakeTar("valid", [Member("./tokenizer/data.json", data), Member("./files.sha256", manifest)]);
    var report = Extract(path, "valid-output");
    Check(report.Files == 2 && report.Bytes == data.Length + manifest.Length, "Archive denominators differ.");
    Check(File.ReadAllBytes(Path.Combine(report.Directory, "tokenizer/data.json")).SequenceEqual(data), "Extracted bytes differ.");
    Check(!File.Exists(Path.Combine(report.Directory, ".sezika-extraction-in-progress")), "Successful marker not removed.");
    Reject(() => Extract(path, "valid-output"));
});
Test("wrong outer SHA creates no destination", () =>
{
    var path = MakeTar("wrong-sha", [Member("files.sha256", manifest)]);
    Reject(() => ArchiveOperations.Extract(path, new string('0', 64), Path.Combine(root, "wrong-sha-output"), Budget()));
    Check(!Directory.Exists(Path.Combine(root, "wrong-sha-output")), "Wrong SHA created output.");
});
Test("manifest omitted file", () => Reject(() => Extract(MakeTar("omitted", [Member("files.sha256", manifest), Member("tokenizer/data.json", data), Member("extra", data)]), "omitted-output")));
Test("manifest hash mismatch preserves partial evidence", () =>
{
    var path = MakeTar("bad-member-sha", [Member("files.sha256", manifest), Member("tokenizer/data.json", [1, 2, 3])]);
    Reject(() => Extract(path, "bad-member-output"));
    Check(File.Exists(Path.Combine(root, "bad-member-output/.sezika-extraction-in-progress")), "Failure marker not preserved.");
});
Test("traversal member rejected", () => Reject(() => Extract(MakeTar("traversal", [Member("../escape", data)]), "traversal-output")));
Test("absolute member rejected", () => Reject(() => Extract(MakeTar("absolute", [Member("/escape", data)]), "absolute-output")));
Test("symbolic link rejected", () => Reject(() => Extract(MakeTar("symlink", [("link", [], TarEntryType.SymbolicLink)]), "symlink-output")));
Test("hard link rejected", () => Reject(() => Extract(MakeTar("hardlink", [("link", [], TarEntryType.HardLink)]), "hardlink-output")));
Test("case duplicate rejected", () => Reject(() => Extract(MakeTar("duplicate", [Member("same", data), Member("SAME", data)]), "duplicate-output")));
Test("manifest duplicate rejected", () => Reject(() => Extract(MakeTar("duplicate-manifest", [Member("tokenizer/data.json", data), Member("files.sha256", [.. manifest, .. manifest])]), "duplicate-manifest-output")));
Test("missing manifest rejected", () => Reject(() => Extract(MakeTar("missing", [Member("file", data)]), "missing-output")));
Test("cancel before extraction", () =>
{
    var cancelPath = Path.Combine(root, "CANCEL"); File.WriteAllText(cancelPath, "cancel");
    Reject(() => ArchiveOperations.Extract("unused", new string('0', 64), Path.Combine(root, "cancelled-output"), new Budget(30, cancelPath, cancel.Token)));
    Check(!Directory.Exists(Path.Combine(root, "cancelled-output")), "Cancelled operation created output.");
});
Test("portable paths reject reserved and aliases", () =>
{
    foreach (var path in new[] { "NUL.txt", "a/./b", "a//b", "a/../b", "a:b", "a\\b", "a.", "CON", "LPT9.log" }) Reject(() => SafePaths.Relative(path));
});
Test("native current process identity", () =>
{
    var current = ProcessObserver.Observe(Environment.ProcessId);
    Check(current.State == "Present" && current.ParentPid > 0 && !string.IsNullOrEmpty(current.CommandLine), "Current native observation failed: " + current.Error);
    var record = new ProcessRecord { Pid = current.Pid, Started = current.Started, ParentPid = current.ParentPid, CommandLine = current.CommandLine,
        Platform = current.Platform, StartTicks = current.StartTicks, BootId = current.BootId };
    Check(ProcessAudit.Compare(record, current).State == "LIVE", "Current identity did not match.");
    File.WriteAllText(Path.Combine(root, "self.result.json"), JsonSerializer.Serialize(new LegacyReceipt { Processes = [record] }, ToolingJsonContext.Default.LegacyReceipt));
});
var stamp = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
ProcessRecord Record() => new() { Pid = 101, ParentPid = 55, Started = stamp, CommandLine = "owned.exe --input item", Platform = "windows" };
ProcessObservation Observation() => new("Present", 101, 55, stamp, "owned.exe --input item", "windows", null);
Test("creation time detects PID reuse", () => Check(ProcessAudit.Compare(Record(), Observation() with { Started = stamp.AddSeconds(1) }).State == "REUSED", "PID reuse lost."));
Test("missing command remains UNKNOWN", () => { var r = Record(); r.CommandLine = null; Check(ProcessAudit.Compare(r, Observation()).State == "UNKNOWN", "Missing argv accepted."); });
Test("parent mismatch remains UNKNOWN", () => Check(ProcessAudit.Compare(Record(), Observation() with { ParentPid = 56 }).State == "UNKNOWN", "Parent mismatch accepted."));
Test("command mismatch remains UNKNOWN", () => Check(ProcessAudit.Compare(Record(), Observation() with { CommandLine = "other" }).State == "UNKNOWN", "Command mismatch accepted."));
Test("access denied remains UNKNOWN", () => Check(ProcessAudit.Compare(Record(), Observation() with { State = "Unknown", Error = "denied" }).State == "UNKNOWN", "Denied observation accepted."));
Test("foreign platform cannot prove absence", () => Check(ProcessAudit.Compare(Record(), Observation() with { State = "Absent", Platform = "linux" }).State == "UNKNOWN", "Foreign platform accepted."));
Test("Linux birth ticks and boot identity", () =>
{
    var r = Record(); r.Platform = "linux"; r.StartTicks = 99; r.BootId = "11111111-1111-1111-1111-111111111111";
    var current = Observation() with { Platform = "linux", StartTicks = 99, BootId = r.BootId };
    Check(ProcessAudit.Compare(r, current).State == "LIVE", "Linux identity mismatch.");
    Check(ProcessAudit.Compare(r, current with { BootId = "22222222-2222-2222-2222-222222222222" }).State == "REUSED", "Linux reboot ignored.");
    r.BootId = "invalid"; Check(ProcessAudit.Compare(r, current).State == "UNKNOWN", "Malformed boot identity accepted.");
    r.StartTicks = null; Check(ProcessAudit.Compare(r, current).State == "UNKNOWN", "Missing ticks accepted.");
});
Test("Linux stat parser supports spaces and parentheses", () =>
{
    var fields = Enumerable.Repeat("0", 20).ToArray(); fields[0] = "S"; fields[1] = "55"; fields[19] = "1234567";
    var parsed = ProcessObserver.ParseLinuxStat("101 (name (with) spaces) " + string.Join(' ', fields), 101);
    Check(parsed.ParentPid == 55 && parsed.StartTicks == 1234567, "Linux stat fields shifted.");
});
Test("empty receipts cannot pass", () =>
{
    var directory = Path.Combine(root, "empty-receipts"); Directory.CreateDirectory(directory);
    Reject(() => ProcessAudit.Run([directory], Budget()));
});
Test("historical missing root preserves incomplete marker", () =>
{
    var directory = Path.Combine(root, "historical-receipts"); Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "old.result.json"), JsonSerializer.Serialize(new LegacyReceipt { Pid = 101, Started = stamp, ParentPid = 55 }, ToolingJsonContext.Default.LegacyReceipt));
    var result = ProcessAudit.Run([directory], Budget(), _ => Observation() with { State = "Absent" });
    Check(result.AbsentRecords == 1 && result.HistoricallyUnobservedRootReceipts == 1 && result.HistoricalIncompleteReceiptsPreserved && !result.TerminationPerformed, "Historical incomplete identity was lost.");
});
Test("fixture live and reused audit denominators", () =>
{
    var directory = Path.Combine(root, "audit-receipts"); Directory.CreateDirectory(directory);
    var other = Record(); other.Pid = 102;
    File.WriteAllText(Path.Combine(directory, "case.result.json"), JsonSerializer.Serialize(new LegacyReceipt { Processes = [Record(), other] }, ToolingJsonContext.Default.LegacyReceipt));
    var result = ProcessAudit.Run([directory], Budget(), pid => pid == 101 ? Observation() : Observation() with { Pid = 102, Started = stamp.AddDays(1) });
    Check(result.MatchingLive == 1 && result.ReusedRecords == 1 && result.UniquePids == 2 && result.IdentityRecords == 2 && result.Status.StartsWith("FAIL", StringComparison.Ordinal), "Audit denominators/status differ.");
});
Test("duplicate log directories rejected", () => Reject(() => ProcessAudit.Run([root, root], Budget())));
Test("CLI argument validation and fresh failure receipt", () =>
{
    var output = Path.Combine(root, "cli-failure.json");
    Check(ToolingCommand.Run(["archive", "extract", "--archive", "unused", "--sha256", "bad", "--destination", Path.Combine(root, "cli-failure-output"), "--output", output]) == 1, "Malformed CLI request accepted.");
    using var json = JsonDocument.Parse(File.ReadAllText(output));
    Check(json.RootElement.GetProperty("Status").GetString() == "FAIL", "Failure receipt absent.");
    var before = File.ReadAllBytes(output);
    Check(ToolingCommand.Run(["process", "audit", "--log-directory", root, "--output", output]) == 1 && File.ReadAllBytes(output).SequenceEqual(before), "Existing receipt overwritten.");
});

if (checks == 0) throw new InvalidOperationException("No checks ran.");
File.WriteAllLines(Path.Combine(root, "checks.txt"), names.Append($"PASS checks={checks}; elapsed={watch.Elapsed.TotalSeconds:F3}s; platform={Environment.OSVersion}; native_aot={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}"));
Console.WriteLine($"PASS {checks} checks; seconds={watch.Elapsed.TotalSeconds:F3}; no process was created or terminated by this suite; fixtures preserved at {root}");
