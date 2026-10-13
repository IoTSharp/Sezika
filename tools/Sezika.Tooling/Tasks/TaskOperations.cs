using System.Formats.Tar;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record TaskPreparationRequest(string RunId, string OutputDirectory, string SourceCommit, string DatasetCommit, string ModelCommit, int MaximumSeconds, int TransferReserveSeconds, long TrainRecords, int Epochs, int MaximumTokens, int CheckpointEvery, string Method = "encoder-full");
internal sealed record PreparedTask(string Status, TaskPreparationRequest Contract, DateTimeOffset CreatedUtc, string Training, string Publication, string Server, int GpuConcurrency, bool PurchaseAuthorized, bool QualityAccepted);
internal sealed record BundleFile(string Source, string RelativePath, long Bytes, string Sha256);
internal sealed record BundleRequest(string OutputDirectory, string SourceCommit, string RuntimeIdentifier, BundleFile[] Files);
internal sealed record BundleReceipt(string Status, string SourceCommit, string RuntimeIdentifier, BundleFile[] Files, string ManifestSha256, bool ServerExecutionAccepted = false);
internal sealed record PackRequest(string SourceDirectory, string DestinationArchive);
internal sealed record PackReceipt(string Status, string Archive, long Bytes, string Sha256, int Files);
internal sealed record CheckpointInputContract(string PlanSha256, string DataManifestSha256, string RecordsSha256, string BaseEncoderSha256, string TokenizerSha256, string OrderSha256, string BackwardManifestSha256);
internal sealed record CheckpointVerificationRequest(string Directory, string PlanPath, string CheckpointSha256, string CursorSha256, CheckpointInputContract Inputs);
internal sealed record CheckpointVerificationReceipt(string Status, string RunId, long CompletedUpdates, long PlannedUpdates, string WeightsSha256, string MomentsSha256, string CheckpointSha256, string CursorSha256, bool OptimizerLoaded = false, bool GpuRun = false);
internal sealed record HostInspectionRequest(string OwnedDirectory, long RequiredFreeBytes);
internal sealed record HostInspectionReceipt(string Status, string Platform, string Architecture, int CpuCount, long AvailableDiskBytes, string? BootId, string GpuAdmission, bool ServerAccepted = false);

