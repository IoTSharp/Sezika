using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sezika.ReferenceTool;

internal static class Exporter
{
    private const string ConfigSha = "47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37";
    private const string TokenizerSha = "197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356";
    private const string Revision = "c5955035435e2bf121cde7f3c8863ef52ff35d82";
    private const string TokenIdsSha = "bab3a41cdb62fc4d5e3353d3d88f6bbdcbd0d3cec9c3db537841f09604d2d80b";
    private const string MarkerSha = "6e45294d69eca0660e53bb653811e6aec1bd269b164ee6174d1961d9d5b94396";
    private const string RequestSha = "75b7055b38c78e7563d1d14de9bc5f4ee9d63a7ff31e637b8c77f3ebf41d9917";
    private static readonly string[] UpstreamHashes = ["1b23db2f59d3f80bd9af89fb6df3ce15affd90b5ffc09925e732450e694868ff", "55cc0c8cb76f592ab178b4662adcbdf4ad5012bb0b4249c0a96603141f18e9ac", "3dd5a81b1e5e0c5371256f00efec19df52255420318737db1e03430a98cf4264"];
    private static readonly string[] SourceNames = ["Budget.cs", "Contracts.cs", "Exporter.cs", "ModernBertReference.cs", "OriginalWeights.cs", "Program.cs", "RestrictedPickle.cs", "Sezika.ReferenceTool.csproj"];

