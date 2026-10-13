using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class TaskChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        Budget Limit() => new(20, null, CancellationToken.None);
        static void Require(bool value) { if (!value) throw new InvalidDataException("Task contract fixture differs."); }
        static void Reject(Action value) { try { value(); } catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or OperationCanceledException or TimeoutException) { return; } throw new InvalidDataException("Expected task rejection."); }
        string Sha(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
        var commit = new string('a', 40);
        var request = new TaskPreparationRequest("task-fixture", Path.Combine(root, "task-prepared"), commit, commit, commit, 600, 60, 14592720, 1, 8192, 4096);
        check("task prepared contract is configuration only", () =>
        {
            var result = TaskOperations.Prepare(request, Limit());
            Require(result.Contract.TrainRecords == 14592720 && result.Contract.CheckpointEvery == 4096 && result.Training == "NOT_RUN" && !result.PurchaseAuthorized && !result.QualityAccepted && result.GpuConcurrency == 1);
        });
        check("task existing configuration preserved", () => Reject(() => TaskOperations.Prepare(request, Limit())));
        check("task immutable global update and save reserve bounds", () =>
        {
            Reject(() => TaskOperations.Prepare(request with { OutputDirectory = Path.Combine(root, "task-bad-total"), TrainRecords = 30000000, Epochs = 64 }, Limit()));
            Reject(() => TaskOperations.Prepare(request with { OutputDirectory = Path.Combine(root, "task-bad-reserve"), TransferReserveSeconds = 600 }, Limit()));
            Reject(() => TaskOperations.Prepare(request with { OutputDirectory = Path.Combine(root, "task-bad-pin"), SourceCommit = "main" }, Limit()));
        });
        var source = Path.Combine(root, "task-source"); Directory.CreateDirectory(source);
        var payload = Encoding.UTF8.GetBytes("C# tooling immutable fixture\n"); File.WriteAllBytes(Path.Combine(source, "tool.bin"), payload);
        var bundle = new BundleRequest(Path.Combine(root, "task-bundle"), commit, "linux-x64", [new(Path.Combine(source, "tool.bin"), "tool/tool.bin", payload.Length, Sha(payload))]);
        check("bundle verifies original and independently copied bytes", () =>
        {
            var result = TaskOperations.Freeze(bundle, Limit());
            Require(result.Files.Length == 1 && !result.ServerExecutionAccepted && File.ReadAllBytes(Path.Combine(bundle.OutputDirectory, "tool", "tool.bin")).SequenceEqual(payload));
        });
        check("bundle rejects script payload and changed source SHA", () =>
        {
            Reject(() => TaskOperations.Freeze(bundle with { OutputDirectory = Path.Combine(root, "task-script"), Files = [bundle.Files[0] with { RelativePath = "hidden.sh" }] }, Limit()));
            Reject(() => TaskOperations.Freeze(bundle with { OutputDirectory = Path.Combine(root, "task-tampered"), Files = [bundle.Files[0] with { Sha256 = new string('0', 64) }] }, Limit()));
        });
        var tar = Path.Combine(root, "task-bundle.tar");
        check("C# TAR pack and independent manifest extraction", () =>
        {
            var result = TaskOperations.Pack(new(bundle.OutputDirectory, tar), Limit());
            Require(result.Files == 3);
            var extracted = ArchiveOperations.Extract(tar, result.Sha256, Path.Combine(root, "task-bundle-readback"), Limit());
            Require(extracted.Files == 3 && File.ReadAllBytes(Path.Combine(extracted.Directory, "tool", "tool.bin")).SequenceEqual(payload));
        });
        check("pack rejects silent omissions and preserves existing archive", () =>
        {
            Reject(() => TaskOperations.Pack(new(bundle.OutputDirectory, tar), Limit()));
            File.WriteAllText(Path.Combine(bundle.OutputDirectory, "unlisted.bin"), "extra");
            Reject(() => TaskOperations.Pack(new(bundle.OutputDirectory, Path.Combine(root, "task-unlisted.tar")), Limit()));
        });
        check("host inspect keeps GPU and server acceptance NOT_RUN", () =>
        {
            var result = TaskOperations.InspectHost(new(source, 0), Limit());
            Require(result.AvailableDiskBytes > 0 && result.GpuAdmission == "NOT_RUN" && !result.ServerAccepted);
        });
        var checkpointRoot = Path.Combine(root, "task-checkpoint"); Directory.CreateDirectory(checkpointRoot);
        var planPath = Path.Combine(root, "task-plan.json");
        var planJson = "{\"RunId\":\"task-fixture\",\"TrainRecords\":10,\"Epochs\":2,\"Seed\":17,\"CorpusManifestSha256\":\"" + new string('b', 64) + "\",\"SourceCommit\":\"" + commit + "\",\"DatasetCommit\":\"" + commit + "\",\"ModelCommit\":\"" + commit + "\"}";
        File.WriteAllText(planPath, planJson);
        var order = Sha(Encoding.UTF8.GetBytes(TaskOperations.ScheduleAlgorithm + "\n17\n2\n10\n" + new string('b', 64)));
        var inputs = new CheckpointInputContract(Sha(File.ReadAllBytes(planPath)), new string('b', 64), new string('c', 64), new string('d', 64), new string('e', 64), order, new string('f', 64));
        // Tiny bytes represent transport identity only. The receipt explicitly
        // leaves optimizer loading and GPU acceptance false.
        var weights = new byte[] { 1, 2, 3 }; var moments = new byte[] { 4, 5, 6 };
        File.WriteAllBytes(Path.Combine(checkpointRoot, "weights.safetensors"), weights);
        File.WriteAllBytes(Path.Combine(checkpointRoot, "moments.safetensors"), moments);
        var serializedInputs = JsonSerializer.Serialize(inputs, TaskJsonContext.Default.CheckpointInputContract);
        File.WriteAllText(Path.Combine(checkpointRoot, "checkpoint.json"), "{\"SchemaVersion\":1,\"RunId\":\"task-fixture\",\"CompletedUpdates\":11,\"Inputs\":" + serializedInputs + ",\"WeightsSha256\":\"" + Sha(weights) + "\",\"MomentsSha256\":\"" + Sha(moments) + "\"}");
        var cpSha = Sha(File.ReadAllBytes(Path.Combine(checkpointRoot, "checkpoint.json")));
        File.WriteAllText(Path.Combine(checkpointRoot, "corpus-cursor.json"), "{\"SchemaVersion\":1,\"RunId\":\"task-fixture\",\"CompletedUpdates\":11,\"TrainingRecordsPerEpoch\":10,\"PlannedEpochs\":2,\"Epoch\":1,\"WithinEpoch\":1,\"CheckpointReceiptSha256\":\"" + cpSha + "\",\"Inputs\":" + serializedInputs + ",\"ScheduleAlgorithm\":\"" + TaskOperations.ScheduleAlgorithm + "\"}");
        var cursorSha = Sha(File.ReadAllBytes(Path.Combine(checkpointRoot, "corpus-cursor.json")));
        var verification = new CheckpointVerificationRequest(checkpointRoot, planPath, cpSha, cursorSha, inputs);
        check("checkpoint four-file identity and global cursor", () =>
        {
            var result = TaskOperations.VerifyCheckpoint(verification, Limit());
            Require(result.CompletedUpdates == 11 && result.PlannedUpdates == 20 && !result.OptimizerLoaded && !result.GpuRun);
        });
        check("checkpoint plan pin tamper rejected", () => Reject(() => TaskOperations.VerifyCheckpoint(verification with { Inputs = inputs with { PlanSha256 = new string('0', 64) } }, Limit())));
        check("checkpoint moments changed and missing rejected", () =>
        {
            File.WriteAllBytes(Path.Combine(checkpointRoot, "moments.safetensors"), [9, 9, 9]);
            Reject(() => TaskOperations.VerifyCheckpoint(verification, Limit()));
            // Keep the failed fixture rather than delete a checkpoint artifact.
        });
    }
}
