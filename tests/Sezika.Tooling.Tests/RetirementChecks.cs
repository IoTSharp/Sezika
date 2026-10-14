using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class RetirementChecks
{
    private sealed record Fixture(string Root, string Original, RetirementReviewRequest Request);
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40)); var fixtures = 0;
        Budget Limit() => new(35, null, cancellation.Token);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException("Expected retirement fixture rejection.");
        }
        void Write(string path, string text) { cancellation.Token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
        string Hash(string path) { using var stream = File.OpenRead(path); return Limit().Hash(stream, stream.Length); }
        ReviewFilePin Pin(string directory, string relative) { var path = SafePaths.Within(directory, relative, Limit()); return new(relative, new FileInfo(path).Length, Hash(path)); }
        void Freeze(string directory, MigrationEntry[] entries)
        {
            var inventory = new MigrationInventory("PASS_classification_only", new string('a', 64), entries.Length, entries.Count(entry => entry.Tracking == "tracked"), entries.Count(entry => entry.Tracking == "ignored-targeted-sample"), entries.Count(entry => entry.Tracking == "external-skill"), entries.Count(entry => entry.Tracking == "untracked-existing"), entries);
            File.WriteAllBytes(Path.Combine(directory, "classification.json"), JsonSerializer.SerializeToUtf8Bytes(inventory, RetirementJson.Default.MigrationInventory));
        }
        Fixture Prepare(string name, string callerText = "当前入口为 C# Replacement.Run。\n", string kind = "documentation", bool original = true, bool external = false)
        {
            cancellation.Token.ThrowIfCancellationRequested(); if (++fixtures > 20) throw new InvalidDataException("Retirement check fixture bound20.");
            var directory = Path.GetFullPath(Path.Combine(root, "retirement-" + name)); Directory.CreateDirectory(directory);
            var legacy = external ? Path.GetFullPath(Path.Combine(root, "retirement-external-" + name + ".ps1")) : "tools/old.ps1";
            var old = external ? legacy : Path.Combine(directory, legacy);
            if (original) Write(old, "Inert original script fixture bytes; never executed.\n");
            Write(Path.Combine(directory, "src", "Replacement.cs"), "// Synthetic gate source identity fixture. No feature acceptance claimed.\ninternal static class Replacement { }\n");
            Write(Path.Combine(directory, "gate.json"), "{\"Status\":\"PASS_explicit_fixture_gate\",\"SyntheticFixture\":true}");
            Write(Path.Combine(directory, "current.md"), callerText);
            var entry = new MigrationEntry(legacy, external ? "external-skill" : "tracked", "T3", "MIGRATE_TASK_CONTROL_ARCHIVE_ONE_OFF_REPAIRS", "task/bundle/inputs/checkpoint/resident", "PROPOSED_NOT_IMPLEMENTED"); Freeze(directory, [entry]);
            var gate = new ReplacementGate("fixture-gate", "T3", [legacy], "Replacement.Run", [Pin(directory, "src/Replacement.cs")], Pin(directory, "gate.json"), "PASS_explicit_fixture_gate", true);
            var request = new RetirementReviewRequest("classification.json", Hash(Path.Combine(directory, "classification.json")), directory, [gate], [new(Pin(directory, "current.md"), kind)], true, 1);
            return new(directory, old, request);
        }
        RetirementReport Review(Fixture fixture, RetirementReviewRequest? request = null) => RetirementReview.Review(request ?? fixture.Request, Limit());
        void NoMutation(RetirementReport report) => Assert(!report.RetirementPerformed && !report.OriginalScriptsExecuted && !report.SharedSkillsChanged && report.Status != "DONE", "A review became mutation/implementation acceptance.");

        check("Retirement tiny explicit gate and pinned caller yield review readiness only", () =>
        {
            var fixture = Prepare("tiny"); var before = File.ReadAllBytes(fixture.Original); var report = Review(fixture); NoMutation(report);
            Assert(report.Entries == 1 && report.Ready == 1 && report.Items[0].ReviewState == "READY" && report.Items[0].Current.Status == "PRESENT" && report.Items[0].Current.Sha256 == Hash(fixture.Original) && File.ReadAllBytes(fixture.Original).SequenceEqual(before), "Tiny review lost source identity or changed original bytes.");
        });
        check("Retirement mapping without an independently verified replacement stays pending", () =>
        {
            var fixture = Prepare("missing-gate"); var report = Review(fixture, fixture.Request with { Gates = [] }); NoMutation(report);
            Assert(report.Pending == 1 && report.Ready == 0 && report.MissingGates.Single().Path == "tools/old.ps1", "A mapping alone became accepted retirement.");
            var unverified = Review(fixture, fixture.Request with { Gates = [fixture.Request.Gates[0] with { IndependentlyVerified = false }] }); Assert(unverified.Pending == 1, "Unverified feature gate became READY.");
        });
        check("Retirement current interpreter and direct script commands block readiness", () =>
        {
            var fixture = Prepare("execution", "pwsh ./tools/old.ps1\n& './tools/old.ps1'\n"); var report = Review(fixture);
            Assert(report.Pending == 1 && report.Items[0].ExecutionReferences == 2 && report.Findings.All(finding => finding.State == "CURRENT_EXECUTION_REFERENCE"), "Current executable script references were hidden.");
        });
        check("Retirement explicit historical nested sections links and prohibitions are preserved references", () =>
        {
            var fixture = Prepare("history", "# 历史证据\n## Windows\n```powershell\npwsh tools/old.ps1\n```\n# 当前说明\n禁止运行 pwsh tools/old.ps1\n原始回执 [旧入口](tools/old.ps1) 仅供追溯\n"); var report = Review(fixture);
            Assert(report.Ready == 1 && report.Items[0].ExecutionReferences == 0 && report.Items[0].AmbiguousReferences == 0 && report.Findings.Any(finding => finding.State == "HISTORICAL_REFERENCE") && report.Findings.Any(finding => finding.State == "PROHIBITED_NOTICE"), "Explicit evidence/prohibition references became execution.");
        });
        check("Retirement historical receipt words cannot hide a current execution instruction", () =>
        {
            var fixture = Prepare("history-invocation", "执行 pwsh tools/old.ps1，并保留原始回执。\n"); var report = Review(fixture); NoMutation(report);
            Assert(report.Ready == 0 && report.Pending == 1 && report.Items[0].ExecutionReferences == 1 &&
                report.Findings.Any(finding => finding.State == "CURRENT_EXECUTION_REFERENCE"), "An inline receipt-history word hid a current script invocation.");
        });
        check("Retirement unresolved code paths remain review required", () =>
        {
            var fixture = Prepare("ambiguous", "var helper = \"tools/old.ps1\";\nProcess.Start(helper);\n", "code"); var report = Review(fixture);
            Assert(report.ReviewRequired == 1 && report.Items[0].AmbiguousReferences == 1, "A dynamic code reference silently became READY.");
            Write(Path.Combine(fixture.Root, "current.md"), "Process.Start(\"pwsh\", \"tools/\" + \"old\" + \".ps1\");\n");
            var dynamic = Review(fixture, fixture.Request with { CurrentCallers = [new(Pin(fixture.Root, "current.md"), "code")] });
            Assert(dynamic.ReviewRequired == 1 && dynamic.Findings.Any(finding => finding.OriginalPath == "*" && finding.State == "REVIEW_REQUIRED"), "Constructed script names disappeared from the review boundary.");
        });
        check("Retirement interpreter invocation with a split extension remains review required", () =>
        {
            var fixture = Prepare("split-extension", "Process.Start(\"pwsh\", \"tools/\" + \"old\" + \".ps\" + \"1\");\n", "code"); var report = Review(fixture); NoMutation(report);
            Assert(report.Ready == 0 && report.ReviewRequired == 1 && report.Items[0].AmbiguousReferences > 0 &&
                report.Findings.Any(finding => finding.OriginalPath == "*" && finding.State == "REVIEW_REQUIRED"), "A suspected interpreter invocation without a complete literal extension became READY.");
        });
        check("Retirement missing original retains UNKNOWN NOT_FOUND and the denominator", () =>
        {
            var fixture = Prepare("missing", original: false); var report = Review(fixture); var item = report.Items.Single();
            Assert(report.Entries == 1 && report.Unknown == 1 && item.ReviewState == "UNKNOWN" && item.Current.Status == "NOT_FOUND" && item.Current.Bytes is null && item.Current.Sha256 is null, "A missing original was dropped or assigned fictitious identity.");
        });
        check("Retirement changed replacement source invalidates the explicit gate", () =>
        {
            var fixture = Prepare("source-changed"); File.AppendAllText(Path.Combine(fixture.Root, "src", "Replacement.cs"), "// changed after verification\n"); var report = Review(fixture);
            Assert(report.Pending == 1 && report.Gates.Single().Status == "PENDING" && report.MissingGates.Single().Reason.StartsWith("REPLACEMENT_GATE_PENDING", StringComparison.Ordinal), "A stale source SHA retained an accepted gate.");
        });
        check("Retirement validation status and malformed receipt cannot certify a replacement", () =>
        {
            var fixture = Prepare("receipt"); Write(Path.Combine(fixture.Root, "gate.json"), "{\"Status\":\"NOT_RUN\"}");
            var gate = fixture.Request.Gates[0] with { Receipt = Pin(fixture.Root, "gate.json") };
            Assert(Review(fixture, fixture.Request with { Gates = [gate] }).Pending == 1, "A NOT_RUN validation certified retirement.");
            Write(Path.Combine(fixture.Root, "gate.json"), "[]"); gate = gate with { Receipt = Pin(fixture.Root, "gate.json") };
            Assert(Review(fixture, fixture.Request with { Gates = [gate] }).Pending == 1, "Nonobject evidence certified retirement.");
        });
        check("Retirement external entry records project dependency release while preserving the shared source", () =>
        {
            var fixture = Prepare("external", external: true); var before = File.ReadAllBytes(fixture.Original); var pending = Review(fixture); Assert(pending.Pending == 1, "External dependency was implicitly removed.");
            var report = Review(fixture, fixture.Request with { Gates = [fixture.Request.Gates[0] with { ProjectExternalDependencyReleased = true }] }); NoMutation(report);
            Assert(report.Ready == 1 && report.Items.Single().ProjectExternalDependencyReleased && File.ReadAllBytes(fixture.Original).SequenceEqual(before), "Project dependency release mutated or retired the machine-wide skill.");
        });
        check("Retirement mismatched or undeclared caller scope cannot produce READY", () =>
        {
            var fixture = Prepare("caller-changed"); Write(Path.Combine(fixture.Root, "current.md"), "pwsh tools/old.ps1\n"); var report = Review(fixture);
            Assert(report.Pending == 1 && report.Callers.Single().Status == "PIN_MISMATCH" && !report.CallerScopeDeclaredComplete, "Unpinned caller bytes became no-current-callers evidence.");
            var request = fixture.Request with { CurrentCallers = [new(Pin(fixture.Root, "current.md"), "documentation")], CallerScopeDeclaredComplete = false };
            Assert(Review(fixture, request).Ready == 0, "An undeclared current caller scope certified readiness.");
        });
        check("Retirement synthetic 466 item fixture retains missing sources and exact gate gaps", () =>
        {
            var fixture = Prepare("466"); var entries = new MigrationEntry[466];
            entries[0] = new("tools/old.ps1", "tracked", "T3", "MIGRATE_TASK_CONTROL_ARCHIVE_ONE_OFF_REPAIRS", "task/bundle/inputs/checkpoint/resident", "PROPOSED_NOT_IMPLEMENTED");
            for (var index = 1; index < entries.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested(); entries[index] = entries[0] with { Path = "tools/missing-" + index.ToString("D3") + ".ps1" };
            }
            Freeze(fixture.Root, entries); var request = fixture.Request with { ClassificationSha256 = Hash(Path.Combine(fixture.Root, "classification.json")), ExpectedEntries = 466 }; var report = Review(fixture, request); NoMutation(report);
            Assert(report.Entries == 466 && report.Items.Length == 466 && report.Ready == 1 && report.Unknown == 465 && report.MissingGates.Length == 465 && report.Ready + report.Pending + report.ReviewRequired + report.Unknown == 466, "466-row denominator or exact gate-gap accounting changed.");
        });
        check("Retirement frozen SHA unknown gate paths and cancellation reject without mutation", () =>
        {
            var fixture = Prepare("rejections"); var before = File.ReadAllBytes(fixture.Original);
            Reject(() => Review(fixture, fixture.Request with { ClassificationSha256 = new string('0', 64) }));
            Reject(() => Review(fixture, fixture.Request with { Gates = [fixture.Request.Gates[0] with { OriginalPaths = ["tools/unclassified.ps1"] }] }));
            Reject(() => RetirementReview.Review(fixture.Request, new Budget(10, null, new CancellationToken(canceled: true))));
            Assert(File.ReadAllBytes(fixture.Original).SequenceEqual(before), "Rejected review mutated the inert original.");
        });
        check("Retirement independently reviewed schema metadata binds full caller and exact line", () =>
        {
            const string line = "var metadata = new { upstream_executed = false, source = \"tools/old.ps1\" };";
            var fixture = Prepare("reviewed-schema", line + "\n", "code");
            Assert(Review(fixture).Ready == 0, "Unreviewed schema metadata silently became READY.");
            var caller = fixture.Request.CurrentCallers[0].File;
            var review = new RetirementReviewedLine(caller.Path, caller.Sha256, 1,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(line))),
                "schema-metadata", "Independent fixture review: the source path is inert provenance metadata; no process is invoked.", true);
            var report = Review(fixture, fixture.Request with { ReviewedNonExecutionLines = [review] }); NoMutation(report);
            Assert(report.Ready == 1 && report.ReviewedNonExecutionLines.Single() == review &&
                report.Findings.Single().State == "INDEPENDENT_NON_EXECUTION_REVIEW", "Explicit review was lost or presented as automatic execution analysis.");
            var noGate = Review(fixture, fixture.Request with { ReviewedNonExecutionLines = [review], Gates = [] });
            Assert(noGate.Ready == 0 && noGate.Pending == 1, "A line review replaced the independent capability gate.");
        });
        check("Retirement unverified duplicate stale line and wrong-category reviews cannot hide references", () =>
        {
            const string line = "旧入口 tools/old.ps1 仅作迁移对应表。";
            var fixture = Prepare("review-pins", line + "\n"); var caller = fixture.Request.CurrentCallers[0].File;
            var review = new RetirementReviewedLine(caller.Path, caller.Sha256, 1,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(line))),
                "retired-entry-mapping", "Independent fixture review confirms this is a retired-entry mapping without an execution instruction.", true);
            foreach (var invalid in new[] { review with { IndependentlyVerified = false }, review with { LineSha256 = new string('0', 64) },
                review with { CallerSha256 = new string('0', 64) }, review with { Line = 2 }, review with { Category = "rejection-fixture" } })
                Reject(() => Review(fixture, fixture.Request with { ReviewedNonExecutionLines = [invalid] }));
            Reject(() => Review(fixture, fixture.Request with { ReviewedNonExecutionLines = [review, review] }));
            Write(Path.Combine(fixture.Root, "current.md"), line + " changed\n");
            Reject(() => Review(fixture, fixture.Request with { CurrentCallers = [new(Pin(fixture.Root, "current.md"), "documentation")], ReviewedNonExecutionLines = [review] }));
        });
        check("Retirement named historical archive decision preserves bytes without claiming capability equivalence", () =>
        {
            var fixture = Prepare("archive-only"); var original = Pin(fixture.Root, "tools/old.ps1");
            var decision = new RetirementArchiveDecision(original, "One fixed historical experiment receipt aggregation recipe.",
                "Independent owner review retires this exact experiment recipe; reusable capabilities remain separately reviewed.", true);
            var request = fixture.Request with { Gates = [], HistoricalArchiveDecisions = [decision] };
            var report = Review(fixture, request); NoMutation(report);
            Assert(report.Ready == 1 && report.MissingGates.Length == 0 && report.Gates.Length == 0 &&
                report.Items.Single().GateId is null && report.Items.Single().ReviewBasis == "HISTORICAL_RECIPE_ARCHIVE_DECISION" &&
                report.Items.Single().Disposition == "MIGRATE_TASK_CONTROL_ARCHIVE_ONE_OFF_REPAIRS" &&
                report.HistoricalArchiveDecisions.Single() == decision && Pin(fixture.Root, original.Path) == original,
                "Archive-only owner disposition rewrote baseline intent, mutated original bytes or claimed a replacement feature gate.");
        });
        check("Retirement archive decisions reject unverified changed duplicate external or conflicting originals", () =>
        {
            var fixture = Prepare("archive-rejections");
            var decision = new RetirementArchiveDecision(Pin(fixture.Root, "tools/old.ps1"), "One fixed historical experiment receipt aggregation recipe.",
                "Independent owner review retires this exact historical recipe and preserves all original evidence bytes.", true);
            var request = fixture.Request with { Gates = [], HistoricalArchiveDecisions = [decision] };
            foreach (var invalid in new[] { decision with { IndependentlyVerified = false }, decision with { Purpose = "short" },
                decision with { Reason = "short" }, decision with { Purpose = new string(' ', 20) }, decision with { Reason = new string(' ', 20) },
                decision with { Purpose = "meaningful purpose padded\nwith control" }, decision with { Original = decision.Original with { Sha256 = new string('0', 64) } },
                decision with { Original = decision.Original with { Bytes = decision.Original.Bytes + 1 } } })
                Reject(() => Review(fixture, request with { HistoricalArchiveDecisions = [invalid] }));
            Reject(() => Review(fixture, request with { HistoricalArchiveDecisions = [decision, decision] }));
            Reject(() => Review(fixture, request with { Gates = fixture.Request.Gates }));
            var external = Prepare("archive-external", external: true);
            var externalDecision = decision with { Original = new(external.Original, new FileInfo(external.Original).Length, Hash(external.Original)) };
            Reject(() => Review(external, external.Request with { Gates = [], HistoricalArchiveDecisions = [externalDecision] }));
            File.AppendAllText(fixture.Original, "changed\n"); Reject(() => Review(fixture, request));
        });
        check("Retirement archive disposition cannot override current execution or incomplete caller scope", () =>
        {
            var fixture = Prepare("archive-callers", "pwsh tools/old.ps1\n");
            var decision = new RetirementArchiveDecision(Pin(fixture.Root, "tools/old.ps1"), "One fixed historical experiment receipt aggregation recipe.",
                "Independent owner review retires this exact historical recipe, subject to current caller removal and scope checks.", true);
            var request = fixture.Request with { Gates = [], HistoricalArchiveDecisions = [decision] };
            var report = Review(fixture, request); NoMutation(report);
            Assert(report.Ready == 0 && report.Pending == 1 && report.Items.Single().ExecutionReferences == 1,
                "Archive owner disposition concealed a current execution instruction.");
            Write(Path.Combine(fixture.Root, "current.md"), "Current entry uses the reviewed C# replacement only.\n");
            report = Review(fixture, request with { CurrentCallers = [new(Pin(fixture.Root, "current.md"), "documentation")], CallerScopeDeclaredComplete = false });
            Assert(report.Ready == 0 && report.Pending == 1 && report.Items.Single().Reasons.Contains("CURRENT_CALLER_SCOPE_NOT_COMPLETE_OR_PINNED"),
                "Archive disposition replaced complete current caller scope.");
        });
    }
}
