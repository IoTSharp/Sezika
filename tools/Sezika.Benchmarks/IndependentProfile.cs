using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Benchmarks;

internal sealed record IndependentProfileOptions(string Package, string Head, string Identity, string IdentityHash,
    string FeatureHash, string HeadHash, string Request, string RequestHash, string Output,
    int Samples, int Warmup, int TimeoutSeconds, int Device, int MaxTokens, int MaxCharacters)
{
    internal static IndependentProfileOptions Parse(string[] args)
    {
        if (args.Length is < 18 or > 40) throw new ArgumentException("需要完整参数，最多40个参数项。");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5) || i + 1 >= args.Length ||
                args[i] is not ("--package" or "--head" or "--identity" or "--identity-sha256" or "--feature-sha256" or
                    "--head-sha256" or "--request" or "--request-sha256" or "--output" or "--samples" or "--warmup" or
                    "--timeout-seconds" or "--device" or "--max-tokens" or "--max-characters" or "--backend") ||
                !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("参数重复、未知、不完整或解析超时。");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("缺少参数：" + key);
        string Hash(string key) { var value = Required(key); if (!IndependentProfile.IsHash(value)) throw new ArgumentException("SHA256无效：" + key); return value; }
        int Number(string key, int fallback, int min, int max)
        {
            var text = values.GetValueOrDefault(key, fallback.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
                ? value : throw new ArgumentException($"{key}必须在{min}–{max}内。");
        }
        if (values.GetValueOrDefault("--backend", "cuda") != "cuda") throw new ArgumentException("此入口仅使用CUDA；CPU仅用于tiny兼容检查。");
        return new(Required("--package"), Required("--head"), Required("--identity"), Hash("--identity-sha256"),
            Hash("--feature-sha256"), Hash("--head-sha256"), Required("--request"), Hash("--request-sha256"), Required("--output"),
            Number("--samples", 1, 1, 5), Number("--warmup", 0, 0, 1), Number("--timeout-seconds", 120, 1, 300),
            Number("--device", 0, 0, 16), Number("--max-tokens", 1024, 2, 1024), Number("--max-characters", 262144, 1, 262144));
    }
}

internal sealed record ProfileExpectedIdentity(string ModelId, string EncoderSha256, string TokenizerSha256,
    string DataManifestSha256, int HiddenSize, string Protocol, string LengthPolicy)
{
    internal MarkerFeatureIdentity ToIdentity()
    {
        if (string.IsNullOrWhiteSpace(ModelId) || !ModelId.StartsWith("sezika/", StringComparison.Ordinal) || ModelId.Length > 256 ||
            !IndependentProfile.IsHash(EncoderSha256) || !IndependentProfile.IsHash(TokenizerSha256) || !IndependentProfile.IsHash(DataManifestSha256) ||
            HiddenSize is < 1 or > 4096 || Protocol != "decision-v1" || LengthPolicy != "strict")
            throw new InvalidDataException("期望身份必须完整且绑定decision-v1/strict。");
        return new(ModelId, EncoderSha256, TokenizerSha256, DataManifestSha256, HiddenSize, Protocol, LengthPolicy);
    }
}

internal sealed record ProfileArtifact(string Kind, string Path, string Sha256, long Bytes);
internal sealed record IndependentProfileSequenceObservation(string QuestionId, string Type, string[] CandidateIds, int Tokens,
    string TokenIdsSha256, string MarkersSha256);
internal sealed record ProfilePreparedSequence(string QuestionId, MarkerQuestionType Type, int[] TokenIds,
    int[] MarkerPositions, string[] CandidateIds);
internal sealed record ProfileAnswer(string QuestionId, string Type, string[] CandidateIds, double[] Probabilities,
    int SelectedIndex, double Concentration, string Status);
internal sealed class ProfileRequestObservation
{
    public int Index { get; init; }
    public string Kind { get; init; } = "diagnostic_sample";
    public string Status { get; set; } = "not_started";
    public int? PlannedQuestions { get; set; }
    public int PipelineScoreEntered { get; set; }
    public int PipelineScoreReturned { get; set; }
    public bool TypedRequestReturned { get; set; }
    public int ValidatedTypedAnswers { get; set; }
    public double? E2eMilliseconds { get; set; }
    public string? Error { get; set; }
    public ProfileAnswer[] Answers { get; set; } = [];
}

