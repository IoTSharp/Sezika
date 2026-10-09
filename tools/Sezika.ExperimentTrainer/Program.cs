using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;
using Sezika.ExperimentTrainer;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(args.Length > 0 && (args[0] is "train-expanded" or "validate-expanded") ? 1500 : 1200));
ConsoleCancelEventHandler cancel = (_, value) => { value.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    if (args.Length == 0 || args[0] == "help")
    {
        Console.WriteLine("Sezika.ExperimentTrainer train --data <derived-three-file-package> --manifest-sha256 <hash> --package <fixed-mmbert-package> --output <new-directory> [--device 0..15] [--steps 1..768] [--learning-rate (0,0.1]] [--seed <int>] [--training-seconds 1..600] [--timeout-seconds 1..1200] [--max-tokens 64..1024]");
        Console.WriteLine("Sezika.ExperimentTrainer validate --data <same-package> --manifest-sha256 <hash> --package <fixed-mmbert-package> --output <training-directory> --report-sha256 <hash> [--device 0..15] [--timeout-seconds 1..1200]");
        Console.WriteLine("Sezika.ExperimentTrainer check --data <derived-package> --manifest-sha256 <hash> --max-checks <1..8> --artifact-directory <existing-directory>");
        Console.WriteLine("Sezika.ExperimentTrainer check-gpu --device <0..15> --max-checks <1|3> --timeout-seconds <1..60>; synthetic numerical checks only");
        Console.WriteLine("Sezika.ExperimentTrainer lengths --data <derived-package> --manifest-sha256 <hash> --tokenizer <fixed-tokenizer.json> --max-records <1|32>; rendered token lengths only,30seconds");
        Console.WriteLine("Sezika.ExperimentTrainer evaluate --data <same-package> --manifest-sha256 <hash> --package <fixed-package> --output <source-training-directory> --report-sha256 <hash> --evaluation-output <fresh-json-outside-source> [--device 0..15] [--timeout-seconds 1..460]; train24 plus8 fresh fixed-left1 Choice encoder forwards only");
        Console.WriteLine("train --diagnostics fixed-left1 requires --steps768 --seed17 --learning-rate0.001 --max-tokens1024; training<=300seconds/overall<=460seconds; saves separate experiment-learnability-evaluation.json without changing the v1 training report contract");
        Console.WriteLine("validate --diagnostics fixed-left1 additionally requires --evaluation-sha256 <same-process-evaluation-hash> --evaluation-output <fresh-json-outside-source>; fresh disk/encoder/CE/row-output comparison, max72 forwards");
        Console.WriteLine("Sezika.ExperimentTrainer check-evaluation --data <same-package> --manifest-sha256 <hash> --source <old-training-directory> --report-sha256 <hash> --max-checks <1|6>; bounded CPU contracts and synthetic fixtures only,no GPU");
        Console.WriteLine("Explicit teacher-labelled experiment only: human review/license-release/quality/calibration/sealed/AOT/performance/publication remain false. Frozen encoder forward and head CE/SGD use GPU. No resume or CPU encoder fallback.");
        Console.WriteLine("train-expanded --data <explicit-v2-or-repaired-v3-package> --manifest-sha256 <hash> --package <fixed-package> --output <fresh-directory> [--max-tokens <1024|v3:8192>] [--device 0..15] [--timeout-seconds 1..1500]; 24576updates,seed17,LR0.001,SGD<=300s; v2=192train/64dev/272forwards; v3=162train/53dev/231forwards<=384; report v4 binds explicit8192 encoder manifest, historical v2/v3 retain1024");
        Console.WriteLine("validate-expanded --data <same-package> --manifest-sha256 <hash> --package <fixed-package> --output <training-directory> --report-sha256 <hash> --validation-output <fresh-file-outside-source> [--device 0..15] [--timeout-seconds 1..1500]; independent fresh full-data+16forwards and disk/CE/output comparison");
        Console.WriteLine("check-expanded-input --data <v2-or-v3-package> --manifest-sha256 <hash> --fixture-only <true|false> --max-checks <1..16|v3:24>; bounded contracts and commit-order references only,fixture-only never train");
        Console.WriteLine("lengths-expanded --data <v2-or-v3-package> --manifest-sha256 <hash> --tokenizer <fixed-tokenizer.json> --max-records <1|actual-total> [--max-tokens <1024|v3:8192>]; formal strict renderer,120seconds,no encoder/GPU/training");
        return 2;
    }
    if (args[0] == "check") { ExperimentChecks.Run(args, cancellation.Token); return 0; }
    if (args[0] == "check-gpu") { ExperimentChecks.RunGpu(args, cancellation.Token); return 0; }
    if (args[0] == "lengths") { ExperimentChecks.RunLengths(args, cancellation.Token); return 0; }
    if (args[0] == "check-evaluation") { ExperimentEvaluationChecks.Run(args, cancellation.Token); return 0; }
    if (args[0] == "check-expanded-input") { ExpansionChecks.Run(args, cancellation.Token); return 0; }
    if (args[0] == "lengths-expanded") return ExpansionChecks.RunLengths(args, cancellation.Token);
    if (args[0] is "train-expanded" or "validate-expanded") { ExperimentOperations.RunExpanded(args, cancellation.Token); return 0; }
    var options = ExperimentOptions.Parse(args, cancellation.Token);
    using var budget = new ExperimentBudget(options.TimeoutSeconds, cancellation.Token);
    using var data = ExperimentData.Open(options.Data, options.ManifestSha256, budget.Check, budget.Token);
    if (options.Command == "train") ExperimentOperations.Train(options, data, budget);
    else if (options.Command == "validate") ExperimentOperations.Validate(options, data, budget);
    else ExperimentOperations.Evaluate(options, data, budget);
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("experiment: cancelled or bounded deadline exceeded"); return 124; }
catch (Exception error) { Console.Error.WriteLine("experiment: " + error); return 1; }
finally { Console.CancelKeyPress -= cancel; }

