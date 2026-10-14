using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika.PortProvenance;

namespace Sezika.IndependentNumericCompare;

internal static class PortReferenceProof
{
    internal const string Policy = "explicit_source_runtime_original134_process_exit_cleanup_receipts";
    internal static readonly string[] OfficialHashes = ["1b23db2f59d3f80bd9af89fb6df3ce15affd90b5ffc09925e732450e694868ff", "55cc0c8cb76f592ab178b4662adcbdf4ad5012bb0b4249c0a96603141f18e9ac", "3dd5a81b1e5e0c5371256f00efec19df52255420318737db1e03430a98cf4264"];
    private static readonly string[] CoreSources = ["Budget.cs", "Contracts.cs", "Exporter.cs", "ModernBertReference.cs", "OriginalWeights.cs", "Program.cs", "RestrictedPickle.cs", "Sezika.ReferenceTool.csproj"];
    internal static PortContract Contract(string path, string expectedHash, Budget budget, bool template = false)
    {
        var contract = Io.Json(path, PortJson.Default.PortContract, budget, out var hash);
        Require.That(Io.SameHash(hash, expectedHash), "External port contract SHA differs."); ValidateContract(contract, budget, template); return contract;
    }
    internal static void ValidateContract(PortContract c, Budget budget, bool template)
    {
        Require.That(c.SchemaVersion == "sezika.independent-oracle-contract.port-derived.v2" && c.Status == "frozen_before_port_measurement" && c.ProofPolicy == Policy, "Port contract schema/status/proof policy differs.");
        Require.That(c.SourceModelId == "jhu-clsp/mmBERT-base" && c.SourceRevision == "c5955035435e2bf121cde7f3c8863ef52ff35d82" && c.CaseId == "independent-smoke-46-v1" && c.SourceWeightsBytes == 1231188142 && c.TokenCount == 46 && c.HiddenSize == 768 && c.LayerCount == 22 && c.Protocol == "decision-v1" && c.LengthPolicy == "strict" && c.IntegerDtype == "I32" && c.IntegerLayout == "little_endian_contiguous" && c.TensorDtype == "F32" && c.TensorLayout == "little_endian_row_major" && c.CheckpointCount == 25 && c.ComparisonCount == 26 && c.Checkpoints.SequenceEqual(Comparison.Checkpoints) && c.MarkerPositions.SequenceEqual([25, 35]) && c.MarkerVectorsName == "marker-vectors.f32", "Fixed port identity/selection differs.");
        Require.That(Io.SameHash(c.SourceWeightsSha256, "8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e") && Io.SameHash(c.ConfigSha256, "47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37") && Io.SameHash(c.TokenizerSha256, "197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356") && Io.SameHash(c.ConvertedEncoderSha256, "6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528") && Io.SameHash(c.TokenIdsSha256, "bab3a41cdb62fc4d5e3353d3d88f6bbdcbd0d3cec9c3db537841f09604d2d80b") && Io.SameHash(c.MarkerPositionsSha256, "6e45294d69eca0660e53bb653811e6aec1bd269b164ee6174d1961d9d5b94396") && Io.SameHash(c.RequestSha256, "75b7055b38c78e7563d1d14de9bc5f4ee9d63a7ff31e637b8c77f3ebf41d9917"), "Fixed port source/input identity changed.");
        var names = new[] { "embedding/raw", "embedding/norm", "layer_hidden", "encoder/final", "marker-vectors.f32" };
        Require.That(c.Tolerances.Count == 5 && names.All(c.Tolerances.ContainsKey), "Port tolerance keys differ.");
        foreach (var name in names) { budget.Check(); var expected = name == "embedding/raw" ? new Tolerance(0, 0) : name == "embedding/norm" ? new Tolerance(1e-5, 1e-5) : new Tolerance(1e-3, 1e-4); Require.That(c.Tolerances[name] == expected, "Port changed frozen numerical tolerance."); }
        Require.That(c.UpstreamVersion == "4.57.1" && c.UpstreamSourceSha256.Select(value => value.ToLowerInvariant()).SequenceEqual(OfficialHashes) && c.ProgramSourceSha256.Length <= 64 && c.ProgramSourceSha256.All(Io.ValidHash) && (template ? c.ProgramSourceSha256.Length == 0 : c.ProgramSourceSha256.Length >= CoreSources.Length), "Port official/program source policy differs.");
    }
    internal static void Verify(PortCapture capture, PortContract contract, Options options, string referenceHash, string actualHash, Budget budget)
    {
        Require.That(options.PortProof is not null, "Explicit independent port proof packet required.");
        var proof = Io.Json(options.PortProof!, PortJson.Default.PortProofRequest, budget, out _);
        Require.That(Same(proof.RunContract, capture.Contract) && PathEqual(proof.RunContract.Path, options.Contract) && Io.SameHash(proof.RunContract.Sha256, options.ContractSha256) && Same(proof.Reference, new(options.Reference, FileSize(options.Reference), referenceHash)) && Same(proof.Actual, capture.ActualCapture) && Io.SameHash(proof.Actual.Sha256, actualHash) && PathEqual(proof.Actual.Path, options.Actual), "Port proof contract/reference/actual pins differ.");
        VerifyPin(proof.RunContract, budget, 1048576); var template = Contract(proof.TemplateContract.Path, proof.TemplateContract.Sha256, budget, true);
        Require.That(IdentityEqual(contract, template), "Run contract changed template identity/tolerances/official source policy.");
        VerifyPin(proof.TemplateContract, budget, 1048576); VerifyPin(proof.Reference, budget, 1048576); VerifyPin(proof.Actual, budget, 1048576);
        Require.That(Same(capture.OriginalWeights, proof.OriginalWeights) && Same(capture.Config, proof.Config) && Same(capture.Tokenizer, proof.Tokenizer) && Same(capture.Executable, proof.Executable) && OptionalSame(capture.ManagedAssembly, proof.ManagedAssembly) && Same(capture.ProcessEvidence, proof.ProcessRegistration) &&
            Same(capture.BuildReceipt, proof.BuildReceipt) && Same(capture.BuildExecution, proof.BuildExecution) &&
            Same(capture.ExportRequest, proof.ExportRequest) && PinsEqual(capture.RuntimeFiles, proof.RuntimeFiles), "Independent port input/runtime/build/request/registration declaration differs.");
        Require.That(proof.OriginalWeights.Bytes == contract.SourceWeightsBytes && Io.SameHash(proof.OriginalWeights.Sha256, contract.SourceWeightsSha256) && Io.SameHash(proof.Config.Sha256, contract.ConfigSha256) && Io.SameHash(proof.Tokenizer.Sha256, contract.TokenizerSha256), "Original port asset identities differ.");
        VerifyPin(proof.OriginalWeights, budget, 2147483648); VerifyPin(proof.Config, budget, 1048576); VerifyPin(proof.Tokenizer, budget, 20971520);
        Require.That(capture.UpstreamSources.Length == 3 && PinsEqual(capture.UpstreamSources, proof.UpstreamSources) && capture.UpstreamSources.Select(pin => pin.Sha256.ToLowerInvariant()).SequenceEqual(OfficialHashes) && PinsEqual(capture.ProgramSources, proof.ProgramSources) && capture.ProgramSources.Select(pin => pin.Sha256.ToLowerInvariant()).SequenceEqual(contract.ProgramSourceSha256.Select(value => value.ToLowerInvariant())) && CoreSources.All(name => capture.ProgramSources.Any(pin => Path.GetFileName(pin.Path) == name)) && capture.ProgramSources.Length is >= 8 and <= 64 && capture.ProgramSources.All(pin => Path.GetExtension(pin.Path) is ".cs" or ".csproj"), "Port source/code pins differ.");
        Require.That(capture.UpstreamSources.Concat(capture.ProgramSources).Select(pin => Path.GetFullPath(pin.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == capture.UpstreamSources.Length + capture.ProgramSources.Length, "Duplicate port source paths.");
        foreach (var pin in capture.UpstreamSources.Concat(capture.ProgramSources)) VerifyPin(pin, budget, 131072);
        VerifyPin(proof.Executable, budget, 536870912); if (proof.ManagedAssembly is not null) VerifyPin(proof.ManagedAssembly, budget, 536870912);
        var build = Io.Parse(ReadPin(proof.BuildReceipt, budget, 4194304), CompiledProgramJson.Default.PortBuildSummary, budget);
        Require.That(PinsEqual(build.ProgramSources.Select(LocalPin).ToArray(), proof.ProgramSources) && Same(LocalPin(build.RuntimeExecutable), proof.Executable) &&
            OptionalSame(build.RuntimeAssembly is null ? null : LocalPin(build.RuntimeAssembly), proof.ManagedAssembly) &&
            Same(LocalPin(build.BuildExecution), proof.BuildExecution) && PinsEqual(build.RuntimeFiles.Select(LocalPin).ToArray(), proof.RuntimeFiles),
            "Compound build source/image/assembly/execution/full-runtime binding differs.");
        _ = CompiledProgramProof.Verify(build, CancellationToken.None, budget.Check);
        var request = Io.Parse(ReadPin(proof.ExportRequest, budget, 4194304), PortJson.Default.PortExportRequest, budget);
        VerifyExportRequest(request, proof, capture, options, budget);
        var exported = Io.Parse(ReadPin(proof.ExportReceipt, budget), PortJson.Default.PortExportProof, budget);
        Require.That(exported.Status == "CAPTURE_COMPLETE_COMPARISON_NOT_RUN" && PathEqual(exported.OutputDirectory, Path.GetDirectoryName(Path.GetFullPath(options.Reference))!) && Io.SameHash(exported.CaptureSha256 ?? "", referenceHash) && exported.Checkpoints == 25 && exported.OriginalTensors == 134 && exported.Error is null && !exported.UpstreamExecuted && !exported.RepresentsParity && exported.ElapsedSeconds >= capture.ElapsedSeconds && exported.ElapsedSeconds <= 1800 && exported.ObservedPeakBytes >= capture.ObservedPeakBytes, "Completed independent exporter receipt differs.");
        var executor = Io.Parse(ReadPin(proof.ExecutorReceipt, budget), ExecutorJson.Default.ExecutorProof, budget);
        VerifyProcess(capture, executor, proof, budget);
        ValidateTensorProofs(capture.OriginalTensors, budget);
        VerifyOriginalTensorBytes(proof.OriginalWeights.Path, capture.OriginalTensors, budget);
        foreach (var pin in new[] { proof.BuildReceipt, proof.BuildExecution, proof.ExportRequest, proof.ExecutorReceipt, proof.ProcessRegistration })
            VerifyPin(pin, budget, 20971520);
        foreach (var pin in proof.RuntimeFiles) { budget.Check(); VerifyPin(pin, budget, 536870912); }
        CompiledProgramProof.VerifyRuntimeDirectoryFiles(build.RuntimeDirectory, build.RuntimeFiles, budget.Check);
    }
    internal static void Shape(PortCapture c, PortContract contract)
    {
        Require.That(c.SchemaVersion == "sezika.independent-reference.v2" && c.Status == "complete" && c.Kind == "independently_ported_original_weights_capture" && !c.FixtureOnly && !c.UpstreamExecuted && c.OriginalWeightsVerified && !c.RepresentsParity && !c.RepresentsQuality && !c.RepresentsCalibration && c.TerminalError is null, "Port reference state/source flags are not a completed independent C# capture.");
        Require.That(c.SourceModelId == contract.SourceModelId && c.SourceRevision == contract.SourceRevision && c.CaseId == contract.CaseId && c.Protocol == contract.Protocol && c.LengthPolicy == contract.LengthPolicy && c.ModelId == "sezika/mmbert-base-independent-s4" && c.Implementation == "transformers-4.57.1-modernbert-eager-csharp-fp32-vector-dot-fp64-norm-statistics-erf-approximation-v1" && c.TokenIds.Length == 46 && c.MarkerPositions.SequenceEqual([25, 35]) && c.Tensors.Length == 25 && c.OriginalTensors.Length == 134 && c.ElapsedSeconds is > 0 and <= 1800 && c.ObservedPeakBytes is > 0 and <= 6442450944, "Port reference fixed identity/denominator differs.");
        var e = c.Execution;
        Require.That(e.Backend == "csharp_cpu_fp32_port" && e.VectorLanes is 4 or 8 or 16 && e.DotArithmetic == "FP32 products and vector-lane reductions" && e.NormStatistics == "FP64 population statistics then FP32 output" && e.Gelu == "erf-form GELU with A&S7.1.26 approximation" && !string.IsNullOrWhiteSpace(e.Runtime) && !string.IsNullOrWhiteSpace(e.OperatingSystem) && !string.IsNullOrWhiteSpace(e.Architecture) && e.ActualArguments.Length is >= 1 and <= 256 && e.ActualArguments.All(value => value is not null && value.Length <= 32768) && e.ActualArguments.Sum(value => (long)value.Length) <= 32768 && e.NumericalThreads == 1, "Port numerical/runtime provenance differs.");
    }
    private static void VerifyProcess(PortCapture capture, ExecutorProof e, PortProofRequest proof, Budget budget)
    {
        var p = capture.Process;
        Require.That(e.Status == "PASS" && e.ExecutionStatus == "SUCCEEDED" && e.CleanupStatus == "PASS" && e.ExitCode == 0 && e.RootPid == p.Pid && e.InputComplete && e.OutputComplete && !e.OutputLimitExceeded && e.RemainingProcesses == 0 && e.Errors.Length == 0 && PathEqual(e.Executable, proof.Executable.Path) && PathEqual(e.OutputDirectory, Path.GetDirectoryName(Path.GetFullPath(proof.ProcessRegistration.Path))!) && e.OwnershipId == p.ScopeId && Guid.TryParseExact(p.ScopeId, "N", out _) && p.Pid > 0 && p.ParentPid > 0 && p.ParentChain.Length is >= 1 and <= 64 && p.ParentChain[^1] == p.ParentPid && p.ParentChain.All(pid => pid > 0 && pid != p.Pid) && p.ParentChain.Distinct().Count() == p.ParentChain.Length && !string.IsNullOrWhiteSpace(p.CommandLine) && !string.IsNullOrWhiteSpace(p.ActualCommandLine) && e.ElapsedSeconds is > 0 and <= 1830 && capture.StartedUtc >= e.StartedUtc && capture.StartedUtc <= e.StartedUtc.AddSeconds(e.ElapsedSeconds), "Port executor completion/cleanup/ownership proof differs.");
        Require.That(e.Processes.Length is >= 1 and <= 256 && e.Processes.Select(item => item.Pid).Distinct().Count() == e.Processes.Length && e.Processes.Count(item => item.Pid == p.Pid) == 1, "Final port process inventory duplicate/count differs.");
        var observed = e.Processes.SingleOrDefault(item => item.Pid == p.Pid) ?? throw new InvalidDataException("Completed port PID missing from final executor inventory.");
        Require.That(ProcessEqual(p, observed) && observed.PendingObservationError is null && observed.State.StartsWith("EXITED", StringComparison.Ordinal), "Final port PID/birth/argv/parent/exit proof differs.");
        var registrations = Io.Parse(ReadPin(proof.ProcessRegistration, budget), ExecutorJson.Default.ExecutorProcessArray, budget);
        Require.That(registrations.Length is >= 1 and <= 256 && registrations.Select(item => item.Pid).Distinct().Count() == registrations.Length && registrations.Count(item => item.Pid == p.Pid) == 1, "Initial port process inventory duplicate/count differs.");
        var initial = registrations.Single(item => item.Pid == p.Pid);
        Require.That(ProcessEqual(p, initial), "Port initial registration provenance differs.");
        if (e.Platform == "windows") Require.That(p.Started is not null && p.StartTicks is null && p.BootId is null && p.ExpectedExecCommandLine is null && p.ActualCommandLine == p.CommandLine, "Windows port birth/exact actual argv proof differs.");
        else if (e.Platform == "linux") Require.That(p.StartTicks is > 0 && Guid.TryParse(p.BootId, out _) && observed.AfterExecCommandLine is not null && observed.ExpectedExecCommandLine == observed.AfterExecCommandLine && p.ActualCommandLine == observed.AfterExecCommandLine, "Linux port birth/boot/exec actual argv proof differs.");
        else throw new InvalidDataException("Unsupported port process platform.");
        var expectedArguments = proof.ManagedAssembly is null ? new[] { proof.Executable.Path }.Concat(e.ArgumentList).ToArray() :
            e.ArgumentList.Length > 0 && PathEqual(e.ArgumentList[0], proof.ManagedAssembly.Path) ? e.ArgumentList : new[] { proof.ManagedAssembly.Path }.Concat(e.ArgumentList).ToArray();
        Require.That(capture.Execution.ActualArguments.SequenceEqual(expectedArguments), "Actual port arguments differ from independently completed command.");
        Require.That(PathEqual(RequestArgument(e.ArgumentList, budget), proof.ExportRequest.Path) &&
            PathEqual(RequestArgument(capture.Execution.ActualArguments, budget), proof.ExportRequest.Path), "Actual completed reference stage did not consume the pinned --request file.");
    }
    private static bool ProcessEqual(PortProcess a, ExecutorProcess b) => a.Pid == b.Pid && a.ParentPid == b.ParentPid && a.ParentChain.SequenceEqual(b.ParentChain) && a.Started == b.Started && a.StartTicks == b.StartTicks && a.BootId == b.BootId && a.CommandLine == b.CommandLine && a.ScopeId == b.ScopeId && a.Ownership == b.Ownership && a.ExpectedExecCommandLine == b.ExpectedExecCommandLine;
    private static ProofPin LocalPin(ProgramFilePin pin) => new(pin.Path, pin.Bytes, pin.Sha256);
    internal static string RequestArgument(string[] arguments, Budget budget)
    {
        Require.That(arguments.Length is >= 2 and <= 256 && arguments.All(value => value is not null && value.Length <= 32768) && arguments.Sum(value => (long)value.Length) <= 65536, "Reference stage argv bounds differ.");
        string? path = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            budget.Check(); if (arguments[index] != "--request") continue;
            Require.That(path is null && ++index < arguments.Length && Path.IsPathFullyQualified(arguments[index]), "Exactly one literal --request absolute-path pair required.");
            path = arguments[index];
        }
        Require.That(path is not null, "Reference stage argv has no literal --request pair."); return path!;
    }
    private static void VerifyExportRequest(PortExportRequest request, PortProofRequest proof, PortCapture capture, Options options, Budget budget)
    {
        budget.Check();
        Require.That(Same(request.OriginalWeights, proof.OriginalWeights) && Same(request.Config, proof.Config) && Same(request.Tokenizer, proof.Tokenizer) &&
            Same(request.ActualCapture, proof.Actual) && Same(request.Contract, proof.RunContract) && PinsEqual(request.UpstreamSources, proof.UpstreamSources) &&
            PinsEqual(request.ProgramSources, proof.ProgramSources) && Same(request.RuntimeExecutable, proof.Executable) &&
            OptionalSame(request.RuntimeAssembly, proof.ManagedAssembly) && Same(request.BuildReceipt, proof.BuildReceipt) &&
            PathEqual(request.OutputDirectory, Path.GetDirectoryName(Path.GetFullPath(options.Reference))!) &&
            PathEqual(request.ProcessEvidenceDirectory, Path.GetDirectoryName(Path.GetFullPath(proof.ProcessRegistration.Path))!) &&
            request.TimeoutSeconds is >= 1 and <= 1800 && capture.ElapsedSeconds <= request.TimeoutSeconds &&
            request.MaximumResidentBytes is >= 536870912 and <= 6442450944 && capture.ObservedPeakBytes <= request.MaximumResidentBytes &&
            request.MaximumOutputBytes is >= 1048576 and <= 20971520, "Pinned actual export request declaration differs from completed reference/build/assets.");
        if (request.CancelFile is not null) Io.CheckPath(request.CancelFile, budget);
    }
    internal static void ValidateTensorProofs(OriginalTensorProof[] proofs, Budget budget)
    {
        var expected = new Dictionary<string, long[]>(StringComparer.Ordinal)
        { ["model.embeddings.tok_embeddings.weight"] = [256000, 768], ["model.embeddings.norm.weight"] = [768], ["model.final_norm.weight"] = [768] };
        for (var layer = 0; layer < 22; layer++)
        {
            budget.Check(); var prefix = "model.layers." + layer + ".";
            if (layer != 0) expected.Add(prefix + "attn_norm.weight", [768]);
            expected.Add(prefix + "attn.Wqkv.weight", [2304, 768]); expected.Add(prefix + "attn.Wo.weight", [768, 768]);
            expected.Add(prefix + "mlp_norm.weight", [768]); expected.Add(prefix + "mlp.Wi.weight", [2304, 768]); expected.Add(prefix + "mlp.Wo.weight", [768, 1152]);
        }
        Require.That(proofs.Length == 134 && proofs.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() == 134, "Original tensor proof denominator/duplicates differ.");
        foreach (var proof in proofs)
        {
            budget.Check(); Require.That(expected.TryGetValue(proof.Name, out var shape) && proof.Shape.SequenceEqual(shape!) && proof.Bytes == checked(proof.Shape.Aggregate(4L, (a, b) => a * b)) && proof.StorageOffset >= 0 && proof.StorageKey.Length is >= 1 and <= 12 && proof.StorageKey.All(char.IsAsciiDigit) && Io.ValidHash(proof.Sha256), "Original tensor proof name/shape/storage/hash differs.");
        }
    }
    private static void VerifyOriginalTensorBytes(string path, OriginalTensorProof[] proofs, Budget budget)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        Require.That(archive.Entries.Count <= 4096 && archive.Entries.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() == archive.Entries.Count, "Original port ZIP entries differ.");
        var pickleEntry = archive.GetEntry("pytorch_model/data.pkl") ?? throw new InvalidDataException("Original proof pickle missing.");
        Require.That(pickleEntry.Length is > 0 and <= 4194304, "Original proof pickle byte bound differs.");
        var pickle = new byte[(int)pickleEntry.Length]; using (var input = pickleEntry.Open()) input.ReadExactly(pickle);
        var descriptors = new PortPickleReader(pickle, budget).Read();
        Require.That(descriptors.Keys.Count(name => name.StartsWith("model.", StringComparison.Ordinal)) == 134, "Original proof pickle encoder count differs.");
        var buffer = new byte[1048576]; long total = 0;
        foreach (var proof in proofs)
        {
            budget.Check(); Require.That(descriptors.TryGetValue(proof.Name, out var descriptor) && descriptor.StorageKey == proof.StorageKey && descriptor.Offset == proof.StorageOffset && descriptor.Shape.SequenceEqual(proof.Shape), "Declared tensor proof differs from original static descriptor.");
            var entry = archive.GetEntry("pytorch_model/data/" + proof.StorageKey) ?? throw new InvalidDataException("Original tensor proof storage missing.");
            var skip = checked(proof.StorageOffset * 4); Require.That(skip <= entry.Length - proof.Bytes, "Original proof storage extent differs.");
            using var input = entry.Open();
            for (var block = 0; block < 2049 && skip > 0; block++) { budget.Check(); var count = (int)Math.Min(skip, buffer.Length); input.ReadExactly(buffer.AsSpan(0, count)); skip -= count; }
            Require.That(skip == 0, "Original proof skip bound exceeded."); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var remaining = proof.Bytes;
            for (var block = 0; block < 2049 && remaining > 0; block++) { budget.Check(); var count = (int)Math.Min(remaining, buffer.Length); input.ReadExactly(buffer.AsSpan(0, count)); hash.AppendData(buffer, 0, count); remaining -= count; }
            total += proof.Bytes; Require.That(remaining == 0 && total <= 2147483648 && Io.SameHash(Convert.ToHexString(hash.GetHashAndReset()), proof.Sha256), "Original raw tensor byte/hash proof differs.");
        }
    }
    internal static byte[] ReadPin(ProofPin pin, Budget budget, int maximum = 1048576) { VerifyPin(pin, budget, maximum); var bytes = Io.Read(pin.Path, maximum, budget); Require.That(bytes.LongLength == pin.Bytes && Io.SameHash(Io.Hash(bytes), pin.Sha256), "Proof pin changed while parsing."); return bytes; }
    internal static void VerifyPin(ProofPin pin, Budget budget, long maximum)
    {
        Require.That(Path.IsPathFullyQualified(pin.Path) && !pin.Path.StartsWith("\\\\", StringComparison.Ordinal) && pin.Bytes is > 0 && pin.Bytes <= maximum && Io.ValidHash(pin.Sha256), "Port proof pin bounds differ.");
        Io.CheckPath(pin.Path, budget); using var file = new FileStream(pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require.That(file.Length == pin.Bytes, "Port proof pin length differs."); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1048576]; long read = 0;
        for (var block = 0; block < 2049 && read < file.Length; block++) { budget.Check(); var count = (int)Math.Min(buffer.Length, file.Length - read); file.ReadExactly(buffer.AsSpan(0, count)); hash.AppendData(buffer, 0, count); read += count; }
        Require.That(read == file.Length && Io.SameHash(Convert.ToHexString(hash.GetHashAndReset()), pin.Sha256), "Port proof pin bytes/hash differs.");
    }
    private static long FileSize(string path) => new FileInfo(path).Length;
    internal static bool Same(ProofPin a, ProofPin b) => PathEqual(a.Path, b.Path) && a.Bytes == b.Bytes && Io.SameHash(a.Sha256, b.Sha256);
    private static bool OptionalSame(ProofPin? a, ProofPin? b) => a is null && b is null || a is not null && b is not null && Same(a, b);
    private static bool PinsEqual(ProofPin[] a, ProofPin[] b) => a.Length == b.Length && a.Zip(b).All(pair => Same(pair.First, pair.Second));
    private static bool PathEqual(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static bool IdentityEqual(PortContract a, PortContract b) => a.SourceModelId == b.SourceModelId && a.SourceRevision == b.SourceRevision && a.SourceWeightsBytes == b.SourceWeightsBytes && Io.SameHash(a.SourceWeightsSha256, b.SourceWeightsSha256) && Io.SameHash(a.ConfigSha256, b.ConfigSha256) && Io.SameHash(a.TokenizerSha256, b.TokenizerSha256) && Io.SameHash(a.ConvertedEncoderSha256, b.ConvertedEncoderSha256) && a.CaseId == b.CaseId && Io.SameHash(a.RequestSha256, b.RequestSha256) && Io.SameHash(a.TokenIdsSha256, b.TokenIdsSha256) && Io.SameHash(a.MarkerPositionsSha256, b.MarkerPositionsSha256) && a.Protocol == b.Protocol && a.LengthPolicy == b.LengthPolicy && a.MarkerPositions.SequenceEqual(b.MarkerPositions) && a.Checkpoints.SequenceEqual(b.Checkpoints) && a.Tolerances.Count == b.Tolerances.Count && a.Tolerances.All(pair => b.Tolerances.TryGetValue(pair.Key, out var other) && pair.Value == other);
}