internal sealed class IndependentProfileReport
{
    public int SchemaVersion => 1;
    public string Contract => "independent-e2e-diagnostic-v1";
    public string Status { get; set; } = "failed";
    public string ObservationScope => "runtime.Evaluate; includes sequence construction; excludes validation/serialization/output";
    public string ForwardObservationScope => "underlying_pipeline.Score_call; does not establish encoder/kernel entry";
    public bool FixtureOnly { get; init; }
    public bool PerformanceAccepted => false;
    public bool QualityAccepted => false;
    public bool Calibrated => false;
    public bool TrainingAccepted => false;
    public bool LicenseApproved => false;
    public bool SealedTestAccepted => false;
    public int FormalSamples => 0;
    public double? EncoderMilliseconds => null;
    public double? HeadMilliseconds => null;
    public int? EncoderForwardsEntered => null;
    public int? EncoderForwardsCompleted => null;
    public string HeadOrigin { get; init; } = "externally_supplied_asset; training provenance not independently accepted";
    public string Backend { get; set; } = "not_started";
    public string Protocol => "decision-v1";
    public string? ModelId { get; set; }
    public string? SourceModelId { get; set; }
    public string? Revision { get; set; }
    public MarkerFeatureIdentity? HeadIdentity { get; set; }
    public string? HeadSha256 { get; set; }
    public string? FeatureSha256 { get; set; }
    public int PlannedRequests { get; init; }
    public int? RequestQuestions { get; set; }
    public int? PlannedQuestions { get; set; }
    public bool GpuResourcesStarted { get; set; }
    public bool? ModelDisposed { get; set; }
    public int? ActiveWorkspaces { get; set; }
    public long? WorkspaceBytes { get; set; }
    public bool? DeviceDisposeReturned { get; set; }
    public CudaDeviceInfo? Device { get; set; }
    public CudaMemorySnapshot? CudaAfterRuntimeRelease { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public string? Error { get; set; }
    public List<string> CleanupErrors { get; } = [];
    public List<ProfileArtifact> Artifacts { get; } = [];
    public List<IndependentProfileSequenceObservation> Sequences { get; } = [];
    public List<ProfileRequestObservation> Requests { get; } = [];
    public string Runtime => RuntimeInformation.FrameworkDescription;
    public string Rid => RuntimeInformation.RuntimeIdentifier;
    public bool ManagedHostDetected => AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not null;
    public bool DynamicCodeSupported => RuntimeFeature.IsDynamicCodeSupported;
    public bool DynamicCodeCompiled => RuntimeFeature.IsDynamicCodeCompiled;
    public int ProcessId => Environment.ProcessId;
    internal Stopwatch? CleanupClock { get; set; }
}

internal sealed class ProfileBudget : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan _limit;
    private readonly CancellationTokenSource _source;
    internal ProfileBudget(int seconds, CancellationToken cancellation = default)
    {
        if (seconds is < 1 or > 300) throw new ArgumentException("工作预算必须为1–300秒。");
        _limit = TimeSpan.FromSeconds(seconds); _source = CancellationTokenSource.CreateLinkedTokenSource(cancellation); _source.CancelAfter(_limit);
    }
    internal CancellationToken Token => _source.Token;
    internal double Elapsed => _clock.Elapsed.TotalMilliseconds;
    internal TimeSpan Remaining { get { Check(); return _limit - _clock.Elapsed; } }
    internal void Check() { Token.ThrowIfCancellationRequested(); if (_clock.Elapsed >= _limit) throw new TimeoutException("画像工作预算耗尽。"); }
    internal void Cancel() => _source.Cancel();
    public void Dispose() => _source.Dispose();
}

