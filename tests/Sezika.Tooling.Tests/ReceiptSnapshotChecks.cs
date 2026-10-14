using System.Text;
using Sezika.Tooling;

internal static class ReceiptSnapshotChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var budget = new Budget(15, null, cancellation.Token);
        var source = Path.Combine(root, "receipt-source"); Directory.CreateDirectory(source);
        var abc = Path.Combine(source, "a.txt"); File.WriteAllText(abc, "abc", new UTF8Encoding(false));
        const string abcSha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        var request = new ReceiptSnapshotRequest(source, ["a.txt"]);
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException("Expected receipt snapshot refusal.");
        }
        check("receipt snapshot known abc bytes SHA256 and Base64 tiny", () =>
        {
            var report = ReceiptSnapshot.Capture(request, budget);
            Assert(report.Status == "PASS_named_small_receipt_snapshot_only" && report.RequestedFiles == 1 && report.CompletedFiles == 1 &&
                report.TotalBytes == 3 && report.Files is [{ Path: "a.txt", Bytes: 3, Sha256: abcSha, Base64: "YWJj" }] &&
                report.ReadOnly && !report.AtomicProducerSnapshot && !report.PublicReleaseAccepted && !report.TrainingAccepted,
                "Snapshot differs from independent abc golden or overclaims acceptance.");
        });
        check("receipt snapshot keeps original failed state bytes and source unchanged", () =>
        {
            const string raw = "{\"Status\":\"FAIL\",\"RemainingProcesses\":3}\r\n";
            var file = Path.Combine(source, "failed.json"); File.WriteAllText(file, raw, new UTF8Encoding(false));
            var report = ReceiptSnapshot.Capture(request with { Paths = ["failed.json"] }, budget);
            Assert(report.Status.StartsWith("PASS", StringComparison.Ordinal) &&
                Encoding.UTF8.GetString(Convert.FromBase64String(report.Files.Single().Base64)) == raw && File.ReadAllText(file) == raw,
                "Failed receipt bytes were normalized, promoted or overwritten.");
        });
        check("receipt snapshot missing later input retains completed denominator", () =>
        {
            var report = ReceiptSnapshot.Capture(request with { Paths = ["a.txt", "missing.json"] }, budget);
            Assert(report.Status == "FAIL_named_receipt_snapshot_partial" && report.RequestedFiles == 2 && report.CompletedFiles == 1 &&
                report.Files.Single().Base64 == "YWJj" && report.TotalBytes == 3, "Missing source erased partial evidence or claimed completion.");
        });
        check("receipt snapshot rejects traversal script duplicate and invalid resource bounds", () =>
        {
            foreach (var paths in new[] { new[] { "../outside.txt" }, new[] { "old.sh" }, new[] { "Module.psm1" }, new[] { "a.txt", "A.txt" } })
            { budget.Check(); Reject(() => ReceiptSnapshot.Capture(request with { Paths = paths }, budget)); }
            Reject(() => ReceiptSnapshot.Capture(request with { MaximumFiles = 33 }, budget));
            Reject(() => ReceiptSnapshot.Capture(request with { MaximumFileBytes = 65537 }, budget));
            Reject(() => ReceiptSnapshot.Capture(request with { MaximumTotalBytes = 1048577 }, budget));
            Reject(() => ReceiptSnapshot.Capture(request with { MaximumSeconds = 21 }, budget));
        });
        check("receipt snapshot per-file and total byte caps fail without false completion", () =>
        {
            var tooSmall = ReceiptSnapshot.Capture(request with { MaximumFileBytes = 2 }, budget);
            Assert(tooSmall.Status.StartsWith("FAIL", StringComparison.Ordinal) && tooSmall.CompletedFiles == 0, "Per-file cap bypassed.");
            File.WriteAllText(Path.Combine(source, "b.txt"), "def", new UTF8Encoding(false));
            var total = ReceiptSnapshot.Capture(request with { Paths = ["a.txt", "b.txt"], MaximumTotalBytes = 5 }, budget);
            Assert(total.Status.StartsWith("FAIL", StringComparison.Ordinal) && total.CompletedFiles == 1 && total.TotalBytes == 3, "Total cap or partial count differs.");
        });
        check("receipt snapshot complete expected pins detect changed source", () =>
        {
            var pinned = request with { ExpectedFiles = [new("a.txt", 3, abcSha)] };
            Assert(ReceiptSnapshot.Capture(pinned, budget).Status.StartsWith("PASS", StringComparison.Ordinal), "Exact independent known pin refused.");
            File.WriteAllText(abc, "abd", new UTF8Encoding(false));
            try
            {
                var changed = ReceiptSnapshot.Capture(pinned, budget);
                Assert(changed.Status.StartsWith("FAIL", StringComparison.Ordinal) && changed.CompletedFiles == 0, "Changed source accepted under original SHA.");
            }
            finally { File.WriteAllText(abc, "abc", new UTF8Encoding(false)); }
            Reject(() => ReceiptSnapshot.Capture(request with { ExpectedFiles = [] }, budget));
        });
        check("receipt snapshot parent cancellation precedes source reads", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Reject(() => ReceiptSnapshot.Capture(request, new Budget(3, null, cancelled.Token)));
            Assert(File.ReadAllText(abc) == "abc", "Cancellation modified source.");
        });
        if (OperatingSystem.IsLinux()) check("receipt snapshot rejects actual filesystem symbolic link", () =>
        {
            var link = Path.Combine(source, "alias.txt"); File.CreateSymbolicLink(link, abc);
            var rejected = ReceiptSnapshot.Capture(request with { Paths = ["alias.txt"] }, budget);
            Assert(rejected.Status.StartsWith("FAIL", StringComparison.Ordinal) && rejected.CompletedFiles == 0 && File.ReadAllText(abc) == "abc",
                "Symlink source escaped scope or changed its target.");
        });
    }
}