internal static class TaskOperations
{
    internal const string ScheduleAlgorithm = "source_round_robin_v1;source_order=sha256(seed,epoch,source);within_source=manifest_shard_row_order;train_only;no_oversampling";
    internal static PreparedTask Prepare(TaskPreparationRequest request, Budget budget)
    {
        budget.Check();
        if (request.RunId.Length is < 3 or > 64 || request.RunId.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')) throw new InvalidDataException("Invalid run identity.");
        Commit(request.SourceCommit); Commit(request.DatasetCommit); Commit(request.ModelCommit);
        if (request.MaximumSeconds is < 60 or > 86400 || request.TransferReserveSeconds is < 1 || request.TransferReserveSeconds >= request.MaximumSeconds || request.TrainRecords is < 1 or > 30000000 || request.Epochs is < 1 or > 64 || checked(request.TrainRecords * request.Epochs) > 800000000 || request.MaximumTokens is < 2 or > 8192 || request.CheckpointEvery is < 1 or > 1000000 || request.Method != "encoder-full") throw new InvalidDataException("Task budget, full corpus or method contract differs.");
        var directory = FreshDirectory(request.OutputDirectory, budget);
        var result = new PreparedTask("PREPARED_configuration_only", request, DateTimeOffset.UtcNow, "NOT_RUN", "NOT_RUN", "NOT_RUN", 1, false, false);
        WriteNew(Path.Combine(directory, "task.json"), result, TaskJsonContext.Default.PreparedTask);
        // Relative STOP/CANCEL names are consumed by the controller and trainer;
        // preparation never clears an existing marker or contacts a server.
        File.WriteAllText(Path.Combine(directory, "OWNED_BY_SEZIKA_REPOSITORY_TASK"), request.RunId + "\n");
        return result;
    }

    internal static BundleReceipt Freeze(BundleRequest request, Budget budget)
    {
        Commit(request.SourceCommit);
        if (request.RuntimeIdentifier is not ("linux-x64" or "win-x64") || request.Files.Length is < 1 or > 998) throw new InvalidDataException("Bundle runtime or file bound998 plus2 manifests differs.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in request.Files)
        {
            budget.Check();
            var name = SafePaths.Relative(file.RelativePath);
            if (name == "files.sha256" || name == "engineering-sources.json" || !names.Add(name) || IsScript(name)) throw new InvalidDataException("Script, reserved manifest or duplicate bundle path.");
            Sha(file.Sha256);
            if (file.Bytes < 0 || file.Bytes > ArchiveOperations.MaximumBytes || (total = checked(total + file.Bytes)) > ArchiveOperations.MaximumBytes) throw new InvalidDataException("Bundle byte bound16GiB.");
            VerifyFile(file.Source, file.Bytes, file.Sha256, budget);
        }
        var root = FreshDirectory(request.OutputDirectory, budget);
        foreach (var file in request.Files)
        {
            var destination = SafePaths.Within(root, file.RelativePath, budget);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyNew(file.Source, destination, file.Bytes, budget);
            VerifyFile(destination, file.Bytes, file.Sha256, budget);
        }
        WriteNew(Path.Combine(root, "engineering-sources.json"), request, TaskJsonContext.Default.BundleRequest);
        var lines = request.Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).Select(f => f.Sha256 + "  " + f.RelativePath).ToList();
        var sourceManifest = Path.Combine(root, "engineering-sources.json");
        using (var source = File.OpenRead(sourceManifest)) lines.Add(budget.Hash(source, source.Length) + "  engineering-sources.json");
        var manifest = Path.Combine(root, "files.sha256");
        File.WriteAllLines(manifest, lines.OrderBy(l => l[66..], StringComparer.Ordinal), new System.Text.UTF8Encoding(false));
        using var stream = File.OpenRead(manifest);
        return new("PASS_frozen_bundle_bytes_only", request.SourceCommit, request.RuntimeIdentifier, request.Files, budget.Hash(stream, stream.Length));
    }

    internal static PackReceipt Pack(PackRequest request, Budget budget)
    {
        var root = Path.GetFullPath(request.SourceDirectory);
        var destination = Path.GetFullPath(request.DestinationArchive);
        SafePaths.NoLinks(root, budget); SafePaths.NoLinks(destination, budget);
        if (!Directory.Exists(root) || Path.Exists(destination) || !Directory.Exists(Path.GetDirectoryName(destination))) throw new IOException("Pack input or fresh output differs.");
        if (destination.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Archive must be outside its source directory.");
        var entries = ManifestFiles(root, budget);
        // Partial archives remain clearly named on failure. No user object is removed.
        var partial = destination + ".partial";
        SafePaths.NoLinks(partial, budget);
        if (Path.Exists(partial)) throw new IOException("Partial archive exists; preserve it.");
        using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = new TarWriter(output, leaveOpen: true))
                foreach (var entry in entries)
                {
                    budget.Check();
                    var path = SafePaths.Within(root, entry, budget);
                    RegularFiles.Require(path, budget);
                    using var input = File.OpenRead(path);
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entry) { DataStream = input });
                    if (output.Length > ArchiveOperations.MaximumBytes) throw new InvalidDataException("Archive bytes exceed16GiB.");
                }
            output.Flush(true);
        }
        // Recheck every source SHA after reading, before atomically accepting the TAR.
        _ = ManifestFiles(root, budget);
        using var archive = File.OpenRead(partial);
        var sha = budget.Hash(archive, archive.Length);
        var bytes = archive.Length;
        archive.Dispose();
        File.Move(partial, destination, false);
        return new("PASS_complete_manifest_tar", destination, bytes, sha, entries.Length);
    }

    internal static CheckpointVerificationReceipt VerifyCheckpoint(CheckpointVerificationRequest request, Budget budget)
    {
        Sha(request.CheckpointSha256); Sha(request.CursorSha256);
        var root = Path.GetFullPath(request.Directory);
        var checkpointPath = SafePaths.Within(root, "checkpoint.json", budget);
        var cursorPath = SafePaths.Within(root, "corpus-cursor.json", budget);
        VerifyFile(checkpointPath, new FileInfo(checkpointPath).Length, request.CheckpointSha256, budget);
        VerifyFile(cursorPath, new FileInfo(cursorPath).Length, request.CursorSha256, budget);
        using var checkpoint = ReadDocument(checkpointPath, budget);
        using var cursor = ReadDocument(cursorPath, budget);
        using var plan = ReadDocument(request.PlanPath, budget);
        var cp = checkpoint.RootElement; var c = cursor.RootElement; var p = plan.RootElement;
        var run = p.GetProperty("RunId").GetString() ?? throw new InvalidDataException("Run missing.");
        var records = p.GetProperty("TrainRecords").GetInt64(); var epochs = p.GetProperty("Epochs").GetInt32();
        var updates = cp.GetProperty("CompletedUpdates").GetInt64();
        var planned = checked(records * epochs);
        if (records is < 1 or > 30000000 || epochs is < 1 or > 64 || planned > 800000000 || updates is < 1 || updates > planned || cp.GetProperty("SchemaVersion").GetInt32() != 1 || c.GetProperty("SchemaVersion").GetInt32() != 1 || cp.GetProperty("RunId").GetString() != run || c.GetProperty("RunId").GetString() != run || c.GetProperty("CompletedUpdates").GetInt64() != updates || c.GetProperty("TrainingRecordsPerEpoch").GetInt64() != records || c.GetProperty("PlannedEpochs").GetInt32() != epochs || c.GetProperty("Epoch").GetInt64() != updates / records || c.GetProperty("WithinEpoch").GetInt64() != updates % records || c.GetProperty("CheckpointReceiptSha256").GetString() != request.CheckpointSha256 || !JsonElement.DeepEquals(cp.GetProperty("Inputs"), c.GetProperty("Inputs"))) throw new InvalidDataException("Checkpoint/cursor/global plan identity differs.");
        var inputs = cp.GetProperty("Inputs");
        foreach (var property in new[] { "SourceCommit", "DatasetCommit", "ModelCommit" }) Commit(p.GetProperty(property).GetString()!);
        var expectedInputs = JsonSerializer.SerializeToElement(request.Inputs, TaskJsonContext.Default.CheckpointInputContract);
        if (!JsonElement.DeepEquals(inputs, expectedInputs)) throw new InvalidDataException("Complete pinned checkpoint input identity differs.");
        VerifyFile(request.PlanPath, new FileInfo(request.PlanPath).Length, request.Inputs.PlanSha256, budget);
        if (inputs.GetProperty("DataManifestSha256").GetString() != p.GetProperty("CorpusManifestSha256").GetString()) throw new InvalidDataException("Corpus manifest identity differs.");
        if (c.GetProperty("ScheduleAlgorithm").GetString() != ScheduleAlgorithm) throw new InvalidDataException("Corpus schedule algorithm differs.");
        var orderBytes = System.Text.Encoding.UTF8.GetBytes(ScheduleAlgorithm + "\n" + p.GetProperty("Seed").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + epochs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + records.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + request.Inputs.DataManifestSha256);
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(orderBytes)) != request.Inputs.OrderSha256) throw new InvalidDataException("Global schedule order identity differs.");
        if (c.TryGetProperty("Schedule", out var schedule) && schedule.ValueKind != JsonValueKind.Null)
        {
            CheckpointScheduleValidation.Validate(schedule, updates, records, epochs);
            using var compact = new MemoryStream();
            using (var writer = new Utf8JsonWriter(compact, new JsonWriterOptions { Indented = false }))
                JsonSerializer.Serialize(writer, schedule, TaskJsonContext.Default.JsonElement);
            var bytes = compact.ToArray();
            if (cp.GetProperty("CorpusScheduleSha256").GetString() != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) || schedule.GetProperty("Epoch").GetInt64() != updates / records || schedule.GetProperty("WithinEpoch").GetInt64() != updates % records) throw new InvalidDataException("Streaming shard cursor SHA or position differs.");
        }
        else if (cp.TryGetProperty("CorpusScheduleSha256", out var scheduleSha) && scheduleSha.ValueKind != JsonValueKind.Null) throw new InvalidDataException("Checkpoint streaming cursor is missing.");
        var weights = cp.GetProperty("WeightsSha256").GetString()!; var moments = cp.GetProperty("MomentsSha256").GetString()!;
        Sha(weights); Sha(moments);
        foreach (var file in new[] { ("weights.safetensors", weights), ("moments.safetensors", moments) })
        {
            var path = SafePaths.Within(root, file.Item1, budget);
            VerifyFile(path, new FileInfo(path).Length, file.Item2, budget);
        }
        VerifyFile(checkpointPath, new FileInfo(checkpointPath).Length, request.CheckpointSha256, budget);
        VerifyFile(cursorPath, new FileInfo(cursorPath).Length, request.CursorSha256, budget);
        return new("PASS_four_file_sha_cursor_only", run, updates, planned, weights, moments, request.CheckpointSha256, request.CursorSha256);
    }

    internal static HostInspectionReceipt InspectHost(HostInspectionRequest request, Budget budget)
    {
        var root = Path.GetFullPath(request.OwnedDirectory);
        SafePaths.NoLinks(root, budget);
        if (!Directory.Exists(root) || request.RequiredFreeBytes < 0) throw new InvalidDataException("Host inspection owned directory required.");
        var disk = new DriveInfo(root).AvailableFreeSpace;
        if (disk < request.RequiredFreeBytes) throw new IOException("Host disk reserve insufficient.");
        string? boot = null;
        if (OperatingSystem.IsLinux())
        {
            boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
            if (!Guid.TryParse(boot, out _)) throw new InvalidDataException("Invalid boot identity.");
        }
        budget.Check();
        return new("PASS_host_disk_identity_only", System.Runtime.InteropServices.RuntimeInformation.OSDescription, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount, disk, boot, "NOT_RUN");
    }

    internal static bool IsScript(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ps1" or ".sh" or ".py" or ".cmd" or ".bat";
    internal static void Sha(string value) { if (value.Length != 64 || value.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))) throw new InvalidDataException("Pinned lowercase SHA256 required."); }
    internal static void Commit(string value) { if (value.Length != 40 || value.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))) throw new InvalidDataException("Pinned lowercase commit required."); }
    internal static JsonDocument ReadDocument(string path, Budget budget)
    {
        RegularFiles.Require(path, budget);
        using var input = File.OpenRead(path);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds16MiB.");
        budget.Check();
        return JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false });
    }
    internal static void VerifyFile(string path, long bytes, string sha, Budget budget)
    {
        Sha(sha); RegularFiles.Require(path, budget);
        using var stream = File.OpenRead(path);
        if (stream.Length != bytes || budget.Hash(stream, bytes) != sha) throw new InvalidDataException("File bytes/SHA differ.");
    }
    private static string FreshDirectory(string path, Budget budget)
    {
        var result = Path.GetFullPath(path); SafePaths.NoLinks(result, budget);
        if (Path.Exists(result)) throw new IOException("Output directory exists; preserve it.");
        Directory.CreateDirectory(result); return result;
    }
    private static void CopyNew(string source, string destination, long bytes, Budget budget)
    {
        RegularFiles.Require(source, budget);
        using var input = File.OpenRead(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[1048576];
        for (long block = 0, maximum = bytes / buffer.Length + 2; block < maximum; block++)
        {
            budget.Check();
            var read = input.Read(buffer);
            if (read == 0) { if (output.Length != bytes) throw new InvalidDataException("Copy denominator differs."); output.Flush(true); return; }
            if (output.Length + read > bytes) throw new InvalidDataException("Source grew during copy.");
            output.Write(buffer, 0, read);
        }
        throw new InvalidDataException("Copy block bound reached.");
    }
    private static string[] ManifestFiles(string root, Budget budget)
    {
        var manifest = SafePaths.Within(root, "files.sha256", budget);
        if (new FileInfo(manifest).Length > 1048576) throw new InvalidDataException("Manifest byte bound1MiB.");
        var rows = File.ReadLines(manifest).Take(1001).ToArray();
        if (rows.Length is < 1 or > 1000) throw new InvalidDataException("Manifest file bound1000.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            budget.Check();
            if (row.Length < 67 || row.Substring(64, 2) != "  ") throw new InvalidDataException("Invalid SHA manifest row.");
            var name = SafePaths.Relative(row[66..]);
            if (name == "files.sha256" || !names.Add(name) || IsScript(name)) throw new InvalidDataException("Reserved/duplicate/script payload.");
            var path = SafePaths.Within(root, name, budget);
            VerifyFile(path, new FileInfo(path).Length, row[..64], budget);
        }
        // Bound traversal to this explicit owned directory. Extra bytes cannot be
        // silently omitted from a complete model or tooling archive.
        var pending = new Queue<(string Directory, int Depth)>(); pending.Enqueue((root, 0));
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        for (var directory = 0; directory < 1001 && pending.Count != 0; directory++)
        {
            var item = pending.Dequeue();
            if (item.Depth > 64) throw new InvalidDataException("Package depth bound64.");
            foreach (var path in Directory.EnumerateFileSystemEntries(item.Directory))
            {
                budget.Check();
                if (++count > 2000) throw new InvalidDataException("Package entry bound2000.");
                SafePaths.NoLinks(path, budget);
                if (Directory.Exists(path)) { pending.Enqueue((path, item.Depth + 1)); continue; }
                var name = SafePaths.Relative(Path.GetRelativePath(root, path).Replace('\\', '/'));
                if (name != "files.sha256" && !names.Contains(name)) throw new InvalidDataException("Unmanifested package file.");
                found.Add(name);
            }
        }
        if (pending.Count != 0 || found.Count != rows.Length + 1) throw new InvalidDataException("Package manifest coverage differs.");
        return names.Append("files.sha256").Order(StringComparer.Ordinal).ToArray();
    }
    internal static void WriteNew<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, value, type); output.Flush(true);
    }
}

[JsonSerializable(typeof(TaskPreparationRequest))]
[JsonSerializable(typeof(PreparedTask))]
[JsonSerializable(typeof(BundleRequest))]
[JsonSerializable(typeof(BundleReceipt))]
[JsonSerializable(typeof(PackRequest))]
[JsonSerializable(typeof(PackReceipt))]
[JsonSerializable(typeof(CheckpointVerificationRequest))]
[JsonSerializable(typeof(CheckpointInputContract))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CheckpointVerificationReceipt))]
[JsonSerializable(typeof(HostInspectionRequest))]
[JsonSerializable(typeof(HostInspectionReceipt))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class TaskJsonContext : JsonSerializerContext;