namespace Sezika.ExperimentTrainer
{
    internal sealed record ExperimentOptions(string Command, string Data, string ManifestSha256, string Package,
        string Output, string? ReportSha256, int Device, int Steps, float LearningRate, int Seed, int TrainingSeconds, int TimeoutSeconds, int MaxTokens,
        bool Diagnostics = false, string? EvaluationOutput = null, string? EvaluationSha256 = null)
    {
        internal static ExperimentOptions Parse(IReadOnlyList<string> arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (arguments.Count is < 9 or > 31 || arguments[0] is not ("train" or "validate" or "evaluate") || (arguments.Count & 1) == 0)
                throw ExperimentData.Invalid("Expected a bounded train, validate or evaluate command with named pairs.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal); var watch = Stopwatch.StartNew();
            string[] allowed = arguments[0] == "train"
                ? new[] { "--data", "--manifest-sha256", "--package", "--output", "--device", "--steps", "--learning-rate", "--seed", "--training-seconds", "--timeout-seconds", "--max-tokens", "--diagnostics" }
                : arguments[0] == "evaluate"
                ? ["--data", "--manifest-sha256", "--package", "--output", "--report-sha256", "--device", "--timeout-seconds", "--evaluation-output"]
                : ["--data", "--manifest-sha256", "--package", "--output", "--report-sha256", "--device", "--timeout-seconds", "--diagnostics", "--evaluation-sha256", "--evaluation-output"];
            for (var index = 1; index < arguments.Count && index < 31; index += 2)
            {
                token.ThrowIfCancellationRequested();
                if (watch.Elapsed > TimeSpan.FromSeconds(5) || !allowed.Contains(arguments[index], StringComparer.Ordinal) ||
                    string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].Length > 2048 || !values.TryAdd(arguments[index], arguments[index + 1]))
                    throw ExperimentData.Invalid("Unknown, duplicate, empty or oversized option.");
            }
            string Need(string name) => values.TryGetValue(name, out var value) ? value : throw ExperimentData.Invalid("Missing " + name);
            int Number(string name, int minimum, int maximum, int fallback)
            {
                if (!values.TryGetValue(name, out var text)) return fallback;
                if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
                    throw ExperimentData.Invalid("Invalid integer " + name); return value;
            }
            var rate = 0.001f;
            if (values.TryGetValue("--learning-rate", out var rateText) && (!float.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || !float.IsFinite(rate) || rate <= 0 || rate > 0.1f))
                throw ExperimentData.Invalid("Invalid learning rate.");
            var manifestHash = Need("--manifest-sha256"); ExperimentData.RequireHash(manifestHash);
            var reportHash = arguments[0] != "train" ? Need("--report-sha256") : null;
            if (reportHash is not null) ExperimentData.RequireHash(reportHash);
            var diagnostics = values.TryGetValue("--diagnostics", out var diagnosticMode);
            if (diagnostics && diagnosticMode != "fixed-left1") throw ExperimentData.Invalid("Only fixed-left1 diagnostics are supported.");
            var evaluationHash = diagnostics && arguments[0] == "validate" ? Need("--evaluation-sha256") : null;
            if (evaluationHash is not null) ExperimentData.RequireHash(evaluationHash);
            var evaluationOutput = arguments[0] == "evaluate" || (diagnostics && arguments[0] == "validate") ? Path.GetFullPath(Need("--evaluation-output")) : null;
            if (!diagnostics && arguments[0] == "validate" && (values.ContainsKey("--evaluation-sha256") || values.ContainsKey("--evaluation-output")))
                throw ExperimentData.Invalid("Validation evaluation options require explicit fixed-left1 diagnostics.");
            var parsed = new ExperimentOptions(arguments[0], Path.GetFullPath(Need("--data")), manifestHash, Path.GetFullPath(Need("--package")),
                Path.GetFullPath(Need("--output")), reportHash, Number("--device", 0, 15, 0), Number("--steps", 1, 768, 72), rate,
                Number("--seed", int.MinValue, int.MaxValue, 17), Number("--training-seconds", 1, 600, diagnostics ? 300 : 600), Number("--timeout-seconds", 1, 1200, diagnostics || arguments[0] == "evaluate" ? 460 : 1200), Number("--max-tokens", 64, 1024, 256),
                diagnostics, evaluationOutput, evaluationHash);
            if ((diagnostics || arguments[0] == "evaluate") && parsed.TimeoutSeconds > 460) throw ExperimentData.Invalid("Learnability CLI has a460second maximum.");
            if (diagnostics && arguments[0] == "train" && (parsed.Steps != 768 || parsed.Seed != 17 || parsed.LearningRate != 0.001f || parsed.MaxTokens != 1024 || parsed.TrainingSeconds > 300))
                throw ExperimentData.Invalid("Learnability training is fixed at768steps/seed17/LR0.001/strict1024/SGD<=300seconds.");
            return parsed;
        }
    }

