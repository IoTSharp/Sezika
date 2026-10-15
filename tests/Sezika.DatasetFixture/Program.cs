using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;

return FixtureEmitter.Run(args);

internal static class FixtureEmitter
{
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private const string SourceId = "ZefanCai/Open-Jev";
    private static readonly UTF8Encoding Utf8 = new(false);

    // Exactly two local literals. No upstream data, loader or network is used.
    private const string CreateRows = """
        CREATE TABLE fixture_rows (
          metadata_json VARCHAR, state_json VARCHAR, question VARCHAR,
          kind VARCHAR, options VARCHAR[], target DOUBLE[], group_id VARCHAR);
        INSERT INTO fixture_rows VALUES
          ('{"language":"en","synthetic_fixture":true}',
           '{"fixture_state":"alpha"}', 'Choose the declared synthetic option.',
           'choice', ['no','yes'], [0.75,0.25], 'literal-alpha'),
          ('{"language":"zh","synthetic_fixture":true}',
           '{"fixture_state":"beta"}', '选择固定的合成选项。',
           'choice', ['甲','乙'], [0.2,0.8], 'literal-beta');
        """;

    // Independently authored semantic oracle, never derived from decoded output.
    // CorpusWriter may rotate candidates: compare probability by candidate text.
    private static readonly ExpectedSemantic[] Expected =
    [
        new("literal-alpha", "en", "Choice", ["no", "yes"], [0.75, 0.25],
            "synthetic_reference", "source_synthetic_target_preserved_not_real_accuracy"),
        new("literal-beta", "zh", "Choice", ["甲", "乙"], [0.2, 0.8],
            "synthetic_reference", "source_synthetic_target_preserved_not_real_accuracy")
    ];

    internal static int Run(string[] args)
    {
        if (args.Length is not (2 or 4 or 6) || args[0] != "--output")
        {
            Console.Error.WriteLine("--output <new-owned-directory> [--timeout-seconds 1..60] [--cancel-file <absolute-path>]");
            return 2;
        }

        var seconds = 30;
        string? cancelFile = null;
        var sawTimeout = false;
        var optionClock = Stopwatch.StartNew();
        try
        {
            for (var index = 2; index < args.Length && index < 6; index += 2)
            {
                if (optionClock.Elapsed.TotalSeconds >= 2) throw new TimeoutException("Option parse bound.");
                if (args[index] == "--timeout-seconds" && !sawTimeout)
                {
                    sawTimeout = true;
                    seconds = int.Parse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture);
                    if (seconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(args));
                }
                else if (args[index] == "--cancel-file" && cancelFile is null)
                    cancelFile = Absolute(args[index + 1]);
                else throw new ArgumentException("Unknown or repeated option.");
            }
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or TimeoutException)
        {
            Console.Error.WriteLine("fixture options: " + error.Message);
            return 2;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var watch = Stopwatch.StartNew();
        void Check()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (watch.Elapsed.TotalSeconds >= seconds) throw new TimeoutException("Fixture wall bound.");
            if (cancelFile is not null && File.Exists(cancelFile)) throw new OperationCanceledException("Fixture cancel marker.");
        }
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        string? ownedRoot = null;
        try
        {
            Check();
            var root = Absolute(args[1]);
            var parent = Directory.GetParent(root) ?? throw new ArgumentException("Output parent required.");
            if (!parent.Exists) throw new DirectoryNotFoundException("Existing output parent required.");
            DirectoryInfo? ancestor = parent;
            for (var depth = 0; depth < 32 && ancestor is not null; depth++)
            {
                Check();
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0 || ancestor.LinkTarget is not null)
                    throw new IOException("Output ancestors must not be links.");
                ancestor = ancestor.Parent;
            }
            if (ancestor is not null) throw new IOException("Output ancestor depth32 bound.");
            if (Path.Exists(root)) throw new IOException("Preserve existing output.");
            Directory.CreateDirectory(root);
            ownedRoot = root;
            WriteNew(Path.Combine(root, "OWNED_BY_DATASET_FIXTURE"), Utf8.GetBytes("synthetic-parquet-literals-v1\n"));
            var parquet = Path.Combine(root, "open-jev-train.parquet");

