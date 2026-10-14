using System.Text.Json.Serialization;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal sealed record ExecutablePin(string Path, string Sha256);
internal enum RuntimeBundleKind { NativeAotImage, SelfContainedDotnet }
internal sealed record BundleFile(string RelativePath, long Bytes, string Sha256);
internal sealed record RuntimeBundleManifest(int SchemaVersion, string Directory, string EntryPoint, RuntimeBundleKind Runtime, BundleFile[] Files);
internal sealed record BundlePin(RuntimeBundleManifest Manifest, string Sha256);
internal sealed record RemoteConfiguration(int SchemaVersion, string HostName, int Port, string UserName, string HostKeySha256,
    string Root, string WorkerPath, string WorkerSha256, ExecutablePin[] AllowedExecutables, string AuthenticationSource,
    string AuthenticationKind, string? CredentialTarget = null, string CredentialEncoding = "utf8", int MaximumSeconds = 300,
    int MaximumDirectoryEntries = 1024, long MaximumFileBytes = 64L * 1024 * 1024 * 1024,
    BundlePin? WorkerBundle = null, BundlePin[]? AllowedPayloadBundles = null);
internal sealed record RemoteSecretInput(string? Password = null, string? PrivateKey = null, string? Passphrase = null);
internal sealed record RemoteInvocationInput(RemoteSecretInput? Authentication = null, byte[]? StandardInput = null);
internal sealed record RemoteFailure(string Status, string ErrorType, string RemoteState = "PRESERVED_RECONCILE_BEFORE_RETRY");
internal sealed record TransferRequest(int SchemaVersion, string OperationId, string Path, long Bytes, string Sha256, bool Executable = false);
internal sealed record RemoteTransferReport(string Status, string OperationId, string HostName, int Port, string HostKeySha256,
    string RemotePath, string LocalPath, long Bytes, string Sha256, long ResumedBytes, bool IndependentReadback, string PartialEvidence = "PRESERVED_ON_FAILURE");
internal sealed record RemoteTransferState(int SchemaVersion, string OperationId, string HostName, int Port, string HostKeySha256,
    string RemotePath, long Bytes, string Sha256, DateTime LastWriteUtc);
internal sealed record WorkerRequest(int SchemaVersion, string RequestId, string Root, string WorkerPath, string WorkerSha256,
    ExecutablePin[] AllowedExecutables, string Executable, string ExecutableSha256, string[] Arguments, string WorkingDirectory,
    string OutputDirectory, int TimeoutSeconds, int CleanupSeconds = 10, int MaximumProcesses = 64, int MaximumOutputBytes = 1048576,
    int StandardInputBytes = 0, BundlePin? WorkerBundle = null, BundlePin? PayloadBundle = null);
internal sealed record WorkerResult(string Status, string RequestId, string RequestSha256, string WorkerBundleSha256,
    string PayloadBundleSha256, string ExecutionReceiptSha256, string BundleValidation, ExecutionResult Execution);
internal sealed record RemoteDispatchReport(string Status, string RequestId, string HostName, int Port, string HostKeySha256,
    int? SshExitCode, long StandardOutputBytes, long StandardErrorBytes, string RemoteReceiptPath, string? ReceiptSha256,
    string ResourceReclamation, string ReceiptReadback, string CancellationSignal,
    string? WorkerBundleSha256 = null, string? PayloadBundleSha256 = null);

[JsonSourceGenerationOptions(WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RemoteConfiguration))]
[JsonSerializable(typeof(RemoteSecretInput))]
[JsonSerializable(typeof(RemoteInvocationInput))]
[JsonSerializable(typeof(RemoteFailure))]
[JsonSerializable(typeof(TransferRequest))]
[JsonSerializable(typeof(RemoteTransferReport))]
[JsonSerializable(typeof(RemoteTransferState))]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerResult))]
[JsonSerializable(typeof(RuntimeBundleManifest))]
[JsonSerializable(typeof(BundlePin))]
[JsonSerializable(typeof(RemoteDispatchReport))]
internal partial class RemoteJsonContext : JsonSerializerContext;