    internal sealed class ExperimentBudget : IDisposable
    {
        private readonly CancellationTokenSource _deadline;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Process _process = Process.GetCurrentProcess();
        private double _nextMemoryCheck;
        private readonly int _seconds;
        internal CancellationToken Token => _deadline.Token;
        internal double ElapsedSeconds => _clock.Elapsed.TotalSeconds;
        internal int EncoderForwardLimit { get; set; } = 72;
        internal int EncoderForwardsStarted { get; private set; }
        internal int EncoderForwardsCompleted { get; private set; }
        internal void BeginEncoderForward()
        {
            Check();
            if (EncoderForwardsStarted >= EncoderForwardLimit) throw ExperimentData.Invalid("Explicit encoder forward count budget exhausted.");
            EncoderForwardsStarted++;
        }
        internal void CompleteEncoderForward() { Check(); EncoderForwardsCompleted++; }
        internal ExperimentBudget(int seconds, CancellationToken token)
        {
            _seconds = seconds; _deadline = CancellationTokenSource.CreateLinkedTokenSource(token); _deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        }
        internal void Check()
        {
            Token.ThrowIfCancellationRequested();
            var elapsed = _clock.Elapsed.TotalSeconds;
            if (elapsed >= _seconds) throw new OperationCanceledException("Experiment overall deadline expired.");
            // Cancellation and time are checked on every guard. OS memory queries
            // are sampled to keep contract/token/commit loops within their budget;
            // final reports additionally reject a process peak above4GiB.
            if (elapsed >= _nextMemoryCheck)
            {
                _nextMemoryCheck = elapsed + 0.25;
                _process.Refresh();
                if (_process.WorkingSet64 > 4L * 1024 * 1024 * 1024 || _process.PeakWorkingSet64 > 4L * 1024 * 1024 * 1024)
                    throw ExperimentData.Invalid("Experiment process resident/peak memory exceeds4GiB.");
            }
        }
        public void Dispose() { _deadline.Dispose(); _process.Dispose(); }
    }