            using (var connection = new DuckDBConnection("Data Source=:memory:"))
            {
                connection.Open();
                Execute(connection, "SET threads=1; SET memory_limit='128MB'; SET autoinstall_known_extensions=false",
                    seconds, cancellation.Token, Check);
                Execute(connection, CreateRows, seconds, cancellation.Token, Check);
                // Only the fresh, fixed filename is escaped into COPY; no supplied SQL.
                var escaped = parquet.Replace("'", "''", StringComparison.Ordinal);
                Execute(connection, "COPY fixture_rows TO '" + escaped + "' (FORMAT PARQUET, COMPRESSION UNCOMPRESSED)",
                    seconds, cancellation.Token, Check);
                Check();
                using var count = connection.CreateCommand();
                count.CommandText = "SELECT count(*) FROM read_parquet(?)";
                count.CommandTimeout = Math.Min(seconds, 10);
                count.Parameters.Add(new DuckDBParameter { Value = parquet });
                using var registration = cancellation.Token.Register(count.Cancel);
                if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) != 2)
                    throw new InvalidDataException("Expected exactly two emitted rows.");
            }

            Check();
            var parquetInfo = new FileInfo(parquet);
            if (parquetInfo.Length is < 12 or > MaximumFileBytes) throw new InvalidDataException("Parquet byte bound.");
            var parquetSha = Hash(parquet, Check);
            var oracleBytes = JsonSerializer.SerializeToUtf8Bytes(Expected, FixtureJson.Default.ExpectedSemanticArray);
            WriteNew(Path.Combine(root, "expected-open-jev-semantics.json"), oracleBytes);
            var receipt = new FixtureReceipt("READY_SYNTHETIC_PARQUET_BYTES_ONLY", "open-jev-train.parquet",
                parquetInfo.Length, parquetSha, 2, "expected-open-jev-semantics.json",
                Convert.ToHexStringLower(SHA256.HashData(oracleBytes)), SourceId,
                "DuckDB.NET.Data.Full", "1.5.3", "MIT", true, false, false, false, false);
            WriteNew(Path.Combine(root, "fixture.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, FixtureJson.Default.FixtureReceipt));
            Check();
            Console.WriteLine("READY synthetic Parquet rows=2; sha256=" + parquetSha + "; DatasetTool acceptance NOT_RUN");
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or
            System.Data.Common.DbException or JsonException or OperationCanceledException or TimeoutException)
        {
            if (ownedRoot is not null)
            {
                var failure = new FixtureFailure("FAIL_PRESERVE_OWNED_OUTPUT", error.GetType().Name, error.Message, false);
                var failurePath = Path.Combine(ownedRoot, "failure.json");
                if (!Path.Exists(failurePath)) WriteNew(failurePath, JsonSerializer.SerializeToUtf8Bytes(failure, FixtureJson.Default.FixtureFailure));
            }
            Console.Error.WriteLine("dataset-fixture: " + error.GetType().Name + ": " + error.Message);
            return error is OperationCanceledException or TimeoutException ? 124 : 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static void Execute(DuckDBConnection connection, string sql, int seconds, CancellationToken token, Action check)
    {
        check();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Math.Min(seconds, 10);
        using var registration = token.Register(command.Cancel);
        command.ExecuteNonQuery();
        check();
    }

    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) || path.Any(char.IsControl))
            throw new ArgumentException("Fully qualified local path required.");
        return Path.GetFullPath(path);
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(true);
    }

    private static string Hash(string path, Action check)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Fixture hash byte bound.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        for (var block = 0; block < 129; block++)
        {
            check();
            var count = stream.Read(buffer);
            if (count == 0) return Convert.ToHexStringLower(hash.GetHashAndReset());
            hash.AppendData(buffer, 0, count);
        }
        throw new InvalidDataException("Fixture hash iteration bound.");
    }
}

internal sealed record ExpectedSemantic(string GroupId, string Language, string Kind, string[] Options, double[] Target,
    string ReferenceKind, string LabelSemantics);
internal sealed record FixtureReceipt(string Status, string ParquetFile, long ParquetBytes, string ParquetSha256, int Rows,
    string OracleFile, string OracleSha256, string AdapterSourceId, string Dependency, string DependencyVersion, string DependencyLicense,
    bool Synthetic, bool RealDatasetDownloaded, bool DatasetToolAccepted, bool TrainingRun, bool QualityAccepted);
internal sealed record FixtureFailure(string Status, string ErrorType, string Message, bool DatasetToolAccepted);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ExpectedSemantic[]))]
[JsonSerializable(typeof(FixtureReceipt))]
[JsonSerializable(typeof(FixtureFailure))]
internal partial class FixtureJson : JsonSerializerContext;
