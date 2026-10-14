using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.Tooling;

internal sealed class ProtectedOutputPathException(string message) : IOException(message);

internal sealed record DownloadRequest(DownloadSpec Asset, string Destination, string? CredentialTarget = null, string CredentialEncoding = "utf8", string CredentialSource = "auto", string AuthenticationProvider = "bearer");
internal sealed record TransportPrepareRequest(string SourceRoot, string LogicalManifest, string OutputRoot, long ChunkBytes);
internal sealed record TransportReadbackRequest(string Root, string Destination, string TransportSha256, string AttributesSha256);
internal sealed record PublicationPreviewRequest(string SourceRoot, PublicationPlan Plan);
internal sealed record FullPublicationRequest(string PackageRoot, string Validation, string Contract, string OutputRoot, string ToolRoot, string TrainingProof, string ValidationProof);
internal sealed record PublicationUploadRequest(string SourceRoot, string Preview, string Authorization, AssetFile Asset, string ServiceRoot, string? CredentialTarget = null, string CredentialEncoding = "utf8", string? ResumeCandidate = null, string? CheckpointRoot = null, string CredentialSource = "auto");
internal sealed record PublicationCommitRequest(string SourceRoot, string Preview, string Authorization, string ServiceRoot, string? CredentialTarget = null, string CredentialEncoding = "utf8", string CredentialSource = "auto");

