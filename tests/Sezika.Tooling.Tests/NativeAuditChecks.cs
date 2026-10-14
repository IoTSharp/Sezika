using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sezika.Tooling;

internal static class NativeAuditChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var clock = Stopwatch.StartNew();
        var index = 0;
        const string scope = "0102030405060708090a0b0c0d0e0f10";
        var started = new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.Zero);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Bound()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 29 || index >= 32) throw new TimeoutException("Native audit checks bound30s/32 fixtures.");
        }
        ExecutionIdentity Identity(int pid, int parent, int[] chain, bool launcher = false) => new()
        {
            Pid = pid, Platform = "windows", Started = started.AddSeconds(pid), ParentPid = parent, ParentChain = chain,
            CommandLine = "fixture.exe success 中文 $HOME $(literal)", State = "EXITED",
            Ownership = launcher ? "launcher" : "windows-job", ScopeId = launcher ? null : scope
        };
        ExecutionResult Receipt() => new()
        {
            Status = "PASS", ExecutionStatus = "SUCCEEDED", CleanupStatus = "PASS", ExitCode = 0, RootPid = 111,
            Platform = "windows", Executable = "fixture.exe", ArgumentList = ["success", "中文", "$HOME $(literal)"],
            OwnershipId = scope, StartedUtc = started, InputComplete = true, OutputComplete = true,
            Launcher = Identity(110, 109, [], true), Processes = [Identity(111, 110, [110])]
        };
        string Write(ExecutionResult receipt, ExecutionIdentity[]? registered = null)
        {
            Bound();
            var directory = Path.Combine(root, "native-audit-" + (++index).ToString("D2"));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "result.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, ExecutionJsonContext.Default.ExecutionResult));
            File.WriteAllBytes(Path.Combine(directory, "registered.json"), JsonSerializer.SerializeToUtf8Bytes(registered ?? receipt.Processes, ExecutionJsonContext.Default.ExecutionIdentityArray));
            return directory;
        }
        AuditReport Audit(string directory, Func<int, ProcessObservation>? observe = null)
        {
            Bound();
            return ProcessAudit.Run([directory], new Budget(5, null, cancellation.Token), observe ??
                (pid => new("Absent", pid, 0, null, null, "windows", null)));
        }
        void Rejected(string directory)
        {
            var observed = 0; var rejected = false;
            try { Audit(directory, pid => { observed++; return new("Absent", pid, 0, null, null, "windows", null); }); }
            catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException) { rejected = true; }
            Assert(rejected && observed == 0, "Malformed native evidence reached observation or was accepted.");
        }
        check("native audit exact result and registered pair with ordinary EXITED null proof", () =>
        {
            var directory = Write(Receipt()); var result = Audit(directory);
            Assert(result.Status.StartsWith("PASS_current_", StringComparison.Ordinal) && result.Receipts == 1 &&
                result.IdentityRecords == 1 && result.AbsentRecords == 1 && result.HistoricallyUnobservedRootReceipts == 0 &&
                result.NativeReceipts is [{ OriginalStatus: "PASS", ExecutionStatus: "SUCCEEDED", CleanupStatus: "PASS", ExitCode: 0 }],
                "Genuine native filenames or original execution evidence were lost.");
        });
        check("native audit failing execution remains failing after current absence", () =>
        {
            var receipt = Receipt(); receipt.Status = "FAIL"; receipt.ExecutionStatus = "FAILED"; receipt.ExitCode = 7;
            receipt.Errors = ["Process exit code 7"];
            var directory = Write(receipt); var bytes = File.ReadAllBytes(Path.Combine(directory, "result.json"));
            var result = Audit(directory);
            Assert(result.Status.StartsWith("PASS_current_", StringComparison.Ordinal) &&
                result.NativeReceipts is [{ OriginalStatus: "FAIL", ExecutionStatus: "FAILED", ExitCode: 7 }] &&
                result.NativeReceipts[0].Errors.SequenceEqual(receipt.Errors) &&
                File.ReadAllBytes(Path.Combine(directory, "result.json")).SequenceEqual(bytes),
                "Current lifecycle audit overwrote or promoted original execution failure.");
        });
        check("native audit original UNKNOWN retained and observer UNKNOWN refuses closure", () =>
        {
            var receipt = Receipt(); receipt.Status = "UNKNOWN"; receipt.CleanupStatus = "UNKNOWN";
            receipt.Errors = ["Original cleanup was not proven"];
            var result = Audit(Write(receipt), pid => new("Unknown", pid, 0, null, null, "windows", null, "permission denied"));
            Assert(result.Status == "FAIL_current_live_or_unknown" && result.Unknown == 1 &&
                result.NativeReceipts is [{ OriginalStatus: "UNKNOWN", CleanupStatus: "UNKNOWN" }], "UNKNOWN became closure or execution PASS.");
        });
        check("native audit live exact argv fails and changed argv remains UNKNOWN", () =>
        {
            var receipt = Receipt(); var identity = receipt.Processes[0]; var directory = Write(receipt);
            var live = Audit(directory, pid => new("Present", pid, identity.ParentPid, identity.Started, identity.CommandLine, "windows", null));
            var changed = Audit(directory, pid => new("Present", pid, identity.ParentPid, identity.Started, identity.CommandLine + " changed", "windows", null));
            Assert(live.MatchingLive == 1 && live.Status == "FAIL_current_live_or_unknown" && changed.Unknown == 1 &&
                changed.MatchingLive == 0 && changed.Status == "FAIL_current_live_or_unknown", "Live or changed-argv identity was accepted as absent.");
        });
        check("native audit PID reuse differs from microsecond rounding", () =>
        {
            var receipt = Receipt(); var identity = receipt.Processes[0]; var directory = Write(receipt);
            var reused = Audit(directory, pid => new("Present", pid, identity.ParentPid, identity.Started!.Value.AddTicks(11), identity.CommandLine, "windows", null));
            var rounded = Audit(directory, pid => new("Present", pid, identity.ParentPid, identity.Started!.Value.AddTicks(10), identity.CommandLine, "windows", null));
            Assert(reused.ReusedRecords == 1 && reused.Status.StartsWith("PASS_current_", StringComparison.Ordinal) && rounded.MatchingLive == 1,
                "Native birth comparison broadened the existing microsecond tolerance.");
        });
        check("native audit wrong observer PID or foreign platform remains UNKNOWN", () =>
        {
            var directory = Write(Receipt());
            var wrong = Audit(directory, pid => new("Absent", pid + 1, 0, null, null, "windows", null));
            var foreign = Audit(directory, pid => new("Absent", pid, 0, null, null, "linux", null));
            Assert(wrong.Unknown == 1 && foreign.Unknown == 1, "Wrong PID or observer platform proved native absence.");
        });
        check("native audit wrong result and registration shapes refused", () =>
        {
            var directory = Write(Receipt()); File.WriteAllText(Path.Combine(directory, "result.json"), "[]"); Rejected(directory);
            directory = Write(Receipt()); File.WriteAllText(Path.Combine(directory, "registered.json"), "{}"); Rejected(directory);
        });
        check("native audit missing fields cannot inherit permissive DTO defaults", () =>
        {
            var directory = Write(Receipt());
            var document = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "result.json")))!.AsObject();
            document.Remove("Status"); File.WriteAllText(Path.Combine(directory, "result.json"), document.ToJsonString()); Rejected(directory);
            directory = Write(Receipt());
            document = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "registered.json")))![0]!.AsObject();
            document.Remove("CommandLine"); File.WriteAllText(Path.Combine(directory, "registered.json"), "[" + document.ToJsonString() + "]"); Rejected(directory);
        });
        check("native audit duplicate PID births and ambiguous JSON keys refused", () =>
        {
            var receipt = Receipt(); receipt.Processes = [receipt.Processes[0], Identity(111, 110, [110])];
            receipt.Processes[1].Started = receipt.Processes[1].Started!.Value.AddSeconds(1); Rejected(Write(receipt));
            var directory = Write(Receipt());
            var path = Path.Combine(directory, "result.json"); var text = File.ReadAllText(path);
            File.WriteAllText(path, text.Insert(text.IndexOf('{') + 1, "\"Status\":\"PASS\",")); Rejected(directory);
        });
        check("native audit final birth argv parent and scope must bind registration", () =>
        {
            var mutations = new Action<ExecutionIdentity>[]
            {
                identity => identity.Started = identity.Started!.Value.AddSeconds(1),
                identity => identity.CommandLine += " changed",
                identity => { identity.ParentPid = 112; identity.ParentChain = [112]; },
                identity => identity.ScopeId = "11111111111111111111111111111111"
            };
            foreach (var mutate in mutations)
            {
                Bound(); var receipt = Receipt(); var original = Identity(111, 110, [110]);
                mutate(receipt.Processes[0]); Rejected(Write(receipt, [original]));
            }
        });
        check("native audit registered snapshots reconcile descendants without duplicate records", () =>
        {
            var receipt = Receipt(); receipt.Processes = [receipt.Processes[0], Identity(112, 111, [110, 111])];
            var directory = Write(receipt, [receipt.Processes[0]]);
            File.WriteAllBytes(Path.Combine(directory, "registered-002.json"), JsonSerializer.SerializeToUtf8Bytes(receipt.Processes, ExecutionJsonContext.Default.ExecutionIdentityArray));
            var result = Audit(directory);
            Assert(result.Receipts == 1 && result.IdentityRecords == 2 && result.UniquePids == 2 && result.AbsentRecords == 2,
                "Registration snapshots doubled counts or lost descendant identities.");
        });
        check("native audit final descendant without its unique birth snapshot is refused", () =>
        {
            var receipt = Receipt(); receipt.Processes = [receipt.Processes[0], Identity(112, 111, [110, 111])];
            var directory = Write(receipt, [receipt.Processes[0]]);
            var snapshot = Path.Combine(directory, "registered-002.json");
            File.WriteAllBytes(snapshot, JsonSerializer.SerializeToUtf8Bytes(receipt.Processes, ExecutionJsonContext.Default.ExecutionIdentityArray));
            var originalResult = File.ReadAllBytes(Path.Combine(directory, "result.json"));
            File.Delete(snapshot); Rejected(directory);
            Assert(File.ReadAllBytes(Path.Combine(directory, "result.json")).SequenceEqual(originalResult),
                "Missing descendant birth snapshot changed the original final result or status.");
        });
        check("native audit contradictory PASS and orphaned registration refused", () =>
        {
            var receipt = Receipt(); receipt.ExecutionStatus = "FAILED"; receipt.ExitCode = 7; Rejected(Write(receipt));
            var directory = Write(Receipt()); File.Delete(Path.Combine(directory, "result.json")); Rejected(directory);
        });
        check("native audit empty native identities and empty directory remain FAIL", () =>
        {
            var receipt = Receipt(); receipt.Processes = []; Rejected(Write(receipt));
            Bound(); var directory = Path.Combine(root, "native-audit-" + (++index).ToString("D2")); Directory.CreateDirectory(directory);
            Rejected(directory);
        });
        check("native audit Linux exec transition is pinned while boot reuse stays distinct", () =>
        {
            var receipt = Receipt(); receipt.Platform = "linux";
            foreach (var identity in new[] { receipt.Launcher!, receipt.Processes[0] })
            {
                Bound(); identity.Platform = "linux"; identity.Started = null; identity.StartTicks = (ulong)identity.Pid;
                identity.BootId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
                identity.CommandLine = "broker\0process\0broker\0configuration\0";
            }
            var rootIdentity = receipt.Processes[0]; rootIdentity.Ownership = "linux-pidfd";
            rootIdentity.ExpectedExecCommandLine = string.Join('\0', new[] { receipt.Executable }.Concat(receipt.ArgumentList)) + '\0';
            rootIdentity.AfterExecCommandLine = rootIdentity.ExpectedExecCommandLine;
            var directory = Write(receipt);
            var result = Audit(directory, pid => new("Present", pid, rootIdentity.ParentPid, null, rootIdentity.AfterExecCommandLine,
                "linux", rootIdentity.StartTicks, BootId: "bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee"));
            Assert(result.ReusedRecords == 1 && result.Status.StartsWith("PASS_current_", StringComparison.Ordinal), "Different native boot identity was treated as matching live.");
            rootIdentity.AfterExecCommandLine += "changed"; Rejected(Write(receipt));
        });
    }
}
