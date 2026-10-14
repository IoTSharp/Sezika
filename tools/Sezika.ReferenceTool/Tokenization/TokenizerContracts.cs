using System.Text.Json.Serialization;

namespace Sezika.ReferenceTool.Tokenization;

internal sealed record TokenizerExportRequest(FilePin Tokenizer, FilePin Golden, FilePin[] ProgramSources,
    FilePin RuntimeExecutable, FilePin? RuntimeAssembly, string OutputDirectory, string ProcessEvidenceDirectory,
    FilePin BuildReceipt, int MaximumCases = 267, int TimeoutSeconds = 60, long MaximumResidentBytes = 536870912,
    string? CancelFile = null);
internal sealed record TokenizerCase(string Id, string Text, int[] Ids, int[] WithoutSpecialTokens);
internal sealed record TokenizerGolden(string SchemaVersion, string TokenizerSha256, int Planned, int Processed, TokenizerCase[] Cases);
internal sealed record TokenizerCapture(string SchemaVersion, string TokenizerSha256, string Reference, string Authorship,
    int Planned, int Processed, TokenizerCase[] Cases, FilePin[] ProgramSources, FilePin RuntimeExecutable,
    FilePin? RuntimeAssembly, FilePin BuildReceipt, FilePin BuildExecution, FilePin ProcessEvidence, Laya.PortProcess Process,
    FilePin Golden, int Crosschecked, FilePin ExportRequest, FilePin[] RuntimeFiles,
    bool UpstreamExecuted = false, bool RepresentsQuality = false);
internal sealed record TokenizerReceipt(string Status, FilePin? Capture, int Planned, int Completed, int Crosschecked,
    double ElapsedSeconds, long ObservedPeakBytes, string? Error, bool UpstreamExecuted = false);
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(TokenizerExportRequest))]
[JsonSerializable(typeof(TokenizerGolden))]
[JsonSerializable(typeof(TokenizerCase))]
[JsonSerializable(typeof(TokenizerCapture))]
[JsonSerializable(typeof(TokenizerReceipt))]
internal partial class TokenizerJson : JsonSerializerContext;
