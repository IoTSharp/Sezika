using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Benchmarks;

internal sealed record DiagnosticProfileOptions(string Package, string Head, string Identity, string IdentityHash,
    string DescriptorHash, string HeadHash, int Seed, string Request, string RequestHash, string Output,
    int Samples, int Warmup, int TimeoutSeconds, int Device, int MaxTokens, int MaxCharacters)
{
    internal static DiagnosticProfileOptions Parse(string[] args)
    {
        if (args.Length is < 20 or > 40) throw new ArgumentException("需要完整参数，最多40个参数项。");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < args.Length; index += 2)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5) || index + 1 >= args.Length ||
                args[index] is not ("--package" or "--head" or "--identity" or "--identity-sha256" or "--descriptor-sha256" or
                    "--head-sha256" or "--seed" or "--request" or "--request-sha256" or "--output" or "--samples" or "--warmup" or
                    "--timeout-seconds" or "--device" or "--max-tokens" or "--max-characters" or "--backend") ||
                !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("参数重复、未知、不完整或解析超时。");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("缺少参数：" + key);
        string Hash(string key) { var value = Required(key); if (!IndependentProfile.IsHash(value)) throw new ArgumentException("SHA256无效：" + key); return value; }
        int Number(string key, int fallback, int minimum, int maximum)
        {
            var text = values.GetValueOrDefault(key, fallback.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum
                ? value : throw new ArgumentException($"{key}必须在{minimum}–{maximum}内。");
        }
        if (values.GetValueOrDefault("--backend", "cuda") != "cuda") throw new ArgumentException("此入口仅使用CUDA，无CPU回退。");
        if (!int.TryParse(Required("--seed"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
            throw new ArgumentException("--seed必须为Int32。");
        return new(Required("--package"), Required("--head"), Required("--identity"), Hash("--identity-sha256"),
            Hash("--descriptor-sha256"), Hash("--head-sha256"), seed, Required("--request"), Hash("--request-sha256"), Required("--output"),
            Number("--samples", 1, 1, 5), Number("--warmup", 0, 0, 1), Number("--timeout-seconds", 120, 1, 300),
            Number("--device", 0, 0, 16), Number("--max-tokens", 1024, 2, 1024), Number("--max-characters", 262144, 1, 262144));
    }
}

internal sealed class ObservedDiagnosticPipeline(IDiagnosticDecisionPipeline inner, ProfilePreparedSequence[] expected,
    ProfileBudget budget) : IDiagnosticDecisionPipeline
{
    private ProfileRequestObservation? _row;
    private int _question;
    private bool _disposed;
    public DiagnosticHeadIdentity Identity => inner.Identity;
    public string DiagnosticAssetSha256 => inner.DiagnosticAssetSha256;
    public string InitializationDescriptorSha256 => inner.InitializationDescriptorSha256;
    public string Backend => inner.Backend;
    public int MaxTokens => inner.MaxTokens;
    internal void Begin(ProfileRequestObservation row) { _row = row; _question = 0; }
    public double[] Score(ReadOnlySpan<int> ids, MarkerQuestionType type, ReadOnlySpan<int> markers, CancellationToken token = default)
    {
        budget.Check(); token.ThrowIfCancellationRequested();
        var row = _row ?? throw new InvalidOperationException("没有当前诊断请求观察。");
        if (_question >= expected.Length || row.PipelineScoreEntered >= 32) throw new InvalidDataException("Score数量越界。");
        var plan = expected[_question++];
        if (plan.Type != type || !ids.SequenceEqual(plan.TokenIds) || !markers.SequenceEqual(plan.MarkerPositions))
            throw new InvalidDataException("实际诊断Score输入与discovery不符。");
        row.PipelineScoreEntered++;
        Console.WriteLine($"independent-diagnostic-profile: request {row.Index + 1}, Score {_question}/{expected.Length}");
        var result = inner.Score(ids, type, markers, token);
        row.PipelineScoreReturned++;
        return result;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; inner.Dispose(); }
}

internal static class DiagnosticIndependentProfile
{
    internal static int Run(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("零更新诊断CUDA E2E画像：--package目录 --head SZDH1文件 --identity独立身份JSON --identity-sha256值 --descriptor-sha256值 --head-sha256值 --seed Int32 --request文件 --request-sha256值 --output新文件。可选--samples 1–5 --warmup 0–1 --timeout-seconds 1–300 --device 0–16 --max-tokens 2–1024 --max-characters 1–262144。全部正式接受false、formal HeadIdentity/FeatureSha256 null、encoder/head分项未测；无CPU回退。");
            return 0;
        }
        DiagnosticProfileOptions options;
        try { options = DiagnosticProfileOptions.Parse(args); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
        var report = CreateReport(options.Samples, options.Warmup);
        using var budget = new ProfileBudget(options.TimeoutSeconds);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; budget.Cancel(); };
        Console.CancelKeyPress += cancel;
        // Preallocate every owner slot; after a successful acquisition registration cannot allocate.
        var leases = new FileStream?[7];
        IndependentEncoderModelPackage? model = null; IDiagnosticDecisionPipeline? pipeline = null; CudaDevice? device = null;
        try
        {
            var output = Path.GetFullPath(options.Output);
            if (!Directory.Exists(Path.GetDirectoryName(output)) || File.Exists(output)) throw new IOException("输出目录必须存在，目标文件必须未存在。");
            var identityBytes = ReadBound(options.Identity, options.IdentityHash, 16384, leases, 0, report, "expected_identity", budget);
            var requestBytes = ReadBound(options.Request, options.RequestHash, 1048576, leases, 1, report, "request", budget);
            _ = ReadBound(options.Head, options.HeadHash, 131072, leases, 2, report, "diagnostic_head", budget);
            var declaration = JsonSerializer.Deserialize(identityBytes, DiagnosticProfileJson.Default.DiagnosticProfileExpectedIdentity)
                ?? throw new InvalidDataException("诊断期望身份为空。");
            var identity = declaration.ToIdentity();
            var request = IndependentDecisionV1Parser.Parse(requestBytes);
            var head = DiagnosticHeadAssetStore.Load(options.Head, identity, options.Seed, options.DescriptorHash, options.HeadHash, budget.Token);
            model = IndependentModelLoader.Load(options.Package, new EncoderExecutionOptions
            { Kernel = EncoderKernelMode.Scalar, Deadline = budget.Remaining, MaxWorkspaceBytes = 128L * 1024 * 1024 }, budget.Token);
            ValidateAssets(model, head, options.MaxTokens);
            BindAssetIdentity(report, model, head);
            var sequences = Discover(model, request, options.MaxTokens, options.MaxCharacters, budget, report);
            AddCodeIdentity(leases, report, budget);
            budget.Check(); report.GpuResourcesStarted = true;
            device = CudaDevice.Open(options.Device, enableProfiling: false); report.Device = device.Info;
            pipeline = new DiagnosticCudaDecisionPipeline(device, model, head, budget.Token);
            if (device.GetMemorySnapshot().PeakOwnedBytes > 3UL * 1024 * 1024 * 1024) throw new InvalidDataException("CUDAowned峰值超过3GiB。");
            Execute(model, head, pipeline, request, sequences, options.Samples, options.Warmup,
                options.MaxTokens, options.MaxCharacters, budget, report);
        }
        catch (Exception error) { report.Status = "failed"; report.Error = IndependentProfile.Describe(error); }
        finally
        {
            var cleanup = report.CleanupClock ??= Stopwatch.StartNew();
            Cleanup("pipeline", () => pipeline?.Dispose(), cleanup, report);
            Cleanup("model", () => model?.Dispose(), cleanup, report);
            Cleanup("cuda_snapshot", () =>
            {
                if (device is null) return;
                var snapshot = device.GetMemorySnapshot(); report.CudaAfterRuntimeRelease = snapshot;
                if (snapshot.OwnedBytes != 0 || snapshot.OwnedAllocationCount != 0 || snapshot.LoadedModuleCount != 0 ||
                    snapshot.ReleaseFailureCount != 0 || snapshot.PeakOwnedBytes > 3UL * 1024 * 1024 * 1024)
                    throw new InvalidDataException("CUDAowned资源、release failure或峰值边界不符。");
            }, cleanup, report);
            Cleanup("device", () => { if (device is not null) { device.Dispose(); report.DeviceDisposeReturned = true; } }, cleanup, report);
            for (var index = 0; index < leases.Length && index < 7; index++)
            {
                var lease = leases[index];
                Cleanup("file_lease_" + index, () => lease?.Dispose(), cleanup, report);
            }
            if (model is not null) ObserveModel(model, report);
            if (report.CleanupErrors.Count > 0) report.Status = "failed";
            report.ElapsedMilliseconds = budget.Elapsed;
            Console.CancelKeyPress -= cancel;
        }
        try { Publish(options.Output, report); }
        catch (Exception error) { Console.Error.WriteLine("诊断报告写入失败：" + IndependentProfile.Describe(error)); return 2; }
        Console.WriteLine($"{report.Status}: {options.Output}; 零训练更新，正式样本=0，分项未测");
        return report.Status == "diagnostic_complete" ? 0 : 2;
    }

    internal static void ValidateAssets(IndependentEncoderModelPackage model, DiagnosticHeadAsset head, int maxTokens)
    {
        var id = head.Identity;
        if (model.IsDisposed || id.ModelId != model.ModelId || !id.EncoderSha256.Equals(model.EncoderSha256, StringComparison.OrdinalIgnoreCase) ||
            !id.TokenizerSha256.Equals(model.TokenizerSha256, StringComparison.OrdinalIgnoreCase) || id.HiddenSize != model.Encoder.Config.HiddenSize ||
            head.Head.HiddenSize != id.HiddenSize || !IndependentProfile.IsHash(head.FileSha256) ||
            !IndependentProfile.IsHash(head.InitializationDescriptorSha256) || head.Origin != DiagnosticHeadOrigin.UntrainedDiagnostic ||
            head.CompletedUpdates != 0 || head.ObservedLoss is not null || head.InitializationAlgorithm != DiagnosticHeadAssetStore.Algorithm ||
            head.AlgorithmVersion != DiagnosticHeadAssetStore.AlgorithmVersion || head.InitializationDescriptorDomain != DiagnosticHeadAssetStore.DescriptorDomain ||
            id.Protocol != "decision-v1" || id.LengthPolicy != "strict" || maxTokens is < 2 or > 1024 || maxTokens > model.Encoder.Config.MaxTokens)
            throw new InvalidDataException("诊断模型/head完整身份或token预算不符。");
    }

    internal static DiagnosticProfileReport CreateReport(int samples, int warmup, bool fixtureOnly = false)
    {
        if (samples is < 1 or > 5 || warmup is < 0 or > 1) throw new InvalidDataException("计划请求数量越界。");
        var report = new DiagnosticProfileReport { PlannedRequests = samples + warmup, FixtureOnly = fixtureOnly };
        for (var index = 0; index < report.PlannedRequests && index < 6; index++)
            report.Requests.Add(new() { Index = index, Kind = index < warmup ? "warmup" : "diagnostic_sample" });
        return report;
    }

    internal static void BindAssetIdentity(DiagnosticProfileReport report, IndependentEncoderModelPackage model, DiagnosticHeadAsset head)
    {
        report.ModelId = model.ModelId; report.SourceModelId = model.SourceModelId; report.Revision = model.Revision;
        report.DiagnosticHeadIdentity = head.Identity; report.DiagnosticAssetSha256 = head.FileSha256;
        report.InitializationDescriptorSha256 = head.InitializationDescriptorSha256; report.Seed = head.Seed;
        report.InitializationAlgorithm = head.InitializationAlgorithm; report.AlgorithmVersion = head.AlgorithmVersion;
        report.InitializationDescriptorDomain = head.InitializationDescriptorDomain;
    }

    internal static ProfilePreparedSequence[] Discover(IndependentEncoderModelPackage model, IndependentDecisionRequest request,
        int maxTokens, int maxCharacters, ProfileBudget budget, DiagnosticProfileReport report)
    {
        budget.Check();
        if (request.SchemaVersion != 1 || request.ModelId != model.ModelId || request.Questions is null || request.Questions.Count is < 1 or > 32)
            throw new InvalidDataException("独立request身份或1–32题边界不符。");
        var ids = new HashSet<string>(StringComparer.Ordinal); var result = new ProfilePreparedSequence[request.Questions.Count];
        for (var index = 0; index < result.Length && index < 32; index++)
        {
            budget.Check(); var question = request.Questions[index];
            if (question is null || string.IsNullOrWhiteSpace(question.Id) || question.Id.Length > 65536 || !ids.Add(question.Id))
                throw new InvalidDataException("题目ID必须有界且唯一。");
            var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, request, question,
                new IndependentDecisionSequenceOptions { MaxTokens = maxTokens, MaxCharacters = maxCharacters, Deadline = budget.Remaining }, budget.Token);
            var candidates = sequence.CandidateIds ?? throw new InvalidDataException("序列缺少候选ID。");
            result[index] = new(question.Id, sequence.Type, sequence.TokenIds.ToArray(), sequence.MarkerPositions.ToArray(), candidates.ToArray());
            report.Sequences.Add(new(question.Id, IndependentProfile.TypeName(sequence.Type), candidates.ToArray(), sequence.TokenIds.Length,
                IntegerHash(sequence.TokenIds), IntegerHash(sequence.MarkerPositions)));
        }
        report.RequestQuestions = result.Length; report.PlannedQuestions = checked(report.PlannedRequests * result.Length);
        for (var index = 0; index < report.Requests.Count && index < 6; index++) report.Requests[index].PlannedQuestions = result.Length;
        return result;
    }

    internal static void Execute(IndependentEncoderModelPackage model, DiagnosticHeadAsset head, IDiagnosticDecisionPipeline pipeline,
        IndependentDecisionRequest request, ProfilePreparedSequence[] sequences, int samples, int warmup,
        int maxTokens, int maxCharacters, ProfileBudget budget, DiagnosticProfileReport report)
    {
        if (samples is < 1 or > 5 || warmup is < 0 or > 1 || sequences.Length is < 1 or > 32 || report.PlannedRequests != samples + warmup)
            throw new InvalidDataException("执行数量边界或报告分母不符。");
        if (report.Requests.Count != report.PlannedRequests || report.RequestQuestions != sequences.Length ||
            report.Requests.Any(row => row.PlannedQuestions != sequences.Length || row.Status != "not_started"))
            throw new InvalidDataException("计划行或已核题数不符。");
        BindAssetIdentity(report, model, head); report.Backend = pipeline.Backend;
        var observer = new ObservedDiagnosticPipeline(pipeline, sequences, budget);
        DiagnosticDecisionV1Runtime? runtime = null;
        try
        {
            runtime = new(model, head, observer, new IndependentDecisionRuntimeOptions
            { MaxTokens = maxTokens, MaxCharacters = maxCharacters, Deadline = budget.Remaining });
            for (var index = 0; index < report.Requests.Count && index < 6; index++)
            {
                budget.Check(); var row = report.Requests[index]; observer.Begin(row); row.Status = "started";
                try
                {
                    var clock = Stopwatch.StartNew(); DiagnosticDecisionResponse response;
                    try { response = runtime.Evaluate(request, budget.Token); row.TypedRequestReturned = true; }
                    finally { row.E2eMilliseconds = clock.Elapsed.TotalMilliseconds; }
                    budget.Check(); row.Answers = ValidateResponse(response, sequences, model, head, pipeline.Backend);
                    row.ValidatedTypedAnswers = row.Answers.Length; row.Status = "returned";
                }
                catch (Exception error)
                {
                    row.Error = IndependentProfile.Describe(error);
                    row.Status = error is OperationCanceledException or TimeoutException ? "cancelled" : "failed";
                    throw;
                }
            }
            budget.Check(); report.Status = "diagnostic_complete";
        }
        catch (Exception error) { report.Status = "failed"; report.Error = IndependentProfile.Describe(error); }
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

    private static ProfileAnswer[] ValidateResponse(DiagnosticDecisionResponse response, ProfilePreparedSequence[] sequences,
        IndependentEncoderModelPackage model, DiagnosticHeadAsset head, string backend)
    {
        if (response.ModelId != model.ModelId || response.EncoderSha256 != model.EncoderSha256 || response.TokenizerSha256 != model.TokenizerSha256 ||
            response.Protocol != "decision-v1" || response.Backend != backend || response.Status != "untrained-diagnostic" ||
            response.Origin != DiagnosticHeadOrigin.UntrainedDiagnostic || response.CompletedUpdates != 0 || response.ObservedLoss is not null ||
            response.Identity != head.Identity || response.DiagnosticAssetSha256 != head.FileSha256 ||
            response.InitializationDescriptorSha256 != head.InitializationDescriptorSha256 || response.Answers.Count != sequences.Length)
            throw new InvalidDataException("实际诊断typed response身份或数量不符。");
        var result = new ProfileAnswer[sequences.Length];
        for (var index = 0; index < result.Length && index < 32; index++)
        {
            var answer = response.Answers[index]; var expected = sequences[index];
            if (answer.QuestionId != expected.QuestionId || answer.Type != expected.Type || !answer.CandidateIds.SequenceEqual(expected.CandidateIds) ||
                answer.Probabilities.Count != expected.MarkerPositions.Length || answer.SelectedIndex < 0 || answer.SelectedIndex >= answer.Probabilities.Count ||
                answer.Status != "uncalibrated" || !double.IsFinite(answer.Concentration) || answer.Concentration is < 0 or > 1 ||
                answer.Probabilities.Any(value => !double.IsFinite(value) || value is < 0 or > 1) || Math.Abs(answer.Probabilities.Sum() - 1) > 1e-9)
                throw new InvalidDataException("实际诊断typed概率/候选顺序不符。");
            result[index] = new(answer.QuestionId, IndependentProfile.TypeName(answer.Type), answer.CandidateIds.ToArray(), answer.Probabilities.ToArray(),
                answer.SelectedIndex, answer.Concentration, answer.Status);
        }
        return result;
    }

    private static byte[] ReadBound(string path, string expectedHash, int maximum, FileStream?[] leases, int slot,
        DiagnosticProfileReport report, string kind, ProfileBudget budget)
    {
        budget.Check(); var absolute = Path.GetFullPath(path);
        if (slot is < 0 or >= 7 || leases.Length != 7 || leases[slot] is not null) throw new InvalidDataException("文件租约槽位越界或已占用。");
        var file = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read); leases[slot] = file;
        if (file.Length < 1 || file.Length > maximum) throw new InvalidDataException(kind + "字节越界。");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); budget.Check(); var hash = IndependentProfile.Hash(bytes);
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(kind + "原字节SHA不符。");
        report.Artifacts.Add(new(kind, absolute, hash, bytes.Length, File.GetCreationTimeUtc(absolute), File.GetLastWriteTimeUtc(absolute)));
        return bytes;
    }

    private static void AddCodeIdentity(FileStream?[] leases, DiagnosticProfileReport report, ProfileBudget budget)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("无法确定实际进程文件。");
        var paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is null ? new[] { executable } :
            new[] { executable, Path.Combine(AppContext.BaseDirectory, "Sezika.Benchmarks.dll"), Path.Combine(AppContext.BaseDirectory, "Sezika.dll"), Path.Combine(AppContext.BaseDirectory, "Sezika.Cuda.dll") };
        for (var index = 0; index < paths.Length && index < 4; index++)
        {
            budget.Check(); var path = Path.GetFullPath(paths[index]); var slot = index + 3;
            if (leases[slot] is not null) throw new InvalidDataException("执行文件租约槽位已占用。");
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); leases[slot] = file;
            if (file.Length < 1 || file.Length > 64L * 1024 * 1024) throw new InvalidDataException("执行产物字节边界不符。");
            report.Artifacts.Add(new("execution_artifact", path, Convert.ToHexString(SHA256.HashData(file)), file.Length,
                File.GetCreationTimeUtc(path), File.GetLastWriteTimeUtc(path))); budget.Check();
        }
    }

    private static string IntegerHash(int[] values)
    {
        if (values.Length is < 1 or > 1024) throw new InvalidDataException("序列hash数量越界。");
        var bytes = new byte[checked(values.Length * 4)];
        for (var index = 0; index < values.Length && index < 1024; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), values[index]);
        return IndependentProfile.Hash(bytes);
    }

    private static void Cleanup(string name, Action action, Stopwatch clock, DiagnosticProfileReport report)
    {
        if (clock.Elapsed >= TimeSpan.FromSeconds(5)) { report.CleanupErrors.Add(name + ": shared cleanup budget exhausted; not checked"); return; }
        try { action(); if (clock.Elapsed >= TimeSpan.FromSeconds(5)) report.CleanupErrors.Add(name + ": cleanup returned after deadline"); }
        catch (Exception error) { report.CleanupErrors.Add(name + ": " + IndependentProfile.Describe(error)); }
    }

    private static void ObserveModel(IndependentEncoderModelPackage model, DiagnosticProfileReport report)
    {
        report.ModelDisposed = model.IsDisposed; report.ActiveWorkspaces = model.Encoder.WorkspacePool.ActiveCount;
        report.WorkspaceBytes = model.Encoder.WorkspacePool.OutstandingBytes;
        if (!model.IsDisposed || report.ActiveWorkspaces != 0 || report.WorkspaceBytes != 0) report.CleanupErrors.Add("model/workspace release not complete");
    }

    internal static void Publish(string output, DiagnosticProfileReport report, CancellationToken cancellation = default,
        Action<FileStream>? afterCreate = null, Action? afterWrite = null)
    {
        using var audit = new ProfileBudget(5, cancellation); audit.Check();
        var destination = Path.GetFullPath(output); var directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory)) throw new IOException("输出目录不存在。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, DiagnosticProfileJson.Default.DiagnosticProfileReport);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("报告超过4MiB。");
        var partial = Path.Combine(directory, ".independent-diagnostic-profile-" + Guid.NewGuid().ToString("N") + ".partial");
        var created = false; var moved = false; DateTime? createdUtc = null;
        FileStream? owner = null; long? ownedLength = null; byte[]? ownedHash = null;
        Exception? failure = null; var cleanupErrors = new List<Exception>(3);
        try
        {
            owner = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true; createdUtc = File.GetCreationTimeUtc(partial);
            afterCreate?.Invoke(owner);
            owner.Write(bytes); owner.Flush(true); ownedLength = owner.Length;
            owner.Position = 0; ownedHash = SHA256.HashData(owner);
            if (ownedLength != bytes.Length || !ownedHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)))
                throw new IOException("写入的诊断报告字节与序列化结果不符。");
            afterWrite?.Invoke(); audit.Check(); owner.Dispose(); owner = null;
            audit.Check(); File.Move(partial, destination, false); moved = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            var cleanup = Stopwatch.StartNew();
            try
            {
                if (owner is not null)
                {
                    CheckPublicationCleanup(cleanup); ownedLength = owner.Length;
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
            throw new AggregateException("诊断报告发布/partial回收失败；未知对象保留；路径：" + partial, cleanupErrors);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CheckPublicationCleanup(Stopwatch clock)
    { if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("partial独立5秒回收预算耗尽。"); }
}
