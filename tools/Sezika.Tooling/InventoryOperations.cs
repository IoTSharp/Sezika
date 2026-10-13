using System.Text;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record MigrationEntry(string Path, string Tracking, string WorkPackage, string Disposition, string Replacement, string OriginalStatus);
internal sealed record MigrationInventory(string Status, string SourceSha256, int Entries, int Tracked, int IgnoredSamples, int ExternalDependencies, int UserEvidence, MigrationEntry[] Assignments, bool ImplementationsAccepted = false);

internal static class InventoryOperations
{
    internal static MigrationInventory Freeze(string path, Budget budget)
    {
        var absolute = System.IO.Path.GetFullPath(path);
        RegularFiles.Require(absolute, budget);
        using var stream = File.OpenRead(absolute);
        if (stream.Length is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException("Inventory byte bound4MiB.");
        var sha = budget.Hash(stream, stream.Length);
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, leaveOpen: true);
        var header = ParseRow(reader.ReadLine() ?? "");
        string[] expected = ["Path", "Tracking", "Extension", "Lines", "Group", "Risk", "Disposition", "ProposedTarget", "Status"];
        if (!header.SequenceEqual(expected)) throw new InvalidDataException("Inventory schema differs.");
        var entries = new List<MigrationEntry>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < 1025; index++)
        {
            budget.Check();
            var line = reader.ReadLine();
            if (line is null)
            {
                if (entries.Count == 0) throw new InvalidDataException("Empty inventory.");
                return new("PASS_classification_only", sha, entries.Count,
                    entries.Count(e => e.Tracking == "tracked"), entries.Count(e => e.Tracking == "ignored-targeted-sample"),
                    entries.Count(e => e.Tracking == "external-skill"), entries.Count(e => e.Tracking == "untracked-existing"), entries.ToArray());
            }
            if (index == 1024) throw new InvalidDataException("Inventory entry bound1024.");
            var fields = ParseRow(line);
            if (fields.Length != expected.Length || !paths.Add(fields[0])) throw new InvalidDataException("Inventory fields or unique paths differ.");
            if (!int.TryParse(fields[3], out var lines) || lines < 0 || lines > 100000) throw new InvalidDataException("Inventory source line bound.");
            if (fields[1] is not ("tracked" or "ignored-targeted-sample" or "external-skill" or "untracked-existing")) throw new InvalidDataException("Unknown tracking class.");
            if (fields[0].Length is < 1 or > 2048 || fields[0].Contains('\0') || fields[0].Contains("../", StringComparison.Ordinal)) throw new InvalidDataException("Unsafe inventory path.");
            entries.Add(Classify(fields));
        }
        throw new InvalidOperationException("Inventory loop exhausted.");
    }

    internal static string[] ParseRow(string line)
    {
        if (line.Length is < 1 or > 16384) throw new InvalidDataException("Inventory row bound.");
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else { quoted = false; closed = true; }
            }
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); closed = false; }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new InvalidDataException("Malformed inventory CSV quotes.");
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unclosed inventory CSV field.");
        fields.Add(field.ToString());
        return fields.ToArray();
    }

    private static MigrationEntry Classify(string[] row)
    {
        var path = row[0];
        if (row[1] == "untracked-existing" || path.StartsWith("docs/evidence/", StringComparison.Ordinal))
            return new(path, row[1], "T7", "PRESERVE_HISTORICAL_BYTES_REMOVE_CURRENT_CALLERS", "historical-evidence", row[8]);
        if (path.Contains("ssh", StringComparison.OrdinalIgnoreCase) || path.Contains("askpass", StringComparison.OrdinalIgnoreCase) || path.Contains("credential", StringComparison.OrdinalIgnoreCase) || path.EndsWith("Invoke-GitPayload.ps1", StringComparison.Ordinal))
            return new(path, row[1], "T2", "MIGRATE_CAPABILITY_RETIRE_EXECUTABLE_ENTRY", "remote/credentials/git", row[8]);
        if (path.Contains("Bounded", StringComparison.OrdinalIgnoreCase) || path.Contains("guard", StringComparison.OrdinalIgnoreCase) || path.Contains("Processes", StringComparison.OrdinalIgnoreCase) || path.Contains("bounded_start", StringComparison.OrdinalIgnoreCase))
            return new(path, row[1], path.Contains("Audit-Task", StringComparison.Ordinal) ? "T1a" : "T1b", "MIGRATE_CAPABILITY_RETIRE_EXECUTABLE_ENTRY", "process/run/audit", row[8]);
        if (path.EndsWith("Expand-RepositoryArchive.ps1", StringComparison.Ordinal))
            return new(path, row[1], "T1a", "MIGRATE_CAPABILITY_RETIRE_EXECUTABLE_ENTRY", "archive/extract", row[8]);
        if (row[4] is "语料与教师" or "数据集构建候选" || path.Contains("queue", StringComparison.OrdinalIgnoreCase) || path.Contains("source-review", StringComparison.OrdinalIgnoreCase))
            return new(path, row[1], "T5", "MIGRATE_REPRODUCIBLE_DATA_CONTRACT", "corpus/dataset/teacher", row[8]);
        if (row[4] is "独立Oracle" or "校验评测" || path.Contains("test", StringComparison.OrdinalIgnoreCase) || path.Contains("Verify-Candidate", StringComparison.OrdinalIgnoreCase))
            return new(path, row[1], "T6", row[4] == "独立Oracle" ? "FREEZE_INDEPENDENT_REFERENCE_RETIRE_PYTHON_EXECUTION" : "MIGRATE_CHECKS_PRESERVE_FAILURE_RECEIPTS", "validation/checks/oracle-compare", row[8]);
        if (row[4] is "模型打包" or "数据研究候选" or "项目依赖的共享技能" || path.Contains("publish", StringComparison.OrdinalIgnoreCase) || path.Contains("transport", StringComparison.OrdinalIgnoreCase) || path.Contains("mirror", StringComparison.OrdinalIgnoreCase) || path.Contains("lfs", StringComparison.OrdinalIgnoreCase) || path.Contains("moark", StringComparison.OrdinalIgnoreCase) || path.Contains("hf_", StringComparison.OrdinalIgnoreCase) || path.Contains("Model", StringComparison.OrdinalIgnoreCase))
            return new(path, row[1], "T4", "MIGRATE_TRANSFER_OR_PROTOCOL_ARCHIVE_ONE_OFF_PROBES", "transport/publication/platform", row[8]);
        return new(path, row[1], "T3", "MIGRATE_TASK_CONTROL_ARCHIVE_ONE_OFF_REPAIRS", "task/bundle/inputs/checkpoint/resident", row[8]);
    }
}

[JsonSerializable(typeof(MigrationInventory))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class InventoryJsonContext : JsonSerializerContext;
