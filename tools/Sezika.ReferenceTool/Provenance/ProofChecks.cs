using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.PortProvenance;

namespace Sezika.ReferenceTool.Provenance;

internal sealed record ProofChecksProbe(FilePin BuildRequest, FilePin BuildExecution, FilePin CompilerExecutable);
internal sealed record ProofChecksRequest(FilePin BuildSummary, string FixtureDirectory,
    ProofChecksProbe? OptionalProbe = null, string? CancelFile = null);
internal sealed record ProofCheckStep(string Name, string Status, string ExpectedGate, string? ObservedGate,
    string? TargetPath = null, FilePin? FixturePin = null);
internal sealed record ProofCheckCase(string Name, string Status, ProofCheckStep[] Steps, string? Reason = null);
internal sealed record ProofChecksReport(string Status, int Planned, int Completed, int Passed, int Failed,
    int NotRun, ProofCheckCase[] Cases, FilePin Request, FilePin BuildSummary, PortCompiledProofResult? PositiveControl,
    string FixtureDirectory, FilePin[] FixtureFiles, long FixtureBytes, double ElapsedSeconds, long ObservedPeakBytes,
    string CleanupStatus, bool FixtureArtifactsRetained, [property: JsonPropertyName("fixture_only")] bool FixtureOnly = true, bool UpstreamExecuted = false,
    bool RepresentsParity = false, bool RepresentsQuality = false, bool RepresentsCalibration = false,
    bool NativeAotVerified = false, bool ExecutionReceiptsMutated = false, int ChildProcessesStarted = 0,
    string ExactCodeViewGuidNegative = "NOT_RUN_requires_independent_real_build_with_different_pdb");

[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ProofChecksRequest))]
[JsonSerializable(typeof(ProofChecksReport))]
internal partial class ProofChecksJson : JsonSerializerContext;

