using System.Text.Json;

namespace Sezika.ReferenceTool.Laya;

internal static class LayaReferenceExport
{
    internal const string Schema = "sezika.laya-oracle.port-derived.v2";
    internal static int Run(string requestPath, string receiptPath, CancellationToken cancellation)
    {
        var admission = new Budget(30, 536870912, null, cancellation); var requestPin = admission.Pin(requestPath, 4194304);
        var request = admission.Read(requestPin, LayaJson.Default.LayaExportRequest, 4194304);
        if (request.MaximumCases is < 1 or > 46 || request.TimeoutSeconds is < 1 or > 1800 || request.MaximumResidentBytes is < 268435456 or > 2147483648) throw new InvalidDataException("Laya reference explicit case/time/resident bounds differ.");
        Budget.Safe(receiptPath); if (Path.Exists(receiptPath) || !Directory.Exists(Path.GetDirectoryName(receiptPath))) throw new IOException("Fresh reference receipt required.");
        var budget = new Budget(request.TimeoutSeconds, request.MaximumResidentBytes, request.CancelFile, cancellation); var records = new List<LayaCaseRecord>(); FilePin? final = null; LayaCapture? partial = null;
        var output = Path.GetFullPath(request.OutputDirectory); Budget.Safe(output);
        if (Path.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output))) throw new IOException("Fresh reference output directory required.");
        if (new[] { request.Weights.Path, request.Tokenizer.Path, request.TokenizerConfig.Path, request.Cases.Path, request.Contract.Path, request.EncoderConfig.Path, request.AgentConfig.Path }.Any(path => Path.GetFullPath(path).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Source inputs cannot reside inside reference output.");
        Directory.CreateDirectory(output); var planned = 0; var version = 0;
        void SavePartial()
        {
            if (partial is null) return; budget.Check(); var capture = partial with { Cases = records.ToArray() };
            if (version > 46) throw new InvalidDataException("Partial snapshot count bound47.");
            Budget.WriteNew(Path.Combine(output, "capture.partial-" + (version++).ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".json"), JsonSerializer.SerializeToUtf8Bytes(capture, LayaJson.Default.LayaCapture));
        }
        LayaExportReceipt Receipt(string status, string? error) => new(status, output, final, planned, records.Count,
            records.Count(record => record.Status == "answered"), records.Count(record => record.Status == "failed"), budget.Elapsed, budget.PeakBytes, error);
        try
        {
            budget.Verify(requestPin, 4194304);
            foreach (var pin in new[] { request.Weights, request.Tokenizer, request.TokenizerConfig, request.EncoderConfig, request.AgentConfig, request.SourceLock, request.CommonSource, request.AgentSource, request.Cases, request.Contract }) { budget.Check(); budget.Verify(pin); }
            if (request.Weights.Sha256 != LayaWeights.Sha256 || request.Tokenizer.Sha256 != Tokenization.FrozenTokenizer.Sha256 || request.TokenizerConfig.Sha256 != "424b69444bf7b5809dc2cd2e36d0bd71b8055124dd24274d6db3c655d38205e7" || request.EncoderConfig.Sha256 != "83f6916d13ef0f556ac461f28308dc2bffa7ebeadee8ec9e2db5812020ea5bb4" || request.AgentConfig.Sha256 != "25061739243b617ad88d1219ba6f8a9c86c5881ca28df024fa2d9b3b2fcc30c6" || request.SourceLock.Sha256 != PortProof.LockSha || request.CommonSource.Sha256 != PortProof.CommonSha || request.AgentSource.Sha256 != PortProof.AgentSha) throw new InvalidDataException("Frozen original assets/upstream bytes differ.");
            ValidateConfig(request, budget);
            var manifest = budget.Read(request.Cases, LayaJson.Default.LayaManifest, 4194304);
            if (manifest.SchemaVersion != "sezika.laya-oracle-inputs.v1" || manifest.Cases.Length is < 1 or > 46 || manifest.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Cases.Length || manifest.Cases.Any(item => item.Id.Length is < 1 or > 128)) throw new InvalidDataException("Fixed input manifest schema/count/identities differ.");
            using (var contract = JsonDocument.Parse(budget.Bytes(request.Contract, 4194304)))
                if (contract.RootElement.GetProperty("schema_version").GetString() != "sezika.laya-oracle-contract.port-derived.v2") throw new InvalidDataException("Port reference requires its own immutable contract; old upstream-only policy is not satisfied.");
            var runtime = PortProof.Runtime(request.ProgramSources, request.RuntimeExecutable, request.RuntimeAssembly, request.BuildReceipt, request.ProcessEvidenceDirectory, budget);
            var tokenizer = new Tokenization.FrozenTokenizer(request.Tokenizer, budget); var roles = ReadRoles(request.TokenizerConfig, tokenizer, budget);
            var provenance = new LayaProvenance("convaiinnovations/laya-multilingual", PortProof.ModelRevision, request.Weights.Sha256, request.Tokenizer.Sha256, request.TokenizerConfig.Sha256, roles,
                request.EncoderConfig.Sha256, request.AgentConfig.Sha256, PortProof.SourceRevision, "0.3.20", request.Cases.Sha256, request.Contract.Sha256, request.SourceLock.Sha256,
                request.AgentSource.Sha256, request.CommonSource.Sha256, request.ProgramSources, request.RuntimeExecutable, request.RuntimeAssembly, request.BuildReceipt, runtime.BuildExecution, runtime.Evidence, runtime.Process, requestPin, runtime.RuntimeFiles);
            planned = Math.Min(request.MaximumCases, manifest.Cases.Length); partial = new(Schema, PortProof.Kind, false, false, provenance, "in_progress", manifest.Cases.Length, planned, []); SavePartial();
            using var weights = new LayaWeights(request.Weights, budget);
            for (var index = 0; index < planned; index++)
            {
                budget.Check(); var spec = manifest.Cases[index]; Console.WriteLine("independent-laya " + (index + 1) + "/" + planned + " " + spec.Id);
                var question = LayaCases.Question(spec, manifest, budget); var state = LayaCases.State(spec, manifest, question, tokenizer, budget);
                var record = new LayaCaseRecord { Id = spec.Id, Primitive = spec.Primitive, Input = new(state, question, spec.Language), Tags = spec.Tags }; var stage = "validate";
                try
                {
                    var internalQuestion = LayaQuestion.Convert(question, budget); if (spec.Primitive != (internalQuestion.Type == "noul" ? "boolean" : internalQuestion.Type)) throw new InvalidDataException("Manifest primitive differs from materialized question.");
                    stage = "encode"; var sequence = LayaSequences.Build(tokenizer, state, internalQuestion, budget); _ = LayaSequences.Collate([sequence], budget);
                    record.TokenIds = sequence.Ids; record.MarkerPositions = sequence.Markers; record.CandidateLabels = internalQuestion.Labels; record.SequenceDiagnostics = sequence.Details;
                    stage = "infer"; var result = LayaForward.Run(weights, sequence, budget); stage = "decode";
                    record.RawLogits = result.Logits; record.Probabilities = result.Probabilities; record.ActProbabilities = result.Actions; record.Prediction = result.Prediction; record.Status = "answered";
                }
                catch (InvalidQuestionException error) { record.Failure = new(stage, "invalid_question", error.Message, nameof(InvalidQuestionException)); }
                catch (SequenceBudgetException error) { record.Failure = new(stage, "sequence_budget_exceeded", error.Message, nameof(SequenceBudgetException)); }
                records.Add(record); SavePartial();
            }
            foreach (var pin in new[] { request.Weights, request.Tokenizer, request.TokenizerConfig, request.Cases, request.Contract, request.EncoderConfig, request.AgentConfig, request.SourceLock, request.CommonSource, request.AgentSource }.Concat(request.ProgramSources)) { budget.Check(); budget.Verify(pin); }
            budget.Verify(request.RuntimeExecutable); if (request.RuntimeAssembly is not null) budget.Verify(request.RuntimeAssembly); budget.Verify(request.BuildReceipt);
            budget.Verify(requestPin, 4194304); PortProof.VerifyRuntimeAfter(runtime.RuntimeFiles, budget);
            var completed = partial with { MeasurementStatus = "completed", Cases = records.ToArray() }; var path = Path.Combine(output, "capture.json"); var bytes = JsonSerializer.SerializeToUtf8Bytes(completed, LayaJson.Default.LayaCapture);
            if (bytes.Length > 33554432) throw new InvalidDataException("Capture JSON byte bound32MiB."); Budget.WriteNew(path, bytes); final = budget.Pin(path, 33554432);
            Budget.WriteNew(receiptPath, JsonSerializer.SerializeToUtf8Bytes(Receipt("PASS_independent_laya_port_capture", null), LayaJson.Default.LayaExportReceipt)); return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException or PlatformNotSupportedException or BadImageFormatException)
        {
            Budget.WriteNew(receiptPath, JsonSerializer.SerializeToUtf8Bytes(Receipt(error is OperationCanceledException or TimeoutException ? "CANCELLED_OR_TIMED_OUT" : "FAIL", error.GetType().Name + ":" + error.Message), LayaJson.Default.LayaExportReceipt));
            return error is OperationCanceledException or TimeoutException ? 124 : 1;
        }
    }
    private static TokenizerRoles ReadRoles(FilePin pin, Tokenization.FrozenTokenizer tokenizer, Budget budget)
    {
        var bytes = budget.Bytes(pin, 1048576); budget.ValidateJson(bytes); using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        string Required(string name) => root.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()) ? item.GetString()! : throw new InvalidDataException("Original tokenizer configuration role missing: " + name);
        var cls = Required("cls_token"); var sep = Required("sep_token"); var mask = Required("mask_token"); var pad = Required("pad_token");
        var roles = new TokenizerRoles(cls, tokenizer.TokenId(cls), sep, tokenizer.TokenId(sep), mask, tokenizer.TokenId(mask), pad, tokenizer.TokenId(pad));
        if (roles.ClsId != Tokenization.FrozenTokenizer.Bos || roles.SepId != Tokenization.FrozenTokenizer.Eos || roles.MaskId != Tokenization.FrozenTokenizer.Mask || roles.PadId != Tokenization.FrozenTokenizer.Pad)
            throw new InvalidDataException("Frozen tokenizer role/dictionary mapping differs from the audited sequence/collation policy.");
        return roles;
    }
    private static void ValidateConfig(LayaExportRequest request, Budget budget)
    {
        var encoderBytes = budget.Bytes(request.EncoderConfig, 1048576); budget.ValidateJson(encoderBytes); using var encoder = JsonDocument.Parse(encoderBytes); var e = encoder.RootElement;
        if (e.GetProperty("hidden_size").GetInt32() != 768 || e.GetProperty("intermediate_size").GetInt32() != 1152 || e.GetProperty("num_hidden_layers").GetInt32() != 22 || e.GetProperty("num_attention_heads").GetInt32() != 12 || e.GetProperty("vocab_size").GetInt32() != 256000 || e.GetProperty("global_attn_every_n_layers").GetInt32() != 3 || e.GetProperty("local_attention").GetInt32() != 128 || e.GetProperty("norm_eps").GetSingle() != 1e-5f || e.GetProperty("hidden_activation").GetString() != "gelu" || e.GetProperty("attention_bias").GetBoolean() || e.GetProperty("mlp_bias").GetBoolean() || e.GetProperty("norm_bias").GetBoolean() || e.GetProperty("rope_parameters").GetProperty("full_attention").GetProperty("rope_theta").GetSingle() != 160000 || e.GetProperty("rope_parameters").GetProperty("sliding_attention").GetProperty("rope_theta").GetSingle() != 160000) throw new InvalidDataException("Audited effective Laya ModernBERT architecture differs.");
        var agentBytes = budget.Bytes(request.AgentConfig, 1048576); budget.ValidateJson(agentBytes); using var agent = JsonDocument.Parse(agentBytes); var a = agent.RootElement;
        if (a.GetProperty("encoder").GetString() != "jhu-clsp/mmBERT-base" || a.GetProperty("head_layers").GetInt32() != 2 || a.GetProperty("max_len").GetInt32() != 1024 || a.GetProperty("head_max_len").GetInt32() != 256 || !a.GetProperty("temperature").EnumerateArray().Select(item => item.GetSingle()).SequenceEqual([1f, 1f, 1f]) || a.GetProperty("temperature_by_options").EnumerateObject().MoveNext()) throw new InvalidDataException("Frozen two-layer head/length/fixed1 temperature differs.");
    }
}
