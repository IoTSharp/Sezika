using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sezika.ExperimentTrainer;

internal static class ExperimentChecks
{
    internal static void RunLengths(string[] arguments, CancellationToken token)
    {
        if (arguments.Length != 9) throw new ArgumentException("lengths requires --data --manifest-sha256 --tokenizer --max-records.");
        using var budget = new ExperimentBudget(30, token);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < 9; index += 2)
        {
            budget.Check();
            if (arguments[index] is not ("--data" or "--manifest-sha256" or "--tokenizer" or "--max-records") ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].Length > 2048 ||
                !values.TryAdd(arguments[index], arguments[index + 1])) throw new ArgumentException("Unknown, duplicate or invalid lengths option.");
        }
        if (!int.TryParse(values["--max-records"], NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) || maximum is not (1 or 32))
            throw new ArgumentException("lengths max-records must be1or32.");
        var tokenizerPath = Path.GetFullPath(values["--tokenizer"]);
        ExperimentData.CheckPath(tokenizerPath, budget.Check);
        ExperimentLengthReport report;
        using (var data = ExperimentData.Open(values["--data"], values["--manifest-sha256"], budget.Check, budget.Token))
        using (var lease = new FileStream(tokenizerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            const int tokenizerBytes = 17_525_329;
            var bytes = ExperimentData.Snapshot(lease, tokenizerBytes, budget.Check);
            var tokenizerHash = ExperimentData.Hash(bytes);
            if (bytes.Length != tokenizerBytes || !ExperimentData.SameHash(tokenizerHash, Sezika.IndependentModelLoader.TokenizerSha256))
                throw ExperimentData.Invalid("Length measurement requires the actual fixed mmBERT tokenizer bytes/hash.");
            Array.Clear(bytes);
            Console.Error.WriteLine("experiment lengths: loading pinned tokenizer only; no encoder/GPU/features/training");
            var tokenizer = new Sezika.TokenizerJson(tokenizerPath, budget.Token); budget.Check();
            if (tokenizer.BosId != 2 || tokenizer.EosId != 1 || tokenizer.MaskId != 4)
                throw ExperimentData.Invalid("Length measurement tokenizer special IDs differ.");
            var rows = new List<ExperimentLengthRow>(maximum);
            for (var index = 0; index < maximum && index < 32; index++)
            {
                budget.Check(); var row = data.Records[index];
                Console.Error.WriteLine($"experiment lengths {index + 1}/{maximum}: {row.RecordId}");
                var sequence = Sezika.IndependentDecisionV1SequenceBuilder.Build(tokenizer, row.Request, row.Question,
                    new Sezika.IndependentDecisionSequenceOptions { MaxTokens = 1024 }, budget.Token);
                if (sequence.TokenIds.Length is < 2 or > 1024 || !sequence.CandidateIds.SequenceEqual(row.CandidateIds, StringComparer.Ordinal))
                    throw ExperimentData.Invalid("Length measurement sequence exceeds the verified bound or changes candidate order.");
                rows.Add(new(row.RecordId, row.Language, row.Split, row.Type.ToString().ToLowerInvariant(),
                    sequence.TokenIds.Length, sequence.MarkerPositions, sequence.CandidateIds));
            }
            budget.Check(); lease.Position = 0;
            var finalBytes = ExperimentData.Snapshot(lease, tokenizerBytes, budget.Check);
            if (finalBytes.Length != tokenizerBytes || !ExperimentData.SameHash(ExperimentData.Hash(finalBytes), tokenizerHash))
                throw ExperimentData.Invalid("Tokenizer bytes changed during length measurement.");
            Array.Clear(finalBytes); budget.Check();
            using var process = Process.GetCurrentProcess();
            if (process.PeakWorkingSet64 > 4L * 1024 * 1024 * 1024) throw ExperimentData.Invalid("Length measurement process peak exceeded4GiB.");
            report = new("sezika/teacher-labelled-rendered-token-lengths/v1", "completed_rendered_lengths_no_model_execution",
                Sezika.IndependentModelLoader.ModelId, data.ManifestSha256, data.RecordsSha256, data.ProvenanceSha256,
                tokenizerHash, data.Root, tokenizerPath, 32, maximum, rows.Count, 1024, rows.Max(row => row.TokenCount), 30,
                budget.ElapsedSeconds, false, false, false, false, process.PeakWorkingSet64, rows.ToArray());
        }
        budget.Check();
        Console.WriteLine(JsonSerializer.Serialize(report, ExperimentJsonContext.Default.ExperimentLengthReport));
    }

    internal static void RunGpu(string[] arguments, CancellationToken token)
    {
        if (arguments.Length != 7) throw new ArgumentException("check-gpu requires --device --max-checks --timeout-seconds.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var parseClock = Stopwatch.StartNew();
        for (var index = 1; index < 7; index += 2)
        {
            token.ThrowIfCancellationRequested();
            if (parseClock.Elapsed > TimeSpan.FromSeconds(5) || arguments[index + 1].Length > 2048 ||
                arguments[index] is not ("--device" or "--max-checks" or "--timeout-seconds") || !values.TryAdd(arguments[index], arguments[index + 1]))
                throw new ArgumentException("Unknown or duplicate GPU check option.");
        }
        if (!int.TryParse(values["--device"], out var deviceIndex) || deviceIndex is < 0 or > 15 ||
            !int.TryParse(values["--max-checks"], out var maximum) || maximum is not (1 or 3) ||
            !int.TryParse(values["--timeout-seconds"], out var seconds) || seconds is < 1 or > 60)
            throw new ArgumentException("GPU check budget requires device0..15/checks1or3/seconds1..60.");
        using var budget = new ExperimentBudget(seconds, token); using var device = Sezika.Cuda.CudaDevice.Open(deviceIndex);
        double error;
        if (maximum == 3)
        {
            var result = Sezika.Cuda.IndependentCudaMarkerHeadEvaluationChecks.Run(device, budget.Token);
            if (result.PassedChecks != 3) throw new InvalidOperationException("The full GPU numerical check denominator differs.");
            error = result.MaximumLossAbsoluteError;
        }
        else
        {
            var identity = new Sezika.MarkerFeatureIdentity("sezika/gpu-evaluator-tiny-fixture", new string('A', 64), new string('B', 64), new string('C', 64), 2);
            Sezika.MarkerFeatureExample example = new("tiny-choice", Sezika.MarkerQuestionType.Choice, "en", [1, 2, 3], [0, 2], [[1, 0], [0, 1]], 0);
            var result = Sezika.Cuda.IndependentCudaMarkerHeadEvaluator.Evaluate(device, new(identity, [example]), new(2, new float[6]), TimeSpan.FromSeconds(10), cancellationToken: budget.Token);
            error = Math.Abs(result.MeanLoss - Math.Log(2));
            if (result.CompletedExamples != 1 || result.ExampleCount != 1 || error > 2e-6 || result.ParameterSha256 != result.DeviceParameterSha256)
                throw new InvalidOperationException("Tiny actual GPU CE/parameter check failed.");
        }
        var resources = ExperimentOperations.CloseResources(device, null, budget);
        Console.WriteLine(JsonSerializer.Serialize(new ExperimentGpuCheckReport("passed_synthetic_gpu_numeric_checks_only", maximum, error, true, false, resources),
            ExperimentJsonContext.Default.ExperimentGpuCheckReport));
    }

    internal static void Run(string[] arguments, CancellationToken token)
    {
        if (arguments.Length != 9) throw new ArgumentException("check requires --data --manifest-sha256 --max-checks --artifact-directory.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var clock = Stopwatch.StartNew(); token = deadline.Token;
        void Check() { token.ThrowIfCancellationRequested(); if (clock.Elapsed.TotalSeconds > 30) throw new OperationCanceledException("Experiment checks deadline."); }
        for (var index = 1; index < 9; index += 2)
        {
            Check(); if (arguments[index] is not ("--data" or "--manifest-sha256" or "--max-checks" or "--artifact-directory") ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) || !values.TryAdd(arguments[index], arguments[index + 1])) throw new ArgumentException("Unknown/duplicate check option.");
        }
        if (!int.TryParse(values["--max-checks"], NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) || maximum is < 1 or > 8)
            throw new ArgumentException("max-checks must be1..8.");
        var artifact = Path.GetFullPath(values["--artifact-directory"]);
        if (!Directory.Exists(artifact)) throw new ArgumentException("An existing explicit artifact directory is required."); ExperimentData.CheckPath(artifact, Check);
        using var data = ExperimentData.Open(values["--data"], values["--manifest-sha256"], Check, token);
        // These are CPU input-contract checks over the already generated real pilot package.
        // They neither compute encoder features nor train/evaluate a model.
        var originalLines = File.ReadAllLines(Path.Combine(data.Root, "experiment-records.jsonl"));
        var cases = new (string Name, Action Action)[]
        {
            ("real-three-file-experiment-package-and-24-8-denominators", () =>
            {
                Assert(data.Records.Length == 32 && data.Records.Count(row => row.Split == "train") == 24 && data.Records.Count(row => row.Split == "development") == 8, "Complete32/24/8 denominators.");
                Assert(!data.Manifest.Formal && data.Manifest.HumanReview == "pending" && !data.Manifest.TeacherOutputTrainingPermitted && data.Manifest.UserAuthorizedExperiment, "Pending source declaration retained independently of experiment authorization.");
            }),
            ("strict-command-defaults-and-bounded-optional-values", Arguments),
            ("duplicate-record-and-family-rejected", () =>
            {
                Rejects(() => Parse(Mutate(1, row => new() { RecordId = data.Records[0].RecordId, FamilyId = row.FamilyId, Language = row.Language, Split = row.Split, TargetIndex = row.TargetIndex, Request = row.Request })));
                Rejects(() => Parse(Mutate(1, row => new() { RecordId = row.RecordId, FamilyId = data.Records[0].FamilyId, Language = row.Language, Split = row.Split, TargetIndex = row.TargetIndex, Request = row.Request })));
            }),
            ("teacher-target-and-predeclared-split-rejected", () =>
            {
                Rejects(() => Parse(Mutate(0, row => new() { RecordId = row.RecordId, FamilyId = row.FamilyId, Language = row.Language, Split = row.Split, TargetIndex = 99, Request = row.Request })));
                Rejects(() => Parse(Mutate(0, row => new() { RecordId = row.RecordId, FamilyId = row.FamilyId, Language = row.Language, Split = "train", TargetIndex = row.TargetIndex, Request = row.Request })));
            }),
            ("unknown-fields-and-duplicate-json-rejected", () =>
            {
                var unknown = (string[])originalLines.Clone(); unknown[0] = "{\"human_approved\":true," + unknown[0][1..]; Rejects(() => Parse(Encoding.UTF8.GetBytes(string.Join('\n', unknown))));
                Rejects(() => ExperimentData.CheckJson("{\"x\":0,\"x\":1}"u8, 65_536, token));
            }),
            ("count-byte-and-json-bounds", () =>
            {
                Rejects(() => Parse(Encoding.UTF8.GetBytes(string.Join('\n', originalLines.Take(31)))));
                Rejects(() => Parse(Encoding.UTF8.GetBytes(string.Join('\n', originalLines.Concat([originalLines[0]])))));
                Rejects(() => ExperimentData.CheckJson(new byte[65_537], 65_536, token));
            }),
            ("cancelled-input-work-acquires-no-model-or-device", () =>
            {
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { ExperimentData.ParseRecords(Encoding.UTF8.GetBytes(string.Join('\n', originalLines)), data.Manifest, () => cancelled.Token.ThrowIfCancellationRequested(), cancelled.Token); }
                catch (OperationCanceledException) { return; }
                throw new InvalidOperationException("Input cancellation was ignored.");
            }),
            ("formal-acceptance-flags-remain-rejected", () =>
            {
                Rejects(() => new ExperimentAcceptance { HumanQuality = true }.Validate());
                Rejects(() => new ExperimentAcceptance { FormalTrainingAdmission = true }.Validate());
                Rejects(() => new ExperimentAcceptance { LicenseRelease = true }.Validate());
                Rejects(() => new ExperimentAcceptance { SealedTest = true }.Validate());
                Rejects(() => new ExperimentAcceptance { Calibration = true }.Validate());
                new ExperimentAcceptance().Validate();
            }),
        };
        for (var index = 0; index < maximum && index < 8; index++)
        {
            Check(); Console.WriteLine($"experiment check {index + 1}/{maximum}: {cases[index].Name}"); cases[index].Action(); Console.WriteLine("PASS: " + cases[index].Name);
        }
        Console.WriteLine($"PASS: {maximum} bounded input-contract checks; no model load/encoder forward/GPU/training/quality acceptance; no temporary fixture files created");

        void Parse(byte[] bytes) => _ = ExperimentData.ParseRecords(bytes, data.Manifest, Check, token);
        byte[] Mutate(int index, Func<ExperimentRecordJson, ExperimentRecordJson> modify)
        {
            var lines = (string[])originalLines.Clone();
            var row = JsonSerializer.Deserialize(lines[index], ExperimentJsonContext.Default.ExperimentRecordJson) ?? throw new InvalidOperationException();
            // Default source context indents report/data DTOs; a single JSONL mutation needs a compact writer.
            using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
                JsonSerializer.Serialize(writer, modify(row), ExperimentJsonContext.Default.ExperimentRecordJson);
            lines[index] = Encoding.UTF8.GetString(stream.ToArray()); return Encoding.UTF8.GetBytes(string.Join('\n', lines));
        }
        void Arguments()
        {
            string[] valid = ["train", "--data", data.Root, "--manifest-sha256", data.ManifestSha256, "--package", artifact, "--output", Path.Combine(artifact, "never-created")];
            var parsed = ExperimentOptions.Parse(valid, token);
            Assert(parsed.Steps == 72 && parsed.Seed == 17 && parsed.LearningRate == 0.001f && parsed.TrainingSeconds == 600 && parsed.TimeoutSeconds == 1200 && parsed.MaxTokens == 256, "Explicit bounded training defaults.");
            Assert(ExperimentOptions.Parse([.. valid, "--max-tokens", "1024"], token).MaxTokens == 1024, "Verified1024token upper bound accepted.");
            Assert(ExperimentOptions.Parse([.. valid, "--max-tokens", "64"], token).MaxTokens == 64, "64token lower bound accepted.");
            Rejects(() => ExperimentOptions.Parse([.. valid, "--max-tokens", "1025"], token));
            Rejects(() => ExperimentOptions.Parse([.. valid, "--max-tokens", "63"], token));
            Rejects(() => ExperimentOptions.Parse([.. valid, "--steps", "769"], token));
            Rejects(() => ExperimentOptions.Parse([.. valid, "--training-seconds", "601"], token));
            Rejects(() => ExperimentOptions.Parse([.. valid, "--timeout-seconds", "1201"], token));
            Rejects(() => ExperimentOptions.Parse([.. valid, "--resume-checkpoint", "forbidden"], token));
            Rejects(() => ExperimentOptions.Parse([.. valid, "--learning-rate", "NaN"], token));
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Rejects(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is Sezika.DecisionException or ArgumentException or JsonException or IOException) { return; }
        throw new InvalidOperationException("Invalid experiment input was not rejected.");
    }
}