// Only real pinned artifacts enter these checks. Metadata mutants are explicitly fixtures;
// successful execution receipts are never generated, edited, or substituted by simulations.
internal static class ProofChecks
{
    private const string ExactBuildBinding = "Exact successful build execution/request/compiler/root/cleanup binding absent.";
    private static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static int Run(string requestPath, string outputPath, int maximum, CancellationToken cancellation)
    {
        if (maximum is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maximum), "Proof checks bound1..8.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var budget = new Budget(30, 536870912, null, deadline.Token);
        var requestPin = budget.Pin(requestPath, 4194304);
        var request = budget.Read(requestPin, ProofChecksJson.Default.ProofChecksRequest, 4194304);
        if (request.CancelFile is not null) Budget.Safe(request.CancelFile);
        void Check()
        {
            budget.Check();
            if (request.CancelFile is not null && File.Exists(request.CancelFile))
                throw new OperationCanceledException("Proof-check cancellation file exists.");
        }
        Check(); Budget.Safe(outputPath); Budget.Safe(request.FixtureDirectory);
        if (Path.Exists(outputPath) || !Directory.Exists(Path.GetDirectoryName(outputPath)))
            throw new IOException("Proof-check report requires a fresh file and existing parent.");
        var summary = budget.Read(request.BuildSummary, CompiledProgramJson.Default.PortBuildSummary, 4194304);
        var cases = new List<ProofCheckCase>(maximum); PortCompiledProofResult? positive = null;
        // A failed positive control cannot authorize interpreting any negative result.
        try
        {
            positive = CompiledProgramProof.Verify(summary, deadline.Token, Check);
            if (positive.Status != "SOURCE_BUILD_ONLY") throw new InvalidDataException("Positive compiled-proof scope differs.");
            cases.Add(new("real-pinned-source-build-positive-control", "PASS",
                [new("shared-verifier-positive", "PASS", "SOURCE_BUILD_ONLY", positive.Status)]));
        }
        catch (InvalidDataException error)
        {
            positive = null;
            cases.Add(new("real-pinned-source-build-positive-control", "FAIL",
                [new("shared-verifier-positive", "FAIL", "SOURCE_BUILD_ONLY", error.Message)]));
        }
        Console.WriteLine("proof-check 1/" + maximum + " " + cases[0].Name + " status=" + cases[0].Status);
        var fixture = new OwnedFixtures(request.FixtureDirectory, budget, Check);
        var reportWritten = false; var reportCreated = false;
        try
        {
            if (positive is not null)
            {
                var build = budget.Read(Local(summary.BuildRequest), CompiledProgramJson.Default.ProgramBuildRequest, 4194304);
                if (Within(outputPath, build.WorkingDirectory) || Within(request.FixtureDirectory, build.WorkingDirectory) ||
                    SamePath(request.FixtureDirectory, build.WorkingDirectory) || Within(outputPath, request.FixtureDirectory))
                    throw new InvalidDataException("Proof-check artifacts must stay outside immutable source/runtime and the report outside fixture storage.");
                if (maximum > 1) fixture.Create();
                for (var index = 1; index < maximum && index < 8; index++)
                {
                    Check();
                    var result = RunCase(index, summary, build, request.OptionalProbe, fixture, budget, Check, deadline.Token);
                    cases.Add(result);
                    Console.WriteLine("proof-check " + (index + 1) + "/" + maximum + " " + result.Name + " status=" + result.Status);
                }
                // The real positive summary must still pass after every metadata/copy-only fixture.
                _ = CompiledProgramProof.Verify(summary, deadline.Token, Check);
            }
            else
            {
                for (var index = 1; index < maximum && index < 8; index++)
                {
                    Check(); cases.Add(new(CaseName(index), "NOT_RUN", [], "Real positive control failed."));
                    Console.WriteLine("proof-check " + (index + 1) + "/" + maximum + " " + CaseName(index) + " status=NOT_RUN");
                }
            }
            Check(); budget.Verify(requestPin, 4194304); budget.Verify(request.BuildSummary, 4194304);
            fixture.Verify();
            var passed = cases.Count(item => item.Status == "PASS"); var failed = cases.Count(item => item.Status == "FAIL");
            var notRun = cases.Count(item => item.Status == "NOT_RUN");
            var status = failed != 0 ? "FAIL_COMPILED_PROOF_FIXTURE_ONLY" :
                notRun != 0 ? "INCOMPLETE_COMPILED_PROOF_FIXTURE_ONLY" : "PASS_COMPILED_PROOF_FIXTURE_ONLY";
            var report = new ProofChecksReport(status, maximum, passed + failed, passed, failed, notRun, cases.ToArray(),
                requestPin, request.BuildSummary, positive, request.FixtureDirectory, fixture.Files, fixture.Bytes,
                budget.Elapsed, budget.PeakBytes, fixture.Created ? "PASS_no_child_processes_owned_fixture_evidence_retained" :
                    "PASS_no_child_processes_no_fixture_files_created", fixture.Created,
                ExactCodeViewGuidNegative: cases.FirstOrDefault(item => item.Name == CaseName(4))?.Status switch
                {
                    "PASS" => "PASS_real_managed_pe_guid_only_copy_rejected_by_exact_production_gate",
                    "FAIL" => "FAIL_real_managed_pe_guid_only_copy_negative",
                    _ => "NOT_RUN_real_managed_pe_guid_only_copy_negative"
                });
            var reportBytes = JsonSerializer.SerializeToUtf8Bytes(report, ProofChecksJson.Default.ProofChecksReport);
            if (reportBytes.Length > 4194304) throw new InvalidDataException("Proof-check report byte bound4MiB.");
            Check();
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                reportCreated = true; output.Write(reportBytes); output.Flush(true);
            }
            reportWritten = true;
            return failed == 0 && notRun == 0 ? 0 : 1;
        }
        finally
        {
            // A completed report retains its two real fixture artifacts for independent readback.
            // An interrupted run removes only exact files created by this invocation, never a tree.
            if (!reportWritten)
            {
                try
                {
                    if (reportCreated && File.Exists(outputPath)) { Budget.Safe(outputPath); File.Delete(outputPath); }
                }
                finally { fixture.Cleanup(); }
            }
        }
    }

    private static ProofCheckCase RunCase(int index, PortBuildSummary summary, ProgramBuildRequest build,
        ProofChecksProbe? probe, OwnedFixtures fixture, Budget budget, Action check, CancellationToken cancellation)
    {
        switch (index)
        {
            case 1:
            {
                var source = summary.ProgramSources.Where(pin => Path.GetFileName(pin.Path) == "LayaForward.cs").ToArray();
                if (source.Length != 1) return Missing(index, "Exactly one real declared LayaForward.cs is required.");
                var mutant = summary with { ProgramSources = summary.ProgramSources.Where(pin => !SamePath(pin.Path, source[0].Path)).ToArray() };
                return Result(index, Reject("omitted-laya-forward-source", mutant,
                    "PDB compiled C# source is undeclared or duplicate: " + source[0].Path, check, cancellation, source[0].Path));
            }
            case 2:
            {
                if (summary.GeneratedSources.Length == 0) return Missing(index, "Actual positive generated-source inventory is empty.");
                var omitted = summary.GeneratedSources[0];
                var mutant = summary with { GeneratedSources = summary.GeneratedSources.Skip(1).ToArray() };
                return Result(index, Reject("omitted-real-generated-source", mutant,
                    "PDB compiled C# source is undeclared or duplicate: " + omitted.DocumentPath, check, cancellation, omitted.DocumentPath));
            }
            case 3:
            {
                var inputs = summary.ProgramBuildInputs.Where(pin => !Path.GetExtension(pin.Path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (inputs.Length == 0) return Missing(index, "A real non-project conventional build input is required.");
                var omitted = inputs[0];
                var mutant = summary with { ProgramBuildInputs = summary.ProgramBuildInputs.Where(pin => !SamePath(pin.Path, omitted.Path)).ToArray() };
                return Result(index, Reject("omitted-real-build-input", mutant,
                    "Actual snapshot build input is omitted: " + omitted.Path, check, cancellation, omitted.Path));
            }
            case 4:
            {
                return GuidOnlyNegative(index, summary, fixture, budget, check, cancellation);
            }
            case 5:
            {
                // Both false claims retain the unchanged, actual successful build execution receipt.
                var wrongCompiler = Reject("compiler-pin-does-not-bind-actual-execution",
                    summary with { CompilerExecutable = summary.RuntimeExecutable }, ExactBuildBinding,
                    check, cancellation, summary.RuntimeExecutable.Path);
                var args = (string[])build.ArgumentList.Clone();
                if (args.Length < 2) return Missing(index, "Real build project argv is absent.");
                args[1] = summary.ProgramSources.Single(pin => Path.GetFileName(pin.Path) == "LayaForward.cs").Path;
                var metadata = fixture.Write(JsonSerializer.SerializeToUtf8Bytes(build with { ArgumentList = args },
                    CompiledProgramJson.Default.ProgramBuildRequest), "fixture-mutated-build-request.json");
                var wrongProject = Reject("project-argv-claim-does-not-bind-actual-execution",
                    summary with { BuildRequest = ProgramPin(metadata) }, ExactBuildBinding,
                    check, cancellation, args[1], metadata);
                return Result(index, wrongCompiler, wrongProject);
            }
            case 6:
            {
                var omittedPath = Path.Combine(summary.RuntimeDirectory, "Sezika.ReferenceTool.runtimeconfig.json");
                if (!summary.RuntimeFiles.Any(pin => SamePath(pin.Path, omittedPath)))
                    return Missing(index, "Positive runtimeconfig inventory member is absent.");
                var mutant = summary with { RuntimeFiles = summary.RuntimeFiles.Where(pin => !SamePath(pin.Path, omittedPath)).ToArray() };
                return Result(index, Reject("omitted-mandatory-runtime-member", mutant,
                    "Mandatory framework-dependent runtime file missing: Sezika.ReferenceTool.runtimeconfig.json",
                    check, cancellation, omittedPath));
            }
            case 7:
            {
                if (probe is null) return new(CaseName(index), "NOT_RUN", [], "No separately pinned genuine native probe execution was supplied.");
                var nativeRequest = budget.Read(probe.BuildRequest, CompiledProgramJson.Default.ProgramBuildRequest, 4194304);
                _ = budget.Read(probe.BuildExecution, CompiledProgramJson.Default.ProgramBuildExecution, 20971520);
                budget.Verify(probe.CompilerExecutable, 536870912);
                if (nativeRequest.ArgumentList is not ["--version"] ||
                    Path.GetFileName(nativeRequest.Executable) is not ("git" or "git.exe"))
                    return Missing(index, "Optional probe must be a genuine pinned git --version request/execution/compiler triple.");
                var mutant = summary with { BuildRequest = ProgramPin(probe.BuildRequest), BuildExecution = ProgramPin(probe.BuildExecution),
                    CompilerExecutable = ProgramPin(probe.CompilerExecutable) };
                var result = Result(index, Reject("genuine-git-version-is-not-a-build", mutant,
                    "Build execution resource/argument inventory bounds differ.", check, cancellation, probe.BuildExecution.Path));
                budget.Verify(probe.BuildRequest, 4194304); budget.Verify(probe.BuildExecution, 20971520);
                budget.Verify(probe.CompilerExecutable, 536870912); return result;
            }
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static ProofCheckStep Reject(string name, PortBuildSummary mutant, string expected, Action check,
        CancellationToken cancellation, string? target = null, FilePin? fixturePin = null)
    {
        check();
        try
        {
            var accepted = CompiledProgramProof.Verify(mutant, cancellation, check);
            return new(name, "FAIL", expected, "Unexpectedly accepted: " + accepted.Status, target, fixturePin);
        }
        catch (InvalidDataException error)
        {
            return new(name, error.Message == expected ? "PASS" : "FAIL", expected, error.Message, target, fixturePin);
        }
    }
    private static ProofCheckCase Result(int index, params ProofCheckStep[] steps) =>
        new(CaseName(index), steps.All(step => step.Status == "PASS") ? "PASS" : "FAIL", steps);
    private static ProofCheckCase Missing(int index, string reason) => new(CaseName(index), "FAIL", [], reason);
    private static string CaseName(int index) => index switch
    {
        1 => "undeclared-real-handwritten-source-rejected",
        2 => "undeclared-real-generated-source-rejected",
        3 => "omitted-real-build-input-rejected",
        4 => "real-managed-pe-guid-only-copy-rejected",
        5 => "actual-compiler-and-project-execution-binding-required",
        6 => "incomplete-real-runtime-inventory-rejected",
        7 => "genuine-non-build-probe-rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    private static FilePin Local(ProgramFilePin pin) => new(pin.Path, pin.Bytes, pin.Sha256);
    private static ProgramFilePin ProgramPin(FilePin pin) => new(pin.Path, pin.Bytes, pin.Sha256);
    private static bool SamePath(string left, string right) => Paths.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    private static bool Within(string path, string directory) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static ProofCheckCase GuidOnlyNegative(int index, PortBuildSummary summary, OwnedFixtures fixture,
        Budget budget, Action check, CancellationToken cancellation)
    {
        check();
        if (summary.CompiledAssembly.Bytes is < 1 or > 4194304)
            return Missing(index, "Real managed PE must fit the explicit4MiB GUID-only fixture bound.");
        var original = budget.Bytes(Local(summary.CompiledAssembly), 4194304);
        using var originalStream = new MemoryStream(original, writable: false);
        using var originalImage = new PEReader(originalStream, PEStreamOptions.LeaveOpen);
        var entries = originalImage.ReadDebugDirectory();
        if (!originalImage.HasMetadata || entries.Length is < 1 or > 32)
            return Missing(index, "Real managed PE metadata/debug-directory is absent or exceeds32.");
        var codeViews = entries.Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (codeViews.Length != 1 || !codeViews[0].IsPortableCodeView)
            return Missing(index, "Exactly one real Portable-PDB CodeView record is required.");
        var entry = codeViews[0]; var codeView = originalImage.ReadCodeViewDebugDirectoryData(entry);
        var offset = checked(entry.DataPointer + 4);
        if (entry.DataPointer < 0 || entry.DataSize < 24 || entry.DataPointer > original.Length - entry.DataSize ||
            !original.AsSpan(entry.DataPointer, 4).SequenceEqual("RSDS"u8))
            return Missing(index, "Real CodeView RSDS signature/GUID extent differs.");
        var modified = (byte[])original.Clone(); modified[offset] ^= 1;
        var differences = 0;
        for (var position = 0; position < original.Length && position < 4194304; position++)
        {
            if ((position & 4095) == 0) check();
            if (original[position] != modified[position])
            {
                if (position < offset || position >= offset + 16) throw new InvalidDataException("Fixture changed bytes outside the exact CodeView GUID.");
                differences++;
            }
        }
        using var modifiedStream = new MemoryStream(modified, writable: false);
        using var modifiedImage = new PEReader(modifiedStream, PEStreamOptions.LeaveOpen);
        var modifiedEntries = modifiedImage.ReadDebugDirectory();
        var modifiedViews = modifiedEntries.Where(item => item.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (differences != 1 || !modifiedImage.HasMetadata || modifiedEntries.Length != entries.Length || modifiedViews.Length != 1 ||
            !modifiedViews[0].Equals(entry))
            throw new InvalidDataException("GUID-only fixture changed the PE/debug-directory structure.");
        var modifiedCodeView = modifiedImage.ReadCodeViewDebugDirectoryData(modifiedViews[0]);
        if (modifiedCodeView.Guid == codeView.Guid || modifiedCodeView.Age != codeView.Age || modifiedCodeView.Path != codeView.Path)
            throw new InvalidDataException("GUID-only fixture changed age/path or retained its original GUID.");
        var retained = fixture.Write(modified, "Sezika.ReferenceTool.guid-only-mutant.dll");
        var runtimeParent = Path.GetFullPath(summary.RuntimeDirectory); Budget.Safe(runtimeParent);
        var runtimeDirectory = Path.GetFullPath(Path.Combine(runtimeParent, "proof-guid-" + Guid.NewGuid().ToString("N")));
        var runtimePath = Path.Combine(runtimeDirectory, "Sezika.ReferenceTool.dll");
        if (!Within(runtimeDirectory, runtimeParent) || Path.Exists(runtimeDirectory))
            throw new IOException("GUID-only runtime copy directory must be fresh within the exact runtime directory.");
        var createdDirectory = false; var createdFile = false; FilePin? runtimePin = null;
        try
        {
            check(); Budget.Safe(runtimeDirectory); Directory.CreateDirectory(runtimeDirectory); createdDirectory = true;
            using (var output = new FileStream(runtimePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                createdFile = true; output.Write(modified); output.Flush(true);
            }
            runtimePin = budget.Pin(runtimePath, 4194304);
            if (runtimePin.Bytes != retained.Bytes || runtimePin.Sha256 != retained.Sha256)
                throw new InvalidDataException("Retained and scoped GUID-only PE copy identities differ.");
            var candidate = ProgramPin(runtimePin);
            // The original five runtime files and real successful build receipts
            // remain untouched. Add only this owned copy to the complete inventory
            // so the production verifier can reach the PE/PDB GUID binding gate.
            var mutant = summary with { CompiledAssembly = candidate, RuntimeAssembly = candidate,
                RuntimeFiles = [.. summary.RuntimeFiles, candidate] };
            return Result(index,
                new("real-codeview-guid-only-one-byte-mutation", "PASS", "Only1 byte in the actual16-byte CodeView GUID changes",
                    "offset=" + offset + "; original=" + codeView.Guid + "; mutant=" + modifiedCodeView.Guid + "; original PDB pin retained",
                    runtimePath, retained),
                Reject("real-pe-guid-only-mutant-retains-original-pdb", mutant,
                    "Pinned PE CodeView GUID/stamp does not bind this Portable PDB.", check, cancellation, runtimePath, retained));
        }
        finally
        {
            // Cleanup has its own bound, including when the verification budget
            // was cancelled. Remove only the exact CreateNew copy and empty dir.
            var cleanup = new Budget(10, 536870912, null, CancellationToken.None);
            cleanup.Check(); Budget.Safe(runtimePath); Budget.Safe(runtimeDirectory);
            if (!Within(runtimePath, runtimeDirectory) || !Within(runtimeDirectory, runtimeParent))
                throw new InvalidDataException("GUID-only runtime cleanup escaped its exact owned paths.");
            if (createdFile && File.Exists(runtimePath))
            {
                var expected = runtimePin ?? new FilePin(runtimePath, modified.LongLength, Budget.Sha(modified));
                cleanup.Verify(expected, 4194304); File.Delete(runtimePath);
            }
            if (createdDirectory && Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, recursive: false);
            cleanup.Verify(Local(summary.CompiledAssembly), 4194304);
            cleanup.Verify(Local(summary.RuntimeSymbols), 536870912);
        }
    }

    private sealed class OwnedFixtures(string directory, Budget budget, Action check)
    {
        private readonly List<FilePin> _files = [];
        private readonly List<string> _ownedPaths = [];
        internal bool Created { get; private set; }
        internal FilePin[] Files => _files.ToArray();
        internal long Bytes { get; private set; }
        internal void Create()
        {
            check(); Budget.Safe(directory);
            if (Path.Exists(directory) || !Directory.Exists(Path.GetDirectoryName(directory)))
                throw new IOException("Proof fixtures require a fresh owned directory and existing parent.");
            Directory.CreateDirectory(directory); Created = true;
        }
        internal FilePin Copy(FilePin source, string leaf)
        {
            check(); budget.Verify(source, 536870912); var target = Target(leaf, source.Bytes);
            using (var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                // Ownership is recorded only after CreateNew succeeded, including partial copies.
                _ownedPaths.Add(target);
                var buffer = new byte[1048576]; long copied = 0;
                for (var block = 0; block < 513 && copied < source.Bytes; block++)
                {
                    check(); var count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, source.Bytes - copied));
                    if (count == 0) throw new EndOfStreamException(); output.Write(buffer, 0, count); copied += count;
                }
                if (copied != source.Bytes || input.ReadByte() != -1) throw new InvalidDataException("Owned fixture copy byte/block bound differs.");
                output.Flush(true);
            }
            var pin = budget.Pin(target, 536870912);
            if (pin.Bytes != source.Bytes || pin.Sha256 != source.Sha256.ToLowerInvariant()) throw new InvalidDataException("Owned PDB copy differs from real source pin.");
            budget.Verify(source, 536870912); _files.Add(pin); Bytes = checked(Bytes + pin.Bytes); return pin;
        }
        internal FilePin Write(byte[] bytes, string leaf)
        {
            check(); if (bytes.Length > 4194304) throw new InvalidDataException("Fixture byte bound4MiB.");
            var target = Target(leaf, bytes.Length);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                _ownedPaths.Add(target); output.Write(bytes); output.Flush(true);
            }
            var pin = budget.Pin(target, 4194304); _files.Add(pin); Bytes = checked(Bytes + pin.Bytes); return pin;
        }
        private string Target(string leaf, long bytes)
        {
            check(); if (!Created || _ownedPaths.Count >= 128 || bytes < 0 || bytes > 536870912 - Bytes ||
                Path.GetFileName(leaf) != leaf) throw new InvalidDataException("Owned proof fixture count128/bytes512MiB/leaf bounds differ.");
            var target = Path.GetFullPath(Path.Combine(directory, leaf)); Budget.Safe(target);
            if (!Within(target, directory) || Path.Exists(target)) throw new IOException("Owned fixture target must be fresh within its exact directory.");
            return target;
        }
        internal void Verify()
        {
            for (var index = 0; index < _files.Count && index < 128; index++) { check(); budget.Verify(_files[index], 536870912); }
        }
        internal void Cleanup()
        {
            var cleanupClock = Stopwatch.StartNew();
            // Exact paths are recorded immediately after successful CreateNew, even if hashing failed.
            for (var index = 0; index < _ownedPaths.Count && index < 128; index++)
            {
                if (cleanupClock.Elapsed.TotalSeconds >= 10) throw new TimeoutException("Owned proof fixture cleanup wall bound10s.");
                var path = Path.GetFullPath(_ownedPaths[index]); Budget.Safe(path);
                if (!Within(path, directory)) throw new InvalidDataException("Owned fixture cleanup escapes its directory.");
                if (File.Exists(path)) File.Delete(path);
            }
            if (Created && Directory.Exists(directory)) { Budget.Safe(directory); Directory.Delete(directory, recursive: false); }
        }
    }
}