internal sealed class ObservedIndependentPipeline(IIndependentDecisionPipeline inner, ProfilePreparedSequence[] expected,
    ProfileBudget budget) : IIndependentDecisionPipeline
{
    private ProfileRequestObservation? _row;
    private int _question;
    private bool _disposed;
    public MarkerFeatureIdentity Identity => inner.Identity;
    public string FeatureSha256 => inner.FeatureSha256;
    public string HeadSha256 => inner.HeadSha256;
    public string Backend => inner.Backend;
    public int MaxTokens => inner.MaxTokens;
    internal void Begin(ProfileRequestObservation row) { _row = row; _question = 0; }
    public double[] Score(ReadOnlySpan<int> ids, MarkerQuestionType type, ReadOnlySpan<int> markers, CancellationToken token = default)
    {
        budget.Check(); token.ThrowIfCancellationRequested();
        var row = _row ?? throw new InvalidOperationException("没有当前请求观察。");
        if (_question >= expected.Length || row.PipelineScoreEntered >= 32) throw new InvalidDataException("Score数量越界。");
        var plan = expected[_question++];
        if (plan.Type != type || !ids.SequenceEqual(plan.TokenIds) || !markers.SequenceEqual(plan.MarkerPositions))
            throw new InvalidDataException("实际Score输入与discovery不符。");
        row.PipelineScoreEntered++;
        Console.WriteLine($"independent-profile: request {row.Index + 1}, Score {_question}/{expected.Length}");
        var result = inner.Score(ids, type, markers, token);
        row.PipelineScoreReturned++;
        return result;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; inner.Dispose(); }
}

internal static class IndependentProfile
{
    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string TypeName(MarkerQuestionType type) => type switch
    { MarkerQuestionType.Choice => "choice", MarkerQuestionType.Score => "score", MarkerQuestionType.Boolean => "boolean", _ => throw new InvalidDataException("未知题型。") };

