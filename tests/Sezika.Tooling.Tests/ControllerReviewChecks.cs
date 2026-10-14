using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class ControllerReviewChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Budget Limit() => new(25, null, timeout.Token);
        static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        { try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException) { return; } throw new InvalidOperationException("Expected review fixture rejection."); }
        string PathFor(string name) => Path.Combine(root, "controller-review-" + name);
        void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
        CheckpointVerificationRequest Checkpoint(string name, string schedule)
        {
            timeout.Token.ThrowIfCancellationRequested(); var directory = PathFor(name); Directory.CreateDirectory(directory); var planPath = Path.Combine(directory, "plan.json"); var manifest = new string('d', 64);
            Write(planPath, "{\"RunId\":\"simulation\",\"TrainRecords\":2,\"Epochs\":1,\"Seed\":17,\"SourceCommit\":\"" + new string('a', 40) + "\",\"DatasetCommit\":\"" + new string('b', 40) + "\",\"ModelCommit\":\"" + new string('c', 40) + "\",\"CorpusManifestSha256\":\"" + manifest + "\"}");
            var inputs = new CheckpointInputContract(CorpusJson.FileHash(planPath, Limit()), manifest, new string('e', 64), new string('f', 64), new string('a', 64), Hash(TaskOperations.ScheduleAlgorithm + "\n17\n1\n2\n" + manifest), new string('b', 64));
            var encodedInputs = JsonSerializer.Serialize(inputs, TaskJsonContext.Default.CheckpointInputContract);
            // Synthetic identity bytes only. These files deliberately are not
            // real tensor assets, and no optimizer, CUDA or model is loaded.
            File.WriteAllBytes(Path.Combine(directory, "weights.safetensors"), [1]); File.WriteAllBytes(Path.Combine(directory, "moments.safetensors"), [2]);
            var checkpoint = "{\"SchemaVersion\":1,\"RunId\":\"simulation\",\"CompletedUpdates\":1,\"Inputs\":" + encodedInputs + ",\"WeightsSha256\":\"" + CorpusJson.FileHash(Path.Combine(directory, "weights.safetensors"), Limit()) + "\",\"MomentsSha256\":\"" + CorpusJson.FileHash(Path.Combine(directory, "moments.safetensors"), Limit()) + "\",\"CorpusScheduleSha256\":\"" + Hash(schedule) + "\"}";
            var checkpointPath = Path.Combine(directory, "checkpoint.json"); Write(checkpointPath, checkpoint); var checkpointHash = CorpusJson.FileHash(checkpointPath, Limit());
            var cursor = "{\"SchemaVersion\":1,\"RunId\":\"simulation\",\"CompletedUpdates\":1,\"Epoch\":0,\"WithinEpoch\":1,\"TrainingRecordsPerEpoch\":2,\"PlannedEpochs\":1,\"CheckpointReceiptSha256\":\"" + checkpointHash + "\",\"ScheduleAlgorithm\":\"" + TaskOperations.ScheduleAlgorithm + "\",\"Inputs\":" + encodedInputs + ",\"Schedule\":" + schedule + "}";
            Write(Path.Combine(directory, "corpus-cursor.json"), cursor);
            return new(directory, planPath, checkpointHash, CorpusJson.FileHash(Path.Combine(directory, "corpus-cursor.json"), Limit()), inputs);
        }
        const string correctSchedule = "{\"Epoch\":0,\"WithinEpoch\":1,\"Round\":1,\"NextSource\":0,\"Sources\":[{\"Source\":\"simulation\",\"Shard\":0,\"Row\":1,\"Accepted\":1}]}";
        check("Simulation compact streaming cursor uses original nonindented SHA", () =>
        {
            var input = Checkpoint("compact-cursor", correctSchedule); var result = TaskOperations.VerifyCheckpoint(input, Limit());
            Assert(result.CompletedUpdates == 1 && !result.GpuRun && !result.OptimizerLoaded && result.Status == "PASS_four_file_sha_cursor_only", "A valid compact cursor or Simulation evidence scope changed.");
        });
        check("Simulation structurally invalid streaming next-source is rejected", () =>
        {
            var input = Checkpoint("invalid-next-source", correctSchedule.Replace("\"NextSource\":0", "\"NextSource\":99", StringComparison.Ordinal));
            Reject(() => TaskOperations.VerifyCheckpoint(input, Limit()));
        });
        check("Simulation structurally invalid streaming accepted denominator is rejected", () =>
        {
            var input = Checkpoint("invalid-accepted", correctSchedule.Replace("\"Accepted\":1", "\"Accepted\":2", StringComparison.Ordinal));
            Reject(() => TaskOperations.VerifyCheckpoint(input, Limit()));
        });
        check("Simulation existing STOP preserved before any driver or trainer launch", () =>
        {
            var directory = PathFor("stop"); Directory.CreateDirectory(directory); Write(Path.Combine(directory, "OWNED_BY_SEZIKA_REPOSITORY_TASK"), "simulation\n"); Write(Path.Combine(directory, "STOP"), "human stop retained\n");
            var image = Environment.ProcessPath ?? throw new InvalidOperationException("Native host missing."); var plan = Path.Combine(directory, "plan.json"); var profile = Path.Combine(directory, "profile.json"); Write(plan, "{}"); Write(profile, "{}");
            var request = new ResidentRequest(directory, image, CorpusJson.FileHash(image, Limit()), plan, CorpusJson.FileHash(plan, Limit()), profile, CorpusJson.FileHash(profile, Limit()), new string('a', 32), image, CorpusJson.FileHash(image, Limit()), 1, 60, 1);
            var cancelled = false;
            try { ResidentController.RunAsync(request, new Budget(120, null, timeout.Token), timeout.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && File.ReadAllText(Path.Combine(directory, "STOP")) == "human stop retained\n" && Directory.EnumerateDirectories(directory).Take(2).Count() == 0, "Existing STOP was altered or caused a launch/output stage.");
        });
        check("Simulation reviewed native hook directory rejects other non-native hook", () =>
        {
            var directory = PathFor("hooks"); Directory.CreateDirectory(directory); var hook = Path.Combine(directory, "pre-push"); File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Native host missing."), hook);
            NativeGitHook.VerifyDirectory(directory, CorpusJson.FileHash(hook, Limit()), Limit());
            Write(Path.Combine(directory, "pre-commit"), "Simulation: unreviewed non-native hook data; never executed\n");
            Reject(() => NativeGitHook.VerifyDirectory(directory, CorpusJson.FileHash(hook, Limit()), Limit()));
        });
        check("Simulation frozen-bundle byte pins reject changed engineering source", () =>
        {
            var path = PathFor("source.txt"); Write(path, "frozen source\n"); var bytes = new FileInfo(path).Length; var sha = CorpusJson.FileHash(path, Limit()); Write(path, "changed source\n");
            Reject(() => TaskOperations.Freeze(new(PathFor("bundle"), new string('a', 40), OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", [new(path, "source.txt", bytes, sha)]), Limit())); Assert(!Directory.Exists(PathFor("bundle")), "Changed source created accepted frozen bundle.");
        });
        check("Local LFS pointer is exactly canonical and round-trips zero and maximum size", () =>
        {
            var oid = new string('a', 64);
            var pointer = NativeLfsFiles.CreatePointer(42, oid);
            Assert(Encoding.ASCII.GetString(pointer) == "version https://git-lfs.github.com/spec/v1\noid sha256:" + oid + "\nsize 42\n", "Canonical pointer bytes changed.");
            Assert(NativeLfsFiles.TryParsePointer(pointer, out var parsed, out var size) && parsed == oid && size == 42, "Canonical pointer did not round-trip.");
            foreach (var value in new[] { 0L, long.MaxValue })
                Assert(NativeLfsFiles.TryParsePointer(NativeLfsFiles.CreatePointer(value, oid), out parsed, out size) && parsed == oid && size == value, "Canonical boundary size did not round-trip.");
        });
        check("Local LFS pointer rejects CR decimal aliases extra lines and malformed SHA", () =>
        {
            var valid = Encoding.ASCII.GetString(NativeLfsFiles.CreatePointer(42, new string('a', 64)));
            var invalid = new[]
            {
                valid.Replace("\n", "\r\n", StringComparison.Ordinal),
                valid.Replace("size 42", "size 042", StringComparison.Ordinal),
                valid.Replace("size 42", "size +42", StringComparison.Ordinal),
                valid.Replace("size 42", "size -1", StringComparison.Ordinal),
                valid.Replace("size 42", "size 9223372036854775808", StringComparison.Ordinal),
                valid.Replace("oid sha256:a", "oid sha256:A", StringComparison.Ordinal),
                valid.Replace("oid sha256:a", "oid sha256:g", StringComparison.Ordinal),
                valid + "extension bad\n", valid.TrimEnd('\n'),
                valid.Replace("size 42", "size 42\nsize 42", StringComparison.Ordinal),
                new string('x', NativeLfsFiles.MaximumPointerBytes + 1)
            };
            foreach (var candidate in invalid)
            {
                timeout.Token.ThrowIfCancellationRequested();
                Assert(!NativeLfsFiles.TryParsePointer(Encoding.ASCII.GetBytes(candidate), out var oid, out var size) && oid == "" && size == 0, "Noncanonical pointer accepted or leaked partial parse output.");
            }
            Reject(() => NativeLfsFiles.CreatePointer(-1, new string('a', 64)));
            Reject(() => NativeLfsFiles.CreatePointer(1, new string('A', 64)));
        });
        check("Local LFS store independently verifies bytes and reuses a correct object", () =>
        {
            var git = PathFor("lfs-valid"); Directory.CreateDirectory(git); var source = PathFor("lfs-source.bin");
            File.WriteAllBytes(source, [0, 1, 2, 127, 128, 255]); var sha = CorpusJson.FileHash(source, Limit());
            var stored = NativeLfsFiles.StoreObject(git, source, 6, sha, Limit());
            Assert(!stored.Reused && stored.Bytes == 6 && stored.Sha256 == sha && File.ReadAllBytes(stored.Path).SequenceEqual(File.ReadAllBytes(source)), "Stored object identity or bytes differ.");
            var reused = NativeLfsFiles.StoreObject(git, source, 6, sha, Limit());
            Assert(reused.Reused && reused.Path == stored.Path && CorpusJson.FileHash(reused.Path, Limit()) == sha, "Existing correct object was not independently verified and reused.");
            Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(stored.Path)!, "*.partial").Take(1).Any(), "Successful local copy retained a partial.");
        });
        check("Local LFS store preserves and rejects an incorrect existing object", () =>
        {
            var git = PathFor("lfs-bad-existing"); Directory.CreateDirectory(git); var source = PathFor("lfs-existing-source.bin"); File.WriteAllBytes(source, [4, 5, 6]);
            var sha = CorpusJson.FileHash(source, Limit()); var existing = Path.Combine(git, "lfs", "objects", sha[..2], sha.Substring(2, 2), sha);
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!); File.WriteAllBytes(existing, [7, 8, 9]);
            Reject(() => NativeLfsFiles.StoreObject(git, source, 3, sha, Limit()));
            Assert(File.ReadAllBytes(existing).SequenceEqual(new byte[] { 7, 8, 9 }), "Incorrect existing object was overwritten.");
        });
        check("Local LFS wrong source pins reject before object-store mutation", () =>
        {
            var git = PathFor("lfs-bad-source"); Directory.CreateDirectory(git); var source = PathFor("lfs-wrong-source.bin"); File.WriteAllBytes(source, [10, 11]);
            var sha = CorpusJson.FileHash(source, Limit());
            Reject(() => NativeLfsFiles.StoreObject(git, source, 3, sha, Limit()));
            Reject(() => NativeLfsFiles.StoreObject(git, source, 2, new string('0', 64), Limit()));
            Assert(!Directory.EnumerateFileSystemEntries(git).Take(1).Any(), "Rejected source pin mutated the object store.");
        });
        check("Local LFS cancellation rejects before any object-store mutation", () =>
        {
            var git = PathFor("lfs-cancelled"); Directory.CreateDirectory(git); var source = PathFor("lfs-cancelled-source.bin"); File.WriteAllBytes(source, [12]);
            var sha = CorpusJson.FileHash(source, Limit());
            Reject(() => NativeLfsFiles.StoreObject(git, source, 1, sha, new Budget(25, null, new CancellationToken(canceled: true))));
            Assert(!Directory.EnumerateFileSystemEntries(git).Take(1).Any(), "Pre-existing cancellation mutated the object store.");
        });
        check("Simulation schedule requires numeric JSON integer fields", () =>
        {
            using var document = JsonDocument.Parse(correctSchedule.Replace("\"Accepted\":1", "\"Accepted\":\"1\"", StringComparison.Ordinal));
            Reject(() => CheckpointScheduleValidation.Validate(document.RootElement, 1, 2, 1));
        });
        check("Simulation final schedule requires empty sources zero next and round", () =>
        {
            const string final = "{\"Epoch\":1,\"WithinEpoch\":0,\"Round\":0,\"NextSource\":0,\"Sources\":[]}";
            using var accepted = JsonDocument.Parse(final); CheckpointScheduleValidation.Validate(accepted.RootElement, 2, 2, 1);
            using var rejected = JsonDocument.Parse(final.Replace("\"Round\":0", "\"Round\":1", StringComparison.Ordinal));
            Reject(() => CheckpointScheduleValidation.Validate(rejected.RootElement, 2, 2, 1));
        });
    }
}