internal static class MigrationCommands
{
    internal static readonly string[] Commands = ["oracle batches", "inventory review", "git run", "transport download", "transport prepare", "transport verify", "transport reassemble", "publication preview", "publication prepare", "publication upload", "publication commit", "task prepare", "task resident", "bundle inputs", "bundle build", "bundle freeze", "bundle install", "archive pack", "checkpoint verify", "host inspect", "host sample-gpu", "corpus prepare", "corpus compose", "corpus queue", "corpus collect", "corpus admission", "corpus dataset", "corpus research", "corpus research-check", "corpus seed-check", "corpus inspect", "corpus estimate", "corpus bind", "validation profile", "validation summarize", "validation data-admission", "validation language-audit", "validation batch", "validation near-ties"];
    internal static int Run(string command, string requestPath, string output, Budget budget, CancellationToken cancellation)
    {
        if (command.StartsWith("validation ", StringComparison.Ordinal)) return RunValidation(command, requestPath, output, budget);
        T Read<T>(JsonTypeInfo<T> type)
        {
            var path = Path.GetFullPath(requestPath); RegularFiles.Require(path, budget);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Command request exceeds4MiB.");
            return CorpusJson.Read(path, type, budget);
        }
        int Save<T>(T result, JsonTypeInfo<T> type)
        {
            budget.Check(); CorpusJson.Write(output, result, type, budget);
            var status = result switch { CorpusOperationReport report => report.Status, CorpusManifest manifest => manifest.Status, CorpusResearchCheckReport report => report.Status, CorpusSeedCheckReport report => report.Status, GitReceipt report => report.Status, BundleReceipt report => report.Status, ResidentReceipt report => report.Status, RetirementReport report => report.Status, BundleInstallationReport report => report.Status, _ => "" };
            return status.StartsWith("PARTIAL", StringComparison.Ordinal) || status.StartsWith("FAIL", StringComparison.Ordinal) || status.StartsWith("UNKNOWN", StringComparison.Ordinal) || status.StartsWith("PENDING", StringComparison.Ordinal) ? 1 : 0;
        }
        void ProtectOutputRoot(string directory)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); var path = Path.GetFullPath(output);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (path.Equals(root, comparison) || path.StartsWith(prefix, comparison))
                throw new ProtectedOutputPathException("Outer receipt must stay outside the protected installation or observation root; no failure receipt is written there.");
        }
        switch (command)
        {
            case "oracle batches":
                var batchRequest = OracleBatchOperations.ReadRequest(Path.GetFullPath(requestPath), budget);
                var batchReport = OracleBatchOperations.RunAsync(batchRequest, budget, cancellation).GetAwaiter().GetResult();
                TaskOperations.WriteNew(output, batchReport, OracleJsonContext.Default.OracleBatchReport);
                return batchReport.Status == "SELECTED_BATCHES_COMPLETE" ? 0 : batchReport.Status == "SELECTED_BATCHES_COMPLETE_WITH_NUMERICAL_DIFFERENCES" ? 1 : 2;
            case "inventory review": return Save(RetirementReview.Review(Read(RetirementJson.Default.RetirementReviewRequest), budget), RetirementJson.Default.RetirementReport);
            case "git run": return Save(NativeGit.RunAsync(Read(GitJsonContext.Default.GitRequest), budget, cancellation).GetAwaiter().GetResult(), GitJsonContext.Default.GitReceipt);
            case "task prepare": return Save(TaskOperations.Prepare(Read(TaskJsonContext.Default.TaskPreparationRequest), budget), TaskJsonContext.Default.PreparedTask);
            case "task resident": return Save(ResidentController.RunAsync(Read(ResidentJsonContext.Default.ResidentRequest), budget, cancellation).GetAwaiter().GetResult(), ResidentJsonContext.Default.ResidentReceipt);
            case "bundle inputs": return Save(BundleBuild.InspectInputs(Read(BundleBuildJson.Default.BundleInputRequest), budget), BundleBuildJson.Default.BundleInputReceipt);
            case "bundle build": return Save(BundleBuild.BuildAsync(Read(BundleBuildJson.Default.BundleBuildRequest), budget, cancellation).GetAwaiter().GetResult(), TaskJsonContext.Default.BundleReceipt);
            case "bundle freeze": return Save(TaskOperations.Freeze(Read(TaskJsonContext.Default.BundleRequest), budget), TaskJsonContext.Default.BundleReceipt);
            case "bundle install":
                var installation = Read(BundleInstallationJson.Default.BundleInstallationRequest);
                ProtectOutputRoot(installation.Destination); ProtectOutputRoot(installation.EvidenceDirectory);
                return Save(BundleInstallation.RunAsync(installation, budget, cancellation).GetAwaiter().GetResult(), BundleInstallationJson.Default.BundleInstallationReport);
            case "archive pack": return Save(TaskOperations.Pack(Read(TaskJsonContext.Default.PackRequest), budget), TaskJsonContext.Default.PackReceipt);
            case "checkpoint verify": return Save(TaskOperations.VerifyCheckpoint(Read(TaskJsonContext.Default.CheckpointVerificationRequest), budget), TaskJsonContext.Default.CheckpointVerificationReceipt);
            case "host inspect": return Save(TaskOperations.InspectHost(Read(TaskJsonContext.Default.HostInspectionRequest), budget), TaskJsonContext.Default.HostInspectionReceipt);
            case "host sample-gpu":
                var observation = Read(GpuObservationJsonContext.Default.GpuObservationRequest);
                ProtectOutputRoot(observation.OutputDirectory);
                var observed = GpuObservation.RunAsync(observation, budget, cancellation).GetAwaiter().GetResult();
                TaskOperations.WriteNew(output, observed, GpuObservationJsonContext.Default.GpuObservationReport);
                return observed.Status == "PASS" ? 0 : 1;
            case "transport download":
                var download = Read(MigrationJsonContext.Default.DownloadRequest);
                var downloadAuthentication = HttpCredentials.Read(download.CredentialSource, download.CredentialTarget, download.CredentialEncoding, download.AuthenticationProvider, budget);
                return Save(AssetTransport.DownloadAsync(download.Asset, download.Destination, budget, cancellation, authentication: downloadAuthentication).GetAwaiter().GetResult(), TransportJsonContext.Default.DownloadReport);
            case "transport prepare":
                var prepare = Read(MigrationJsonContext.Default.TransportPrepareRequest);
                return Save(AssetTransport.Prepare(prepare.SourceRoot, prepare.LogicalManifest, prepare.OutputRoot, prepare.ChunkBytes, budget), TransportJsonContext.Default.TransportPreparationReport);
            case "transport verify":
                var verify = Read(MigrationJsonContext.Default.TransportReadbackRequest);
                return Save(AssetTransport.VerifyPhysical(verify.Root, verify.TransportSha256, verify.AttributesSha256, budget), TransportJsonContext.Default.TransportManifest);
            case "transport reassemble":
                var reassemble = Read(MigrationJsonContext.Default.TransportReadbackRequest);
                return Save(AssetTransport.Reassemble(reassemble.Root, reassemble.Destination, reassemble.TransportSha256, reassemble.AttributesSha256, budget), TransportJsonContext.Default.TransportReadbackReport);
            case "publication preview":
                var preview = Read(MigrationJsonContext.Default.PublicationPreviewRequest);
                return Save(AssetTransport.Preview(preview.SourceRoot, preview.Plan, budget), TransportJsonContext.Default.PublicationPreview);
            case "publication prepare":
                var publication = Read(MigrationJsonContext.Default.FullPublicationRequest);
                return Save(AssetTransport.PreparePublication(publication.PackageRoot, publication.Validation, publication.Contract, publication.OutputRoot, publication.ToolRoot, publication.TrainingProof, publication.ValidationProof, budget), TransportJsonContext.Default.LogicalManifest);
            case "publication upload":
                var upload = Read(MigrationJsonContext.Default.PublicationUploadRequest);
                var frozenUpload = AssetTransport.ReadJson(upload.Preview, TransportJsonContext.Default.PublicationPreview, budget);
                var uploadAuthorization = AssetTransport.ReadJson(upload.Authorization, TransportJsonContext.Default.PublicationAuthorization, budget);
                using (var client = AssetTransport.CreateClient(false))
                {
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, Math.Min(1800, budget.RemainingSeconds)));
                    var authentication = HttpCredentials.Read(upload.CredentialSource, upload.CredentialTarget, upload.CredentialEncoding, frozenUpload.Plan.Provider, budget);
                    return Save(AssetTransport.UploadLfsObjectAsync(frozenUpload, uploadAuthorization, upload.SourceRoot, upload.Asset, AssetTransport.ValidateUri(upload.ServiceRoot, false), client, null, budget, cancellation, upload.ResumeCandidate, upload.CheckpointRoot, authentication).GetAwaiter().GetResult(), TransportJsonContext.Default.ObjectUploadReport);
                }
            case "publication commit":
                var commit = Read(MigrationJsonContext.Default.PublicationCommitRequest);
                var frozenCommit = AssetTransport.ReadJson(commit.Preview, TransportJsonContext.Default.PublicationPreview, budget);
                var commitAuthorization = AssetTransport.ReadJson(commit.Authorization, TransportJsonContext.Default.PublicationAuthorization, budget);
                using (var client = AssetTransport.CreateClient(false))
                {
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, Math.Min(1800, budget.RemainingSeconds)));
                    var authentication = HttpCredentials.Read(commit.CredentialSource, commit.CredentialTarget, commit.CredentialEncoding, frozenCommit.Plan.Provider, budget);
                    return Save(AssetTransport.CommitHuggingFaceAsync(frozenCommit, commitAuthorization, commit.SourceRoot, AssetTransport.ValidateUri(commit.ServiceRoot, false), client, null, budget, cancellation, authentication).GetAwaiter().GetResult(), TransportJsonContext.Default.HubCommitReceipt);
                }
            case "corpus prepare": return Save(CorpusOperations.Prepare(Read(CorpusJsonContext.Default.CorpusPrepareRequest), budget), CorpusJsonContext.Default.CorpusManifest);
            case "corpus compose": return Save(CorpusOperations.Compose(Read(CorpusJsonContext.Default.CorpusComposeRequest), budget), CorpusJsonContext.Default.CorpusManifest);
            case "corpus queue": return Save(CorpusOperations.BuildQueue(Read(CorpusJsonContext.Default.CorpusQueueRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus collect": return Save(CorpusOperations.Collect(Read(CorpusJsonContext.Default.CorpusTeacherRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus admission": return Save(CorpusOperations.CheckAdmission(Read(CorpusJsonContext.Default.CorpusAdmissionRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus dataset": return Save(CorpusOperations.BuildDataset(Read(CorpusJsonContext.Default.CorpusDatasetBuildRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus research": return Save(CorpusOperations.BuildResearchPlan(Read(CorpusJsonContext.Default.CorpusResearchPlanRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus research-check": return Save(CorpusOperations.CheckResearch(Read(CorpusJsonContext.Default.CorpusResearchCheckRequest), budget), CorpusJsonContext.Default.CorpusResearchCheckReport);
            case "corpus seed-check": return Save(CorpusOperations.CheckSeeds(Read(CorpusJsonContext.Default.CorpusSeedCheckRequest), budget), CorpusJsonContext.Default.CorpusSeedCheckReport);
            case "corpus inspect": return Save(CorpusOperations.Inspect(Read(CorpusJsonContext.Default.CorpusInspectRequest), budget), CorpusJsonContext.Default.CorpusInspectionReport);
            case "corpus estimate": return Save(CorpusOperations.Estimate(Read(CorpusJsonContext.Default.CorpusEstimateRequest), budget), CorpusJsonContext.Default.CorpusEstimateReport);
            case "corpus bind": return Save(CorpusOperations.Bind(Read(CorpusJsonContext.Default.CorpusBindRequest), budget), CorpusJsonContext.Default.CorpusBinding);
            default: throw new ArgumentException("Unsupported corpus migration command.");
        }
    }
    private static int RunValidation(string command, string requestPath, string output, Budget budget)
    {
        T Read<T>(JsonTypeInfo<T> type)
        {
            var path = Path.GetFullPath(requestPath); RegularFiles.Require(path, budget);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Validation request exceeds4MiB.");
            return CorpusJson.Read(path, type, budget);
        }
        void Bind(string requested, bool directory)
        {
            var expected = Path.GetFullPath(output + (directory ? ".artifacts" : ""));
            if (!Path.IsPathFullyQualified(requested) || !string.Equals(Path.GetFullPath(requested), expected,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Validation request Output differs from the exact CLI report/artifact binding.");
        }
        int Finish<T>(T result, string reportPath, JsonTypeInfo<T> type, bool outerReport = false)
        {
            var actual = CorpusJson.Read(reportPath, type, budget);
            if (CorpusJson.Canonical(JsonSerializer.SerializeToElement(result, type)) != CorpusJson.Canonical(JsonSerializer.SerializeToElement(actual, type)))
                throw new InvalidDataException("Validation report readback differs from the returned typed result.");
            var exit = actual switch
            {
                ProfileValidationReport { Status: "PASS_HISTORICAL_PROFILE_CONSISTENCY_ONLY" } => 0,
                S5SummaryReport { Status: "PASS_HISTORICAL_S5_REPORT_CONSISTENCY_ONLY" } => 0,
                LanguageAuditReport { Status: "original_fixture_audit_not_independent_quality_acceptance" } => 0,
                ValidationBatchReport { Status: "PASS_BOUNDED_NATIVE_VALIDATION" } => 0,
                ValidationBatchReport { Status: "PARTIAL_OR_FAILED_VALIDATION_PRESERVE_RECEIPTS" } => 1,
                NearTieReport { Status: "COMPLETE_HISTORICAL_MARGIN_OBSERVATION_ONLY" } => 0,
                NearTieReport { Status: "PARTIAL_HISTORICAL_MARGIN_OBSERVATION" } => 1,
                DataAdmissionReport { Status: "blocked" } => 1,
                DataAdmissionReport { Status: "checks_passed_pending_human_acceptance" } => 3,
                _ => 2
            };
            if (outerReport) CorpusJson.Write(output, actual, type, budget);
            return exit;
        }
        switch (command)
        {
            case "validation profile":
                var profile = Read(ValidationJsonContext.Default.ProfileValidationRequest); Bind(profile.Output, false);
                return Finish(ValidationOperations.Profile(profile, budget), output, ValidationJsonContext.Default.ProfileValidationReport);
            case "validation summarize":
                var summary = Read(ValidationJsonContext.Default.S5SummaryRequest); Bind(summary.Output, false);
                return Finish(ValidationOperations.Summarize(summary, budget), output, ValidationJsonContext.Default.S5SummaryReport);
            case "validation data-admission":
                var admission = Read(ValidationJsonContext.Default.DataAdmissionRequest); Bind(admission.Output, false);
                return Finish(ValidationOperations.DataAdmission(admission, budget), output, ValidationJsonContext.Default.DataAdmissionReport);
            case "validation language-audit":
                var language = Read(ValidationJsonContext.Default.LanguageAuditRequest); Bind(language.Output, true);
                return Finish(ValidationOperations.LanguageAudit(language, budget), Path.Combine(language.Output, "manifest.json"), ValidationJsonContext.Default.LanguageAuditReport, true);
            case "validation batch":
                var batch = Read(ValidationJsonContext.Default.ValidationBatchRequest); Bind(batch.Output, true);
                return Finish(ValidationOperations.Batch(batch, budget), Path.Combine(batch.Output, "report.json"), ValidationJsonContext.Default.ValidationBatchReport, true);
            case "validation near-ties":
                var near = Read(ValidationJsonContext.Default.NearTieRequest); Bind(near.Output, false);
                return Finish(ValidationOperations.NearTies(near, budget), output, ValidationJsonContext.Default.NearTieReport);
            default: throw new ArgumentException("Unknown validation command.");
        }
    }
}

[JsonSerializable(typeof(DownloadRequest))]
[JsonSerializable(typeof(TransportPrepareRequest))]
[JsonSerializable(typeof(TransportReadbackRequest))]
[JsonSerializable(typeof(PublicationPreviewRequest))]
[JsonSerializable(typeof(FullPublicationRequest))]
[JsonSerializable(typeof(PublicationUploadRequest))]
[JsonSerializable(typeof(PublicationCommitRequest))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class MigrationJsonContext : JsonSerializerContext;