    internal static ExportReceipt Run(ExportRequest request, CancellationToken cancellation)
    {
        if (request.TimeoutSeconds is < 1 or > 1800 || request.MaximumResidentBytes is < 536870912 or > 6442450944 || request.MaximumOutputBytes is < 1048576 or > 20971520) throw new InvalidDataException("Reference request time/resident/output bounds differ.");
        if (request.CancelFile is not null) Budget.Safe(request.CancelFile);
        var budget = new Budget(request.TimeoutSeconds, request.MaximumResidentBytes, request.CancelFile, cancellation);
        var exportRequest = VerifyRequest(request, budget);
        var contract = budget.Read(request.Contract, ReferenceJson.Default.ReferenceContract);
        ValidateContract(contract, request);
        var sourcePins = VerifySources(request.UpstreamSources, request.ProgramSources, budget);
        var build = budget.Verify(request.BuildReceipt, 4194304);
        var provenance = Laya.PortProof.Runtime(request.ProgramSources, request.RuntimeExecutable, request.RuntimeAssembly,
            build, request.ProcessEvidenceDirectory, budget);
        var executable = budget.Verify(request.RuntimeExecutable, 536870912);
        var assembly = request.RuntimeAssembly is null ? null : budget.Verify(request.RuntimeAssembly, 536870912);
        var registered = provenance.Process;
        var process = new ProcessProof(registered.Pid, registered.ParentPid, registered.ParentChain, registered.Started,
            registered.StartTicks, registered.BootId, registered.CommandLine, registered.ScopeId, registered.Ownership,
            registered.ExpectedExecCommandLine, registered.ActualCommandLine);
        var original = budget.Verify(request.OriginalWeights); var config = budget.Verify(request.Config, 1048576);
        var tokenizer = budget.Verify(request.Tokenizer, 20971520); var actualPin = budget.Verify(request.ActualCapture, 1048576);
        var actual = budget.Read(actualPin, ReferenceJson.Default.ActualInput); ValidateActual(actual, contract);
        var actualRoot = Path.GetDirectoryName(actualPin.Path)!;
        var tokenBytes = Integers(actual.TokenIds); var markerBytes = Integers(actual.MarkerPositions);
        var tokenPin = Input(actualRoot, actual.TokenIdsArtifact, budget); var markerPin = Input(actualRoot, actual.MarkerPositionsArtifact, budget);
        if (!budget.Bytes(tokenPin, 184).AsSpan().SequenceEqual(tokenBytes) || !budget.Bytes(markerPin, 8).AsSpan().SequenceEqual(markerBytes)) throw new InvalidDataException("Actual integer artifact differs from declared realized input.");
        var requestPin = Input(actualRoot, actual.Request, budget); var requestBytes = budget.Bytes(requestPin, 1048576); budget.ValidateJson(requestBytes);
        if (Budget.Sha(tokenBytes) != contract.TokenIdsSha256.ToLowerInvariant() || Budget.Sha(markerBytes) != contract.MarkerPositionsSha256.ToLowerInvariant() || Budget.Sha(requestBytes) != contract.RequestSha256.ToLowerInvariant()) throw new InvalidDataException("Realized request/token/marker SHA differs from premeasurement contract.");
        ValidateConfig(budget.Bytes(config, 1048576), budget);
        var processEvidence = provenance.Evidence;
        var output = Path.GetFullPath(request.OutputDirectory); Budget.Safe(output);
        if (Path.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output))) throw new IOException("Reference output must be a new directory with existing parent.");
        foreach (var input in new[] { original.Path, config.Path, tokenizer.Path, actualPin.Path, request.Contract.Path, exportRequest.Path, build.Path }
            .Concat(provenance.RuntimeFiles.Select(pin => pin.Path)).Concat(sourcePins.Select(pin => pin.Path)))
            if (Within(output, Path.GetDirectoryName(input)!)) throw new InvalidDataException("Reference output intersects a source/input tree.");
        Directory.CreateDirectory(output); var owner = Guid.NewGuid().ToString("N");
        Budget.WriteNew(Path.Combine(output, "ownership.txt"), Encoding.ASCII.GetBytes(owner));
        long outputBytes = owner.Length; var tensorOutputs = new List<TensorOutput>(); var partialNumber = 0;
        TensorOutput? markerOutput = null; var started = DateTimeOffset.UtcNow; WeightTensorProof[] proofs = []; byte[]? publishedMetadata = null;
        var tokenArtifact = WriteArtifact("token-ids.i32", tokenBytes); var markerArtifact = WriteArtifact("marker-positions.i32", markerBytes); var rawRequest = WriteArtifact("request.json", requestBytes);
        Capture Metadata(string status, string? error, bool verified) => new("sezika.independent-reference.v2", status,
            "independently_ported_original_weights_capture", false, false, "transformers-4.57.1-modernbert-eager-csharp-fp32-vector-dot-fp64-norm-statistics-erf-approximation-v1",
            contract.SourceModelId, contract.SourceRevision, contract.CaseId, contract.Protocol, contract.LengthPolicy, actual.ModelId,
            original, config, tokenizer, actualPin, request.Contract, request.UpstreamSources, request.ProgramSources,
            executable, assembly, build, provenance.BuildExecution, provenance.RuntimeFiles, exportRequest,
            processEvidence, process, tokenArtifact, markerArtifact, rawRequest, actual.TokenIds,
            actual.MarkerPositions, tensorOutputs.ToArray(), markerOutput, proofs,
            new("csharp_cpu_fp32_port", System.Numerics.Vector<float>.Count, "FP32 products and vector-lane reductions", "FP64 population statistics then FP32 output",
                "erf-form GELU with A&S7.1.26 approximation", Environment.Version.ToString(), RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.GetCommandLineArgs(), 1),
            started, budget.Elapsed, budget.PeakBytes, error, verified);
        void Write(string name, byte[] bytes)
        {
            budget.Check(); Budget.Safe(output);
            if (File.ReadAllText(Path.Combine(output, "ownership.txt"), Encoding.ASCII) != owner || outputBytes > request.MaximumOutputBytes - bytes.Length) throw new InvalidDataException("Reference output ownership/byte budget differs.");
            Budget.WriteNew(Path.Combine(output, name), bytes); outputBytes += bytes.Length;
        }
        InputArtifact WriteArtifact(string name, byte[] bytes) { Write(name, bytes); return new() { Path = name, Bytes = bytes.Length, Sha256 = Budget.Sha(bytes) }; }
        void Partial(string? error = null)
        {
            if (partialNumber >= 28) throw new InvalidDataException("Reference partial count bound28.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Metadata("partial", error, false), ReferenceJson.Default.Capture);
            if (bytes.Length > 1048576) throw new InvalidDataException("Reference metadata byte bound1MiB.");
            Write("capture.partial-" + (partialNumber++).ToString("D3") + ".json", bytes);
        }
        TensorOutput Tensor(string name, float[] values, int[] shape, string file)
        {
            ModernBertReference.Finite(values); if (values.Length != shape.Aggregate(1, (a, b) => checked(a * b))) throw new InvalidDataException("Reference output shape differs.");
            var bytes = new byte[checked(values.Length * 4)]; for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), BitConverter.SingleToInt32Bits(values[i]));
            Write(file, bytes); return new(name, file, "F32", shape, "little_endian_row_major", bytes.Length, Budget.Sha(bytes));
        }
        try
        {
            Partial(); using var weights = new OriginalWeights(original, budget);
            var result = ModernBertReference.Forward(weights, actual.TokenIds, (name, values) =>
            {
                var expected = tensorOutputs.Count switch { 0 => "embedding/raw", 1 => "embedding/norm", 24 => "encoder/final", var number => "layer/" + (number - 2) + "/hidden" };
                if (tensorOutputs.Count >= 25 || name != expected) throw new InvalidDataException("Reference checkpoint order/count differs.");
                tensorOutputs.Add(Tensor(name, values, [46, 768], "tensor-" + tensorOutputs.Count.ToString("D3") + ".f32"));
                proofs = weights.Proofs; Partial(); Console.WriteLine($"reference-checkpoint {tensorOutputs.Count}/25 {name}");
            }, budget);
            proofs = weights.Proofs; if (tensorOutputs.Count != 25 || proofs.Length != 134) throw new InvalidDataException("Reference tensor/checkpoint denominator incomplete.");
            var markers = new float[1536]; for (var row = 0; row < 2; row++) result.AsSpan(actual.MarkerPositions[row] * 768, 768).CopyTo(markers.AsSpan(row * 768, 768));
            markerOutput = Tensor("marker-vectors.f32", markers, [2, 768], "marker-vectors.f32");
            foreach (var pin in new[] { original, config, tokenizer, actualPin, request.Contract, tokenPin, markerPin, requestPin, executable }.Concat(sourcePins)) budget.Verify(pin);
            if (assembly is not null) budget.Verify(assembly);
            budget.Verify(build, 4194304); budget.Verify(provenance.BuildExecution, 20971520);
            budget.Verify(exportRequest, 4194304); Laya.PortProof.VerifyRuntimeAfter(provenance.RuntimeFiles, budget);
            publishedMetadata = JsonSerializer.SerializeToUtf8Bytes(Metadata("complete", null, true), ReferenceJson.Default.Capture);
            if (publishedMetadata.Length > 1048576) throw new InvalidDataException("Reference metadata byte bound1MiB.");
            Write("capture.json", publishedMetadata);
            var completed = budget.Pin(Path.Combine(output, "capture.json"), 1048576);
            return new("CAPTURE_COMPLETE_COMPARISON_NOT_RUN", output, completed.Sha256, tensorOutputs.Count, proofs.Length, budget.Elapsed, budget.PeakBytes, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or TimeoutException or OperationCanceledException or OverflowException)
        {
            // Preserve completed raw outputs. Failure/cancellation cannot publish complete metadata.
            var completePath = Path.Combine(output, "capture.json");
            if (publishedMetadata is not null && File.Exists(completePath))
            {
                try
                {
                    Budget.Safe(completePath);
                    using (var input = new FileStream(completePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (input.Length != publishedMetadata.Length || input.Length > 1048576 || File.ReadAllText(Path.Combine(output, "ownership.txt"), Encoding.ASCII) != owner ||
                            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input)) != Budget.Sha(publishedMetadata)) throw new InvalidDataException("Failed publication identity changed; retained.");
                    }
                    File.Move(completePath, Path.Combine(output, "capture.failed-after-publication.json"), false);
                }
                catch (Exception publicationError) when (publicationError is IOException or UnauthorizedAccessException or InvalidDataException) { Console.Error.WriteLine("Failed complete marker retained for review: " + publicationError.GetType().Name); }
            }
            try { Partial(error.GetType().Name + ": " + error.Message); } catch (Exception terminal) when (terminal is IOException or InvalidDataException or TimeoutException or OperationCanceledException) { Console.Error.WriteLine("Partial metadata could not be completed: " + terminal.GetType().Name); }
            return new("FAIL_REFERENCE_CAPTURE_INCOMPLETE", output, null, tensorOutputs.Count, proofs.Length, budget.Elapsed, budget.PeakBytes, error.GetType().Name + ": " + error.Message);
        }
    }
    internal static FilePin VerifyRequest(ExportRequest request, Budget budget)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Length is < 3 or > 256 || arguments.Any(value => value.Length > 32768) ||
            arguments.Sum(value => (long)value.Length) > 65536) throw new InvalidDataException("Actual reference argv inventory bound differs.");
        string? path = null;
        for (var index = 1; index < arguments.Length; index++)
        {
            budget.Check();
            if (arguments[index] != "--request") continue;
            if (path is not null || ++index >= arguments.Length || !Path.IsPathFullyQualified(arguments[index]))
                throw new InvalidDataException("Exactly one literal --request absolute-path pair required.");
            path = arguments[index];
        }
        if (path is null) throw new InvalidDataException("Actual reference argv has no literal --request pair.");
        var pin = budget.Pin(path, 4194304); var reloaded = budget.Read(pin, ReferenceJson.Default.ExportRequest, 4194304);
        var expected = JsonSerializer.SerializeToUtf8Bytes(request, ReferenceJson.Default.ExportRequest);
        var actual = JsonSerializer.SerializeToUtf8Bytes(reloaded, ReferenceJson.Default.ExportRequest);
        if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidDataException("Actual request-file DTO differs from exporter request.");
        budget.Verify(pin, 4194304); return pin;
    }
    private static void ValidateContract(ReferenceContract contract, ExportRequest request)
    {
        if (contract.SchemaVersion != "sezika.independent-oracle-contract.port-derived.v2" || contract.Status != "frozen_before_port_measurement" ||
            contract.SourceModelId != "jhu-clsp/mmBERT-base" || contract.SourceRevision != Revision || contract.CaseId != "independent-smoke-46-v1" ||
            contract.Protocol != "decision-v1" || contract.LengthPolicy != "strict" || contract.TokenCount != 46 || !contract.MarkerPositions.SequenceEqual([25, 35]) ||
            contract.TokenIdsSha256.ToLowerInvariant() != TokenIdsSha || contract.MarkerPositionsSha256.ToLowerInvariant() != MarkerSha || contract.RequestSha256.ToLowerInvariant() != RequestSha ||
            contract.WeightsSha256.ToLowerInvariant() != OriginalWeights.OriginalSha || contract.WeightsBytes != OriginalWeights.OriginalBytes ||
            contract.ConfigSha256.ToLowerInvariant() != ConfigSha || contract.TokenizerSha256.ToLowerInvariant() != TokenizerSha || contract.UpstreamVersion != "4.57.1" ||
            request.Config.Sha256.ToLowerInvariant() != ConfigSha || request.Tokenizer.Sha256.ToLowerInvariant() != TokenizerSha ||
            !contract.UpstreamSourceSha256.Select(value => value.ToLowerInvariant()).SequenceEqual(UpstreamHashes) ||
            !contract.ProgramSourceSha256.Select(value => value.ToLowerInvariant()).SequenceEqual(request.ProgramSources.Select(pin => pin.Sha256.ToLowerInvariant()))) throw new InvalidDataException("Frozen port-derived v2 source/model/input contract differs.");
    }
    private static FilePin[] VerifySources(FilePin[] upstream, FilePin[] program, Budget budget)
    {
        if (upstream.Length != 3 || program.Length < SourceNames.Length || program.Length > 64 ||
            SourceNames.Any(name => !program.Any(pin => Path.GetFileName(pin.Path) == name)) ||
            program.Any(pin => Path.GetExtension(pin.Path) is not (".cs" or ".csproj"))) throw new InvalidDataException("Explicit reference/upstream source scope differs.");
        if (!upstream.Select(pin => pin.Sha256.ToLowerInvariant()).SequenceEqual(UpstreamHashes) || upstream.Concat(program).Select(pin => pin.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != upstream.Length + program.Length) throw new InvalidDataException("Pinned upstream source hashes/unique paths differ.");
        return upstream.Concat(program).Select(pin => budget.Verify(pin, 131072)).ToArray();
    }
    private static void ValidateActual(ActualInput input, ReferenceContract contract)
    {
        if (input.SchemaVersion != 1 || input.Status != "cpu_capture_complete" || input.Kind != "csharp_cpu_capture" || input.FixtureOnly || string.IsNullOrWhiteSpace(input.ModelId) || input.ModelId.Length > 256 || input.SourceModelId != contract.SourceModelId || input.SourceRevision != contract.SourceRevision || input.CaseId != contract.CaseId || input.Protocol != contract.Protocol || input.LengthPolicy != contract.LengthPolicy || input.IntegerDtype != "I32" || input.IntegerLayout != "little_endian_contiguous" || input.ExpectedCheckpoints != 267 || input.Tensors.Length != 267 || input.TokenIds.Length != 46 || !input.MarkerPositions.SequenceEqual(contract.MarkerPositions) || input.TokenIds.Any(value => value is < 0 or >= 256000)) throw new InvalidDataException("Complete realized C# integer input differs; actual tensor values are never used as reference.");
    }
    private static void ValidateConfig(byte[] bytes, Budget budget)
    {
        budget.ValidateJson(bytes); using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        if (root.GetProperty("hidden_size").GetInt32() != 768 || root.GetProperty("intermediate_size").GetInt32() != 1152 || root.GetProperty("num_hidden_layers").GetInt32() != 22 || root.GetProperty("num_attention_heads").GetInt32() != 12 || root.GetProperty("vocab_size").GetInt32() != 256000 || root.GetProperty("global_attn_every_n_layers").GetInt32() != 3 || root.GetProperty("local_attention").GetInt32() != 128 || root.GetProperty("global_rope_theta").GetSingle() != 160000 || root.GetProperty("local_rope_theta").GetSingle() != 160000 || root.GetProperty("norm_eps").GetSingle() != 1e-5f || root.GetProperty("hidden_activation").GetString() != "gelu" || root.GetProperty("attention_bias").GetBoolean() || root.GetProperty("mlp_bias").GetBoolean() || root.GetProperty("norm_bias").GetBoolean() || root.GetProperty("attention_dropout").GetSingle() != 0 || root.GetProperty("mlp_dropout").GetSingle() != 0 || root.GetProperty("embedding_dropout").GetSingle() != 0) throw new InvalidDataException("Audited original ModernBERT config differs.");
    }
    private static FilePin Input(string root, InputArtifact artifact, Budget budget)
    {
        if (string.IsNullOrWhiteSpace(artifact.Path) || Path.IsPathFullyQualified(artifact.Path)) throw new InvalidDataException("Realized artifact requires relative path.");
        var path = Path.GetFullPath(artifact.Path, root); if (!Within(path, root)) throw new InvalidDataException("Realized artifact escapes its actual capture root.");
        return budget.Verify(new(path, artifact.Bytes, artifact.Sha256), 1048576);
    }
    private static byte[] Integers(int[] values) { var bytes = new byte[values.Length * 4]; for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), values[i]); return bytes; }
    private static bool Within(string path, string root) { var relative = Path.GetRelativePath(root, path); return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative); }
}