    internal static partial class ExperimentOperations
    {
        internal const ulong GpuLimit = 3UL * 1024 * 1024 * 1024;
        internal const string HeadName = "experiment-head.asset";
        internal const string CheckpointName = "experiment-training.checkpoint";
        internal const string ReportName = "experiment-training-report.json";
        private static void Progress(string message) => Console.Error.WriteLine("experiment: " + message);
        private static void CheckGpu(CudaDevice device, ExperimentBudget budget)
        {
            budget.Check(); var memory = device.GetMemorySnapshot();
            if (memory.OwnedBytes > GpuLimit || memory.PeakOwnedBytes > GpuLimit || memory.ReleaseFailureCount != 0)
                throw ExperimentData.Invalid("Owned GPU memory exceeds3GiB or a native release failure was recorded.");
        }
        private static void RequireReleased(CudaDevice device, ExperimentBudget budget)
        {
            CheckGpu(device, budget); var memory = device.GetMemorySnapshot();
            if (memory.OwnedBytes != 0 || memory.OwnedAllocationCount != 0 || memory.LoadedModuleCount != 0)
                throw ExperimentData.Invalid("A completed CUDA stage retained task-owned buffers/modules.");
        }
        private static IndependentEncoderModelPackage LoadModel(string package, ExperimentBudget budget)
        {
            Progress("loading fixed mmBERT package; no CPU encoder forward");
            return IndependentModelLoader.Load(package, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(600) }, budget.Token);
        }
        private static (MarkerFeatureSet Train, MarkerFeatureSet Development) Export(CudaDevice device, IndependentEncoderModelPackage model,
            ExperimentData data, int maxTokens, ExperimentBudget budget, bool includeDevelopment = true)
        {
            var identity = new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, data.ManifestSha256, model.Encoder.Config.HiddenSize);
            var train = new List<MarkerFeatureExample>(24); var development = new List<MarkerFeatureExample>(8);
            using (var encoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, budget.Token))
            {
                CheckGpu(device, budget);
                for (var index = 0; index < 32; index++)
                {
                    CheckGpu(device, budget); var row = data.Records[index];
                    if (!includeDevelopment && row.Split != "train") continue;
                    Progress($"GPU feature export {index + 1}/32 {row.RecordId}");
                    if (row.Request.ModelId != model.ModelId) throw ExperimentData.Invalid("Request student model differs from the actually loaded model.");
                    var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, row.Request, row.Question,
                        new IndependentDecisionSequenceOptions { MaxTokens = maxTokens }, budget.Token);
                    budget.BeginEncoderForward(); var hidden = encoder.Encode(sequence.TokenIds, budget.Token); budget.CompleteEncoderForward();
                    try
                    {
                        if (hidden.Length != checked(sequence.TokenIds.Length * identity.HiddenSize) || hidden.Length > checked(1024 * 2048))
                            throw ExperimentData.Invalid("GPU full-sequence hidden tensor is incomplete or exceeds the verified bound.");
                        for (var offset = 0; offset < hidden.Length && offset < 1024 * 2048; offset++)
                        {
                            if ((offset & 255) == 0) budget.Check();
                            if (!float.IsFinite(hidden[offset])) throw ExperimentData.Invalid("GPU hidden tensor is non-finite.");
                        }
                        var markers = new float[sequence.MarkerPositions.Length][];
                        for (var candidate = 0; candidate < markers.Length && candidate < 32; candidate++)
                        {
                            budget.Check(); markers[candidate] = hidden.AsSpan(sequence.MarkerPositions[candidate] * identity.HiddenSize, identity.HiddenSize).ToArray();
                        }
                        var example = new MarkerFeatureExample(row.RecordId, row.Type, row.Language, sequence.TokenIds, sequence.MarkerPositions, markers, row.TargetIndex);
                        (row.Split == "train" ? train : development).Add(example);
                    }
                    finally { Array.Clear(hidden); }
                    CheckGpu(device, budget);
                }
            }
            RequireReleased(device, budget);
            return (new(identity, train.ToArray()), new(identity, development.ToArray()));
        }
        private static ExperimentLoss Measure(string scope, CudaDevice device, MarkerFeatureSet features, LinearMarkerHead head, ExperimentBudget budget)
        {
            CheckGpu(device, budget);
            var result = IndependentCudaMarkerHeadEvaluator.Evaluate(device, features, head, TimeSpan.FromSeconds(300),
                item => { if (item.CompletedExamples == 0 || item.CompletedExamples == item.ExampleCount) Progress($"CE {scope} {item.CompletedExamples}/{item.ExampleCount}"); }, budget.Token);
            RequireReleased(device, budget); return ExperimentLoss.From(scope, result);
        }
        private static ExperimentInference Infer(CudaDevice device, IndependentEncoderModelPackage model, IndependentMarkerHeadAsset head,
            ExperimentData data, int maxTokens, ExperimentBudget budget)
        {
            var rows = new List<ExperimentPrediction>(8);
            using (var pipeline = new IndependentCudaDecisionPipeline(device, model, head, budget.Token))
            {
                for (var index = 0; index < 32; index++)
                {
                    var row = data.Records[index]; if (row.Split != "development") continue;
                    CheckGpu(device, budget); Progress($"loaded GPU inference {rows.Count + 1}/8 {row.RecordId}");
                    var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, row.Request, row.Question,
                        new IndependentDecisionSequenceOptions { MaxTokens = maxTokens }, budget.Token);
                    budget.BeginEncoderForward(); var logits = pipeline.Score(sequence.TokenIds, sequence.Type, sequence.MarkerPositions, budget.Token); budget.CompleteEncoderForward();
                    if (logits.Length != row.CandidateIds.Length || logits.Any(value => !double.IsFinite(value))) throw ExperimentData.Invalid("Loaded GPU logits are invalid.");
                    var max = logits.Max(); var sum = 0d; var probabilities = new double[logits.Length]; var predicted = 0;
                    for (var candidate = 0; candidate < logits.Length && candidate < 32; candidate++)
                    {
                        budget.Check(); probabilities[candidate] = Math.Exp(logits[candidate] - max); sum += probabilities[candidate];
                        if (logits[candidate] > logits[predicted]) predicted = candidate;
                    }
                    for (var candidate = 0; candidate < probabilities.Length && candidate < 32; candidate++) probabilities[candidate] /= sum;
                    var nll = max + Math.Log(sum) - logits[row.TargetIndex];
                    if (!double.IsFinite(nll) || nll < 0) throw ExperimentData.Invalid("Loaded inference teacher NLL is invalid.");
                    rows.Add(new(row.RecordId, row.Language, row.Type.ToString().ToLowerInvariant(), sequence.CandidateIds, row.TargetIndex,
                        predicted, sequence.CandidateIds[predicted], logits, probabilities, nll, predicted == row.TargetIndex));
                    CheckGpu(device, budget);
                }
            }
            RequireReleased(device, budget);
            if (rows.Count != 8) throw ExperimentData.Invalid("Loaded inference did not complete the full8-row denominator.");
            var agreed = rows.Count(row => row.AgreesWithTeacher);
            return new("cuda-independent-linear-marker-head", "teacher_agreement_on_exposed_unreviewed_development_not_gold_accuracy", 8, 8,
                agreed, agreed / 8d, rows.Average(row => row.TeacherNll), rows.ToArray());
        }
        internal static void Train(ExperimentOptions options, ExperimentData data, ExperimentBudget budget)
        {
            var output = new ExperimentOutput(options.Output, budget.Check); Exception? primary = null;
            try { TrainCore(options, data, budget, output); }
            catch (Exception error) { primary = error; }
            try { output.Dispose(); }
            catch (Exception cleanup)
            {
                if (primary is not null) throw new AggregateException("Experiment training failed and output rollback failed.", primary, cleanup);
                throw;
            }
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
        private static void TrainCore(ExperimentOptions options, ExperimentData data, ExperimentBudget budget, ExperimentOutput output)
        {
            using var model = LoadModel(options.Package, budget); budget.Check();
            using var device = CudaDevice.Open(options.Device); CheckGpu(device, budget);
            var artifacts = CudaTrainingExecutionIdentity.Read(budget.Token);
            var features = Export(device, model, data, options.MaxTokens, budget);
            var zero = new LinearMarkerHead(model.Encoder.Config.HiddenSize, new float[3 * model.Encoder.Config.HiddenSize]);
            var beforeTrain = Measure("baseline_zero_parameters_train24", device, features.Train, zero, budget);
            var beforeDevelopment = Measure("baseline_zero_parameters_development8", device, features.Development, zero, budget);
            Progress($"GPU SGD 0/{options.Steps}; the progress initial loss is not a measured baseline");
            var lastProgress = Stopwatch.StartNew();
            var state = IndependentCudaMarkerHeadTrainer.Train(device, features.Train,
                new(options.Seed, options.Steps, options.LearningRate, TimeSpan.FromSeconds(options.TrainingSeconds)), progress: item =>
                {
                    CheckGpu(device, budget);
                    if (item.CompletedSteps == options.Steps || lastProgress.Elapsed.TotalSeconds >= 5)
                    { Progress($"GPU SGD {item.CompletedSteps}/{item.MaxSteps} pre-update selected-example CE={item.LastLoss:R}"); lastProgress.Restart(); }
                }, cancellationToken: budget.Token);
            RequireReleased(device, budget);
            var weights = state.Head.CopyWeights(); var parameterHash = ExperimentData.Hash(MemoryMarshal.AsBytes(weights.AsSpan()));
            if (state.CompletedSteps != options.Steps || state.CompletedSteps < 1 || !weights.Any(value => value != 0)) throw ExperimentData.Invalid("No real committed weight update was observed.");
            var afterTrain = Measure("trained_parameters_train24", device, features.Train, state.Head, budget);
            var afterDevelopment = Measure("trained_parameters_development8", device, features.Development, state.Head, budget);
            var checkpointHash = MarkerTrainingCheckpoint.SaveNew(output.Checkpoint.Path, state, budget.Token); output.Checkpoint.Acquire(checkpointHash);
            var headHash = IndependentMarkerHeadAssetStore.SaveNew(output.Head.Path, state, budget.Token); output.Head.Acquire(headHash);
            var loadedCheckpoint = MarkerTrainingCheckpoint.Load(output.Checkpoint.Path, features.Train, checkpointHash, budget.Token);
            var loadedHead = IndependentMarkerHeadAssetStore.Load(output.Head.Path, state.Identity, state.FeatureSha256, headHash, budget.Token);
            RequireRoundtrip(state, loadedCheckpoint, loadedHead, parameterHash);
            var loadedTrain = Measure("disk_loaded_parameters_train24", device, features.Train, loadedHead.Head, budget);
            var loadedDevelopment = Measure("disk_loaded_parameters_development8", device, features.Development, loadedHead.Head, budget);
            var inference = Infer(device, model, loadedHead, data, options.MaxTokens, budget);
            var diagnostics = options.Diagnostics ? Observe(device, model, data, features.Train, features.Development, loadedHead, budget, inference) : null;
            var info = device.Info;
            var resources = CloseResources(device, model, budget);
            var report = new ExperimentTrainingReport
            {
                SchemaVersion = 1, TrainingContract = ExperimentTrainingReport.Contract, Status = "completed_experiment_not_formal_acceptance", DataMode = "teacher_labelled_experiment",
                FeatureBackend = "cuda-modernbert-fp32-v1", TrainingBackend = IndependentCudaMarkerHeadTrainer.Backend, EncoderFrozen = true,
                InitialParameterSource = "all_zero_three_linear_rows", Identity = state.Identity, ManifestSha256 = data.ManifestSha256, RecordsSha256 = data.RecordsSha256,
                InputProvenanceSha256 = data.ProvenanceSha256, ActualDataRoot = data.Root,
                OriginalSourceVerification = "historical_source_license_hashes_verified_at_windows_data_preparation; this_execution_verifies_derived_three_file_package_only",
                DataDeclaration = data.Manifest, CudaArtifacts = artifacts,
                Device = new(info.Uuid, info.Name, info.DriverVersion, info.ComputeCapabilityMajor, info.ComputeCapabilityMinor, info.TotalMemoryBytes, device.DeviceIndex),
                Resources = resources,
                Seed = state.Seed, LearningRate = state.LearningRate, CompletedSteps = state.CompletedSteps, StepSemantics = "total_committed_steps_no_resume",
                LastStepPreUpdateSelectedExampleLoss = state.LastLoss, ParametersChangedFromZero = true, TrainCount = 24, DevelopmentCount = 8,
                MaxTokens = options.MaxTokens, TrainingTimeoutSeconds = options.TrainingSeconds, OverallTimeoutSeconds = options.TimeoutSeconds,
                OwnedGpuLimitBytes = (long)GpuLimit, ElapsedSeconds = budget.ElapsedSeconds, FeatureSha256 = state.FeatureSha256,
                CheckpointSha256 = checkpointHash, HeadSha256 = headHash, ParameterSha256 = parameterHash, CheckpointAndHeadRoundtripMatched = true,
                BaselineTrain = beforeTrain, BaselineDevelopment = beforeDevelopment, TrainedTrain = afterTrain, TrainedDevelopment = afterDevelopment,
                LoadedTrain = loadedTrain, LoadedDevelopment = loadedDevelopment, LoadedInference = inference, Acceptance = new(),
            };
            report.Validate();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ExperimentJsonContext.Default.ExperimentTrainingReport);
            if (bytes.Length > 1_048_576) throw ExperimentData.Invalid("Training report exceeds1MiB.");
            var reportHash = ExperimentData.Hash(bytes); output.Report.WriteNew(bytes, reportHash); budget.Check();
            string? evaluationHash = null;
            if (diagnostics is not null)
                evaluationHash = WriteEvaluation(output.Evaluation, MakeEvaluation("new_head_disk_loaded_same_process", reportHash, report, diagnostics, resources, budget), budget);
            Console.WriteLine($"{{\"status\":\"completed_experiment_not_formal_acceptance\",\"report_sha256\":\"{reportHash}\",\"head_sha256\":\"{headHash}\",\"checkpoint_sha256\":\"{checkpointHash}\",\"completed_steps\":{state.CompletedSteps}}}");
            if (evaluationHash is not null) Progress("evaluation_sha256=" + evaluationHash);
            budget.Check(); output.Commit();
        }
        internal static void Validate(ExperimentOptions options, ExperimentData data, ExperimentBudget budget)
        {
            if (options.Diagnostics) { ValidateLearnability(options, data, budget); return; }
            ExperimentData.CheckPath(options.Output, budget.Check);
            var reportPath = Path.Combine(options.Output, ReportName); var headPath = Path.Combine(options.Output, HeadName); var checkpointPath = Path.Combine(options.Output, CheckpointName);
            using var reportLease = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var headLease = new FileStream(headPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var checkpointLease = new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var reportBytes = ExperimentData.Snapshot(reportLease, 1_048_576, budget.Check);
            if (!ExperimentData.SameHash(ExperimentData.Hash(reportBytes), options.ReportSha256)) throw ExperimentData.Invalid("Expected experiment report SHA differs.");
            ExperimentData.CheckJson(reportBytes, 1_048_576, budget.Token);
            var report = JsonSerializer.Deserialize(reportBytes, ExperimentJsonContext.Default.ExperimentTrainingReport) ?? throw ExperimentData.Invalid("Training report is null."); report.Validate();
            if (!ExperimentData.SameHash(report.ManifestSha256, data.ManifestSha256) || !ExperimentData.SameHash(report.RecordsSha256, data.RecordsSha256) ||
                !ExperimentData.SameHash(report.InputProvenanceSha256, data.ProvenanceSha256)) throw ExperimentData.Invalid("Fresh validation data differs from training data.");
            using var model = LoadModel(options.Package, budget); using var device = CudaDevice.Open(options.Device); CheckGpu(device, budget);
            var actualArtifacts = CudaTrainingExecutionIdentity.Read(budget.Token);
            if (device.Info.Uuid != report.Device.Uuid || device.Info.DriverVersion != report.Device.DriverVersion ||
                !ExperimentData.SameHash(actualArtifacts.SourceSha256, report.CudaArtifacts.SourceSha256) || !ExperimentData.SameHash(actualArtifacts.ManifestSha256, report.CudaArtifacts.ManifestSha256) ||
                actualArtifacts.Kernels.Count != report.CudaArtifacts.Kernels.Count) throw ExperimentData.Invalid("Fresh GPU/device/kernel source identity differs.");
            for (var index = 0; index < 17; index++)
            {
                budget.Check(); if (actualArtifacts.Kernels[index] != report.CudaArtifacts.Kernels[index]) throw ExperimentData.Invalid("Fresh static PTX identity differs.");
            }
            var features = Export(device, model, data, report.MaxTokens, budget);
            var loadedCheckpoint = MarkerTrainingCheckpoint.Load(checkpointPath, features.Train, report.CheckpointSha256, budget.Token);
            var loadedHead = IndependentMarkerHeadAssetStore.Load(headPath, features.Train.Identity, report.FeatureSha256, report.HeadSha256, budget.Token);
            RequireRoundtrip(loadedCheckpoint, loadedCheckpoint, loadedHead, report.ParameterSha256);
            if (loadedCheckpoint.CompletedSteps != report.CompletedSteps || loadedCheckpoint.Seed != report.Seed || loadedCheckpoint.LearningRate != report.LearningRate ||
                loadedCheckpoint.LastLoss != report.LastStepPreUpdateSelectedExampleLoss) throw ExperimentData.Invalid("Loaded optimizer metadata differs from report.");
            var trainLoss = Measure("fresh_process_disk_loaded_train24", device, features.Train, loadedHead.Head, budget);
            var developmentLoss = Measure("fresh_process_disk_loaded_development8", device, features.Development, loadedHead.Head, budget);
            var inference = Infer(device, model, loadedHead, data, report.MaxTokens, budget);
            var resources = CloseResources(device, model, budget); budget.Check();
            Console.WriteLine(JsonSerializer.Serialize(new ExperimentValidationReport
            {
                Contract = "sezika/teacher-labelled-gpu-experiment-validation/v1", Status = "completed_experiment_not_formal_acceptance",
                SourceReportSha256 = options.ReportSha256!, HeadSha256 = loadedHead.FileSha256, CheckpointSha256 = report.CheckpointSha256,
                FeatureSha256 = loadedCheckpoint.FeatureSha256, ParameterSha256 = report.ParameterSha256, ActualDataRoot = data.Root,
                LoadedTrain = trainLoss, LoadedDevelopment = developmentLoss, LoadedInference = inference, Acceptance = new(), Resources = resources,
            }, ExperimentJsonContext.Default.ExperimentValidationReport));
        }
        internal static ExperimentResources CloseResources(CudaDevice device, IndependentEncoderModelPackage? model, ExperimentBudget budget)
        {
            RequireReleased(device, budget); var memory = device.GetMemorySnapshot();
            using var process = Process.GetCurrentProcess(); var peak = process.PeakWorkingSet64; var working = process.WorkingSet64;
            if (peak > 4L * 1024 * 1024 * 1024) throw ExperimentData.Invalid("Process peak resident memory exceeded4GiB.");
            device.Dispose(); model?.Dispose(); budget.Check();
            return new(memory, peak, working, true, model is null ? null : true);
        }
        private static void RequireRoundtrip(MarkerTrainingState state, MarkerTrainingState checkpoint, IndependentMarkerHeadAsset head, string expectedParameters)
        {
            if (checkpoint.Identity != state.Identity || head.Identity != state.Identity || checkpoint.CompletedSteps != state.CompletedSteps || head.CompletedSteps != state.CompletedSteps ||
                checkpoint.Seed != state.Seed || head.Seed != state.Seed || checkpoint.LearningRate != state.LearningRate || head.LearningRate != state.LearningRate ||
                checkpoint.LastLoss != state.LastLoss || head.LastLoss != state.LastLoss || !ExperimentData.SameHash(checkpoint.FeatureSha256, state.FeatureSha256) ||
                !ExperimentData.SameHash(head.FeatureSha256, state.FeatureSha256) ||
                !ExperimentData.SameHash(ExperimentData.Hash(MemoryMarshal.AsBytes(checkpoint.Head.CopyWeights().AsSpan())), expectedParameters) ||
                !ExperimentData.SameHash(ExperimentData.Hash(MemoryMarshal.AsBytes(head.Head.CopyWeights().AsSpan())), expectedParameters))
                throw ExperimentData.Invalid("Actual checkpoint/head roundtrip metadata or parameter bytes differs.");
        }
    }

    internal sealed class ExperimentOutput : IDisposable
    {
        private readonly DirectoryInfo _directory;
        private readonly DateTime _creationUtc;
        private bool _committed;
        internal ExperimentOwnedFile Checkpoint { get; }
        internal ExperimentOwnedFile Head { get; }
        internal ExperimentOwnedFile Report { get; }
        internal ExperimentOwnedFile Evaluation { get; }
        internal ExperimentOutput(string directory, Action check)
        {
            directory = Path.GetFullPath(directory); check(); ExperimentData.CheckPath(directory, check);
            if (Directory.Exists(directory) || File.Exists(directory) || !Directory.Exists(Path.GetDirectoryName(directory)))
                throw ExperimentData.Invalid("Output requires a fresh exclusive directory under an existing parent.");
            Checkpoint = new(Path.Combine(directory, ExperimentOperations.CheckpointName)); Head = new(Path.Combine(directory, ExperimentOperations.HeadName)); Report = new(Path.Combine(directory, ExperimentOperations.ReportName));
            Evaluation = new(Path.Combine(directory, ExperimentOperations.EvaluationName));
            _directory = new DirectoryInfo(directory); _directory.Create(); _directory.Refresh(); _creationUtc = _directory.CreationTimeUtc;
        }
        internal void Commit() => _committed = true;
        public void Dispose()
        {
            if (_committed) return;
            var watch = Stopwatch.StartNew(); var errors = new List<Exception>(4);
            void Check() { if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("Experiment output rollback deadline expired."); }
            foreach (var file in new[] { Evaluation, Report, Head, Checkpoint })
            { try { file.Cleanup(Check); } catch (Exception error) { errors.Add(error); } }
            try
            {
                Check(); _directory.Refresh();
                if (_directory.Exists && _directory.CreationTimeUtc == _creationUtc && (_directory.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    !Directory.EnumerateFileSystemEntries(_directory.FullName).Take(5).Any()) { Check(); _directory.Delete(false); Check(); }
            }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Experiment output rollback incomplete; preserved changed or unidentified paths.", errors);
        }
    }
    internal sealed class ExperimentOwnedFile
    {
        internal string Path { get; }
        private readonly FileInfo _info;
        private string? _hash;
        private DateTime? _creationUtc;
        private long _length;
        private readonly int _maximumBytes;
        internal ExperimentOwnedFile(string path, int maximumBytes = 1_048_576)
        {
            if (maximumBytes is < 1 or > 4_194_304) throw ExperimentData.Invalid("Owned output byte bound differs.");
            Path = System.IO.Path.GetFullPath(path); _info = new(Path); _maximumBytes = maximumBytes;
        }
        internal void Acquire(string hash) { _hash = hash; _info.Refresh(); _length = _info.Length; _creationUtc = _info.CreationTimeUtc; }
        internal void WriteNew(byte[] bytes, string hash)
        {
            var errors = new Exception?[3]; FileStream? stream = null;
            try
            {
                stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                _hash = hash; _info.Refresh(); _creationUtc = _info.CreationTimeUtc; _length = bytes.Length;
                stream.Write(bytes); stream.Flush(true);
            }
            catch (Exception error) { errors[0] = error; }
            finally { try { stream?.Dispose(); } catch (Exception error) { errors[1] = error; } }
            if (errors[0] is null && errors[1] is null) return;
            if (_hash is not null)
            {
                try
                {
                    // Independent recovery ignores the failed operation's token. Capture only an
                    // unchanged creation identity whose actual bytes are a prefix of this write.
                    var clock = Stopwatch.StartNew();
                    void Check() { if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new IOException("Report prefix capture recovery deadline expired."); }
                    Check(); ExperimentData.CheckPath(Path, Check); _info.Refresh();
                    if (!_info.Exists || _creationUtc is null || _info.CreationTimeUtc != _creationUtc || _info.Length < 0 || _info.Length > bytes.Length)
                        throw new IOException("Report prefix ownership could not be confirmed; path preserved.");
                    using var input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var prefix = new byte[(int)input.Length]; input.ReadExactly(prefix); Check();
                    if (!prefix.AsSpan().SequenceEqual(bytes.AsSpan(0, prefix.Length))) throw new IOException("Report bytes differ from the owned prefix; preserved.");
                    _length = prefix.Length; _hash = ExperimentData.Hash(prefix); Check();
                }
                catch (Exception error) { errors[2] = error; }
            }
            var collected = new List<Exception>(3);
            for (var index = 0; index < 3; index++) if (errors[index] is { } error) collected.Add(error);
            if (collected.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(collected[0]).Throw();
            throw new AggregateException("Report write/close/prefix recovery failed.", collected);
        }
        internal void Cleanup(Action check)
        {
            if (_hash is null) return; check(); ExperimentData.CheckPath(Path, check); _info.Refresh(); if (!_info.Exists) return;
            if (_creationUtc is null || _info.CreationTimeUtc != _creationUtc || _info.Length != _length || _length > _maximumBytes) throw new IOException("Output ownership differs; file preserved.");
            using (var input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length != _length || input.Length > _maximumBytes) throw new IOException("Output size changed; preserved.");
                var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); check();
                if (!ExperimentData.SameHash(ExperimentData.Hash(bytes), _hash)) throw new IOException("Output hash differs; file preserved.");
            }
            check(); _info.Refresh(); if (!_info.Exists || _info.CreationTimeUtc != _creationUtc || _info.Length != _length) throw new IOException("Output changed before cleanup; preserved.");
            File.Delete(Path); check();
        }
    }
}