    internal static int Run(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("独立CUDA诊断E2E画像：--package目录 --head文件 --identity期望身份JSON --identity-sha256值 --feature-sha256值 --head-sha256值 --request文件 --request-sha256值 --output新文件。可选--samples 1–5 --warmup 0–1 --timeout-seconds 1–300 --device 0–16 --max-tokens 2–1024 --max-characters 1–262144。encoder/head分项未测，全部正式接受为false；CPU仅check-independent-profile tiny检查。");
            return 0;
        }
        IndependentProfileOptions options;
        try { options = IndependentProfileOptions.Parse(args); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
        var report = CreateReport(options.Samples, options.Warmup);
        using var budget = new ProfileBudget(options.TimeoutSeconds);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; budget.Cancel(); };
        Console.CancelKeyPress += cancel;
        var leases = new List<FileStream>(3);
        IndependentEncoderModelPackage? model = null; IIndependentDecisionPipeline? pipeline = null; CudaDevice? device = null;
        try
        {
            var output = Path.GetFullPath(options.Output);
            if (!Directory.Exists(Path.GetDirectoryName(output)) || File.Exists(output)) throw new IOException("输出目录必须存在，目标文件必须未存在。");
            var identityBytes = ReadBound(options.Identity, options.IdentityHash, 16384, leases, report, "expected_identity", budget);
            var requestBytes = ReadBound(options.Request, options.RequestHash, 1048576, leases, report, "request", budget);
            _ = ReadBound(options.Head, options.HeadHash, 131072, leases, report, "head", budget);
            var declaration = JsonSerializer.Deserialize(identityBytes, IndependentProfileJson.Default.ProfileExpectedIdentity)
                ?? throw new InvalidDataException("期望身份为空。");
            var identity = declaration.ToIdentity();
            var request = IndependentDecisionV1Parser.Parse(requestBytes);
            var head = IndependentMarkerHeadAssetStore.Load(options.Head, identity, options.FeatureHash, options.HeadHash, budget.Token);
            model = IndependentModelLoader.Load(options.Package, new EncoderExecutionOptions
            { Kernel = EncoderKernelMode.Scalar, Deadline = budget.Remaining, MaxWorkspaceBytes = 128L * 1024 * 1024 }, budget.Token);
            ValidateAssets(model, head, options.MaxTokens);
            BindAssetIdentity(report, model, head);
            var sequences = Discover(model, request, options.MaxTokens, options.MaxCharacters, budget, report);
            AddCodeIdentity(report, budget);
            budget.Check(); report.GpuResourcesStarted = true;
            device = CudaDevice.Open(options.Device, enableProfiling: false); report.Device = device.Info;
            pipeline = new IndependentCudaDecisionPipeline(device, model, head, budget.Token);
            if (device.GetMemorySnapshot().PeakOwnedBytes > 3UL * 1024 * 1024 * 1024) throw new InvalidDataException("CUDAowned峰值超过3GiB。");
            Execute(model, head, pipeline, request, sequences, options.Samples, options.Warmup,
                options.MaxTokens, options.MaxCharacters, budget, report);
        }
        catch (Exception error) { report.Status = "failed"; report.Error = Describe(error); }
        finally
        {
            // Idempotent owners may already have been disposed by Execute or a failed runtime constructor.
            var cleanup = report.CleanupClock ??= Stopwatch.StartNew();
            Cleanup("pipeline", () => pipeline?.Dispose(), cleanup, report);
            Cleanup("model", () => model?.Dispose(), cleanup, report);
            Cleanup("cuda_snapshot", () =>
            {
                if (device is null) return;
                var snapshot = device.GetMemorySnapshot(); report.CudaAfterRuntimeRelease = snapshot;
                if (snapshot.OwnedBytes != 0 || snapshot.OwnedAllocationCount != 0 || snapshot.LoadedModuleCount != 0 || snapshot.ReleaseFailureCount != 0)
                    throw new InvalidDataException("CUDAowned资源或release failure未归零。");
            }, cleanup, report);
            Cleanup("device", () => { if (device is not null) { device.Dispose(); report.DeviceDisposeReturned = true; } }, cleanup, report);
            for (var index = 0; index < leases.Count && index < 3; index++)
                Cleanup("input_lease_" + index, leases[index].Dispose, cleanup, report);
            if (model is not null) ObserveModel(model, report);
            if (report.CleanupErrors.Count > 0) report.Status = "failed";
            report.ElapsedMilliseconds = budget.Elapsed;
            Console.CancelKeyPress -= cancel;
        }
        try { Publish(options.Output, report); }
        catch (Exception error) { Console.Error.WriteLine("报告写入失败：" + Describe(error)); return 2; }
        Console.WriteLine($"{report.Status}: {options.Output}; 正式样本=0，分项未测");
        return report.Status == "diagnostic_complete" ? 0 : 2;
    }

    internal static void ValidateAssets(IndependentEncoderModelPackage model, IndependentMarkerHeadAsset head, int maxTokens)
    {
        var id = head.Identity;
        if (model.IsDisposed || id.ModelId != model.ModelId || !id.EncoderSha256.Equals(model.EncoderSha256, StringComparison.OrdinalIgnoreCase) ||
            !id.TokenizerSha256.Equals(model.TokenizerSha256, StringComparison.OrdinalIgnoreCase) || id.HiddenSize != model.Encoder.Config.HiddenSize ||
            head.Head.HiddenSize != id.HiddenSize || !IsHash(id.DataManifestSha256) || !IsHash(head.FeatureSha256) || !IsHash(head.FileSha256) ||
            id.Protocol != "decision-v1" || id.LengthPolicy != "strict" || maxTokens > model.Encoder.Config.MaxTokens)
            throw new InvalidDataException("模型/head完整身份或token预算不符。");
    }

    internal static IndependentProfileReport CreateReport(int samples, int warmup, bool fixtureOnly = false)
    {
        if (samples is < 1 or > 5 || warmup is < 0 or > 1) throw new InvalidDataException("计划请求数量越界。");
        var report = new IndependentProfileReport { PlannedRequests = samples + warmup, FixtureOnly = fixtureOnly,
            HeadOrigin = fixtureOnly ? "synthetic in-memory F32 fixture; no training report/asset file" :
                "externally_supplied_asset; training provenance not independently accepted" };
        for (var index = 0; index < report.PlannedRequests && index < 6; index++)
            report.Requests.Add(new() { Index = index, Kind = index < warmup ? "warmup" : "diagnostic_sample" });
        return report;
    }

    internal static void BindAssetIdentity(IndependentProfileReport report, IndependentEncoderModelPackage model, IndependentMarkerHeadAsset head)
    {
        report.ModelId = model.ModelId; report.SourceModelId = model.SourceModelId; report.Revision = model.Revision;
        report.HeadIdentity = head.Identity; report.HeadSha256 = head.FileSha256; report.FeatureSha256 = head.FeatureSha256;
    }

    internal static ProfilePreparedSequence[] Discover(IndependentEncoderModelPackage model, IndependentDecisionRequest request,
        int maxTokens, int maxCharacters, ProfileBudget budget, IndependentProfileReport report)
    {
        budget.Check();
        if (request.SchemaVersion != 1 || request.ModelId != model.ModelId || request.Questions is null || request.Questions.Count is < 1 or > 32)
            throw new InvalidDataException("独立request身份或1–32题边界不符。");
        var ids = new HashSet<string>(StringComparer.Ordinal); var result = new ProfilePreparedSequence[request.Questions.Count];
        for (var index = 0; index < result.Length; index++)
        {
            budget.Check(); var question = request.Questions[index];
            if (question is null || string.IsNullOrWhiteSpace(question.Id) || question.Id.Length > 65536 || !ids.Add(question.Id))
                throw new InvalidDataException("题目ID必须有界且唯一。");
            var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, request, question,
                new IndependentDecisionSequenceOptions { MaxTokens = maxTokens, MaxCharacters = maxCharacters, Deadline = budget.Remaining }, budget.Token);
            var candidates = sequence.CandidateIds ?? throw new InvalidDataException("序列缺少候选ID。");
            result[index] = new(question.Id, sequence.Type, sequence.TokenIds.ToArray(), sequence.MarkerPositions.ToArray(), candidates.ToArray());
            report.Sequences.Add(new(question.Id, TypeName(sequence.Type), candidates.ToArray(), sequence.TokenIds.Length,
                IntegerHash(sequence.TokenIds), IntegerHash(sequence.MarkerPositions)));
        }
        report.RequestQuestions = result.Length; report.PlannedQuestions = checked(report.PlannedRequests * result.Length);
        for (var index = 0; index < report.Requests.Count && index < 6; index++) report.Requests[index].PlannedQuestions = result.Length;
        return result;
    }

    internal static void Execute(IndependentEncoderModelPackage model, IndependentMarkerHeadAsset head, IIndependentDecisionPipeline pipeline,
        IndependentDecisionRequest request, ProfilePreparedSequence[] sequences, int samples, int warmup,
        int maxTokens, int maxCharacters, ProfileBudget budget, IndependentProfileReport report)
    {
        if (samples is < 1 or > 5 || warmup is < 0 or > 1 || sequences.Length is < 1 or > 32 || report.PlannedRequests != samples + warmup)
            throw new InvalidDataException("执行数量边界或报告分母不符。");
        if (report.Requests.Count != report.PlannedRequests || report.RequestQuestions != sequences.Length ||
            report.Requests.Any(row => row.PlannedQuestions != sequences.Length || row.Status != "not_started"))
            throw new InvalidDataException("计划行或已核题数不符。");
        BindAssetIdentity(report, model, head); report.Backend = pipeline.Backend;
        var observer = new ObservedIndependentPipeline(pipeline, sequences, budget);
        IndependentDecisionV1Runtime? runtime = null;
        try
        {
            runtime = new(model, head, new IndependentDecisionRuntimeOptions
            { MaxTokens = maxTokens, MaxCharacters = maxCharacters, Deadline = budget.Remaining }, observer);
            for (var index = 0; index < report.Requests.Count && index < 6; index++)
            {
                budget.Check(); var row = report.Requests[index]; observer.Begin(row); row.Status = "started";
                try
                {
                    var clock = Stopwatch.StartNew(); IndependentDecisionResponse response;
                    try { response = runtime.Evaluate(request, budget.Token); row.TypedRequestReturned = true; }
                    finally { row.E2eMilliseconds = clock.Elapsed.TotalMilliseconds; }
                    budget.Check(); row.Answers = ValidateResponse(response, sequences, model, pipeline.Backend);
                    row.ValidatedTypedAnswers = row.Answers.Length; row.Status = "returned";
                }
                catch (Exception error)
                {
                    row.Error = Describe(error);
                    row.Status = error is OperationCanceledException or TimeoutException ? "cancelled" : "failed";
                    throw;
                }
            }
            budget.Check(); report.Status = "diagnostic_complete";
        }
        catch (Exception error) { report.Status = "failed"; report.Error = Describe(error); }
        finally
        {
            var cleanup = report.CleanupClock ??= Stopwatch.StartNew();
            Cleanup("runtime", () => runtime?.Dispose(), cleanup, report);
            Cleanup("observed_pipeline", observer.Dispose, cleanup, report);
            Cleanup("owned_model", model.Dispose, cleanup, report);
            ObserveModel(model, report);
            if (report.CleanupErrors.Count > 0) report.Status = "failed";
        }
    }

    private static ProfileAnswer[] ValidateResponse(IndependentDecisionResponse response, ProfilePreparedSequence[] sequences,
        IndependentEncoderModelPackage model, string backend)
    {
        if (response.ModelId != model.ModelId || response.EncoderSha256 != model.EncoderSha256 || response.TokenizerSha256 != model.TokenizerSha256 ||
            response.Protocol != "decision-v1" || response.Backend != backend || response.Status != "uncalibrated" || response.Answers.Count != sequences.Length)
            throw new InvalidDataException("实际typed response身份或数量不符。");
        var result = new ProfileAnswer[sequences.Length];
        for (var index = 0; index < result.Length; index++)
        {
            var answer = response.Answers[index]; var expected = sequences[index];
            if (answer.QuestionId != expected.QuestionId || answer.Type != expected.Type || !answer.CandidateIds.SequenceEqual(expected.CandidateIds) || answer.Probabilities.Count != expected.MarkerPositions.Length ||
                answer.SelectedIndex < 0 || answer.SelectedIndex >= answer.Probabilities.Count || answer.Status != "uncalibrated" ||
                !double.IsFinite(answer.Concentration) || answer.Concentration is < 0 or > 1 ||
                answer.Probabilities.Any(value => !double.IsFinite(value) || value is < 0 or > 1) || Math.Abs(answer.Probabilities.Sum() - 1) > 1e-9)
                throw new InvalidDataException("实际typed概率/候选顺序不符。");
            result[index] = new(answer.QuestionId, TypeName(answer.Type), answer.CandidateIds.ToArray(), answer.Probabilities.ToArray(),
                answer.SelectedIndex, answer.Concentration, answer.Status);
        }
        return result;
    }

    private static byte[] ReadBound(string path, string expectedHash, int maximum, List<FileStream> leases,
        IndependentProfileReport report, string kind, ProfileBudget budget)
    {
        budget.Check(); var absolute = Path.GetFullPath(path); var file = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
        leases.Add(file);
        if (file.Length < 1 || file.Length > maximum) throw new InvalidDataException(kind + "字节越界。");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); budget.Check(); var hash = Hash(bytes);
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(kind + "原字节SHA不符。");
        report.Artifacts.Add(new(kind, absolute, hash, bytes.Length)); return bytes;
    }

    private static void AddCodeIdentity(IndependentProfileReport report, ProfileBudget budget)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("无法确定实际进程文件。");
        var paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is null ? new[] { executable } :
            new[] { executable, Path.Combine(AppContext.BaseDirectory, "Sezika.Benchmarks.dll"), Path.Combine(AppContext.BaseDirectory, "Sezika.dll"), Path.Combine(AppContext.BaseDirectory, "Sezika.Cuda.dll") };
        foreach (var path in paths)
        {
            budget.Check(); using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 64L * 1024 * 1024) throw new InvalidDataException("执行产物超过64MiB。");
            report.Artifacts.Add(new("execution_artifact", path, Convert.ToHexString(SHA256.HashData(file)), file.Length)); budget.Check();
        }
    }

    private static string IntegerHash(int[] values)
    {
        var bytes = new byte[checked(values.Length * 4)];
        for (var index = 0; index < values.Length; index++) System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), values[index]);
        return Hash(bytes);
    }
    internal static string Describe(Exception error) { var text = error.GetType().Name + ": " + error.Message; return text[..Math.Min(text.Length, 4096)]; }
    private static void Cleanup(string name, Action action, Stopwatch clock, IndependentProfileReport report)
    {
        if (clock.Elapsed >= TimeSpan.FromSeconds(5)) { report.CleanupErrors.Add(name + ": shared cleanup budget exhausted; not checked"); return; }
        try { action(); if (clock.Elapsed >= TimeSpan.FromSeconds(5)) report.CleanupErrors.Add(name + ": cleanup returned after deadline"); }
        catch (Exception error) { report.CleanupErrors.Add(name + ": " + Describe(error)); }
    }
    private static void ObserveModel(IndependentEncoderModelPackage model, IndependentProfileReport report)
    {
        report.ModelDisposed = model.IsDisposed; report.ActiveWorkspaces = model.Encoder.WorkspacePool.ActiveCount;
        report.WorkspaceBytes = model.Encoder.WorkspacePool.OutstandingBytes;
        if (!model.IsDisposed || report.ActiveWorkspaces != 0 || report.WorkspaceBytes != 0) report.CleanupErrors.Add("model/workspace release not complete");
    }

    internal static void Publish(string output, IndependentProfileReport report, CancellationToken cancellation = default,
        Action<FileStream>? afterCreate = null, Action? afterWrite = null)
    {
        using var audit = new ProfileBudget(5, cancellation); audit.Check();
        var destination = Path.GetFullPath(output); var directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory)) throw new IOException("输出目录不存在。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, IndependentProfileJson.Default.IndependentProfileReport);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("报告超过4MiB。");
        var partial = Path.Combine(directory, ".independent-profile-" + Guid.NewGuid().ToString("N") + ".partial");
        var created = false; var moved = false; DateTime? createdUtc = null;
        FileStream? owner = null; long? ownedLength = null; byte[]? ownedHash = null;
        Exception? failure = null; var cleanupErrors = new List<Exception>(3);
        try
        {
            owner = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true; createdUtc = File.GetCreationTimeUtc(partial);
            // Creation ownership precedes the first write/flush and every test-only fault callback.
            afterCreate?.Invoke(owner);
            owner.Write(bytes); owner.Flush(true); ownedLength = owner.Length;
            owner.Position = 0; ownedHash = SHA256.HashData(owner);
            if (ownedLength != bytes.Length || !ownedHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)))
                throw new IOException("写入的报告字节与序列化结果不符。");
            afterWrite?.Invoke(); audit.Check(); owner.Dispose(); owner = null;
            audit.Check(); File.Move(partial, destination, false); moved = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            // This independent reserve remains usable when audit is cancelled or its five seconds elapsed.
            var cleanup = Stopwatch.StartNew();
            try
            {
                if (owner is not null)
                {
                    CheckPublicationCleanup(cleanup);
                    // A failed write may leave only a prefix. Snapshot the still exclusively owned handle.
                    ownedLength = owner.Length;
                    if (ownedLength > 4 * 1024 * 1024) throw new IOException("partial超出核验大小；保留：" + partial);
                    owner.Position = 0; ownedHash = SHA256.HashData(owner); CheckPublicationCleanup(cleanup);
                }
            }
            catch (Exception error) { ownedLength = null; ownedHash = null; cleanupErrors.Add(error); }
            try { owner?.Dispose(); } catch (Exception error) { cleanupErrors.Add(error); }
            if (created && !moved && File.Exists(partial))
            {
                try
                {
                    CheckPublicationCleanup(cleanup);
                    if (createdUtc is null || ownedLength is null || ownedHash is null || Path.GetDirectoryName(partial) != directory)
                        throw new IOException("partial归属不能证明；保留：" + partial);
                    using (var lease = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        if (File.GetCreationTimeUtc(partial) != createdUtc || lease.Length != ownedLength ||
                            !SHA256.HashData(lease).AsSpan().SequenceEqual(ownedHash)) throw new IOException("partial身份改变；保留：" + partial);
                        CheckPublicationCleanup(cleanup);
                    }
                    File.Delete(partial); CheckPublicationCleanup(cleanup);
                }
                catch (Exception error) { cleanupErrors.Add(new IOException("partial回收未完成；路径：" + partial, error)); }
            }
        }
        if (cleanupErrors.Count > 0)
        {
            if (failure is not null) cleanupErrors.Insert(0, failure);
            throw new AggregateException("报告发布/partial回收失败；未知对象保留；路径：" + partial, cleanupErrors);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CheckPublicationCleanup(Stopwatch clock)
    { if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("partial独立5秒回收预算耗尽。"); }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32, WriteIndented = true)]
[JsonSerializable(typeof(ProfileExpectedIdentity))]
[JsonSerializable(typeof(IndependentProfileReport))]
internal partial class IndependentProfileJson : JsonSerializerContext;
