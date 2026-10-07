using System.Text;
using Sezika;

namespace Sezika.IndependentPredict;

internal static class Admission
{
    internal static (AdmissionBinding Binding, RequestRow[] Rows) ReadApproved(string root, ExportManifest export,
        int maximumRecords, CancellationToken token)
    {
        var manifestBytes = BoundedFiles.Verify(root, export.AdmissionManifest, token);
        var audit = BoundedFiles.Json(manifestBytes, SplitAuditJsonContext.Default.AuditManifest, token);
        var auditRoot = Path.GetDirectoryName(BoundedFiles.Resolve(root, export.AdmissionManifest, token))!;
        var reportBytes = BoundedFiles.Verify(root, export.AdmissionReport, token);
        var report = BoundedFiles.Json(reportBytes, SplitAuditJsonContext.Default.AuditReport, token);
        var acceptanceBytes = BoundedFiles.Verify(root, export.HumanAcceptance, token, 65536);
        var acceptance = BoundedFiles.Json(acceptanceBytes, ExportJsonContext.Default.HumanAcceptance, token);
        var now = DateTimeOffset.UtcNow;
        BoundedFiles.Require(audit.SchemaVersion == 1 && audit.Stage == DatasetStage.Candidate && Enum.IsDefined(audit.IntendedUse) && audit.DatasetId == export.DatasetId &&
            report.SchemaVersion == 1 && report.DatasetId == audit.DatasetId && report.ToolPolicyVersion == "s4-05-admission-v1" &&
            report.Status == "checks_passed_pending_human_acceptance" && report.AdmissionChecksPassed && report.RequiresHumanAcceptance &&
            !report.RepresentsModelQuality && report.Issues is { Length: 0 } && report.RecordCount is >= 3 and <= 1000 &&
            BoundedFiles.Same(report.ManifestSha256, BoundedFiles.Hash(manifestBytes)) &&
            BoundedFiles.Same(report.RecordsSha256, audit.Records.Sha256), "S4-05 admission report is blocked, fixture-only or bound to different data.");
        BoundedFiles.Require(audit.Sources is { Length: >= 1 and <= 32 } && report.Sources is not null && report.Sources.Length == audit.Sources.Length &&
            report.Splits is { Length: 5 } && report.Splits.Select(item => item.Split).Distinct().Count() == 5 &&
            report.Splits.All(item => Enum.IsDefined(item.Split) && item.Count >= 0) && report.Splits.Sum(item => item.Count) == report.RecordCount &&
            report.Splits.Any(item => item.Split == DataSplit.Development && item.Count > 0) &&
            report.Splits.Any(item => item.Split == DataSplit.Calibration && item.Count > 0) &&
            report.Splits.Any(item => item.Split == DataSplit.SealedTest && item.Count > 0), "Admission split/source counts are incomplete.");
        var sources = new Dictionary<string, AuditSource>(StringComparer.Ordinal);
        foreach (var source in audit.Sources)
        {
            token.ThrowIfCancellationRequested();
            var matching = report.Sources.Where(item => item.SourceId == source.Id).ToArray();
            BoundedFiles.Require(sources.TryAdd(source.Id, source) && matching.Length == 1 && matching[0].AdmissionChecksPassed &&
                source.LicenseReview == ReviewStatus.Approved && matching[0].LicenseReview == ReviewStatus.Approved &&
                Enum.IsDefined(source.Origin) && Enum.IsDefined(source.Exposure) && source.AllowedUses is { Length: >= 1 and <= 3 } &&
                source.AllowedSplits is { Length: >= 1 and <= 5 } && source.AllowedUses.All(Enum.IsDefined) && source.AllowedSplits.All(Enum.IsDefined) &&
                !string.IsNullOrWhiteSpace(source.LicenseExpression) && !string.IsNullOrWhiteSpace(source.LicenseReviewer) &&
                source.LicenseReviewedUtc is not null && source.LicenseReviewedUtc <= now && source.LicenseEvidence is not null &&
                source.AllowedUses.Contains(audit.IntendedUse) && matching[0].DeclaredAllowedUses.SequenceEqual(source.AllowedUses) &&
                matching[0].DeclaredAllowedSplits.SequenceEqual(source.AllowedSplits) && matching[0].DeclaredExposure == source.Exposure,
                "Source license, intended use or report/source binding is not approved.");
            _ = BoundedFiles.Verify(auditRoot, source.LicenseEvidence!, token);
        }
        var seal = audit.TestSeal;
        BoundedFiles.Require(seal.Status == SealStatus.Sealed && !string.IsNullOrWhiteSpace(seal.Custodian) &&
            seal.SealedUtc is not null && seal.SealedUtc <= now && seal.Evidence is not null, "Independent test seal evidence is required.");
        var sealBytes = BoundedFiles.Verify(auditRoot, seal.Evidence!, token, 65536);
        var sealEvidence = BoundedFiles.Json(sealBytes, SplitAuditJsonContext.Default.SealEvidence, token);
        BoundedFiles.Require(sealEvidence.DatasetId == audit.DatasetId && BoundedFiles.Same(sealEvidence.RecordsSha256, report.RecordsSha256) &&
            sealEvidence.Custodian == seal.Custodian && sealEvidence.SealedUtc == seal.SealedUtc,
            "Seal does not bind the audited dataset inventory.");
        BoundedFiles.Require(acceptance.SchemaVersion == 1 && acceptance.Status == "accepted_for_quality" &&
            acceptance.DatasetId == audit.DatasetId && acceptance.IntendedUse == audit.IntendedUse.ToString() &&
            !string.IsNullOrWhiteSpace(acceptance.Reviewer) && !string.IsNullOrWhiteSpace(acceptance.Note) &&
            acceptance.AcceptedUtc <= now && acceptance.AcceptedUtc >= sealEvidence.SealedUtc && acceptance.InputContractAndLabelsReviewed &&
            BoundedFiles.Same(acceptance.AdmissionManifestSha256, BoundedFiles.Hash(manifestBytes)) &&
            BoundedFiles.Same(acceptance.AdmissionReportSha256, BoundedFiles.Hash(reportBytes)) &&
            BoundedFiles.Same(acceptance.AdmissionRecordsSha256, report.RecordsSha256) &&
            BoundedFiles.Same(acceptance.RequestsSha256, export.Requests.Sha256) &&
            BoundedFiles.Same(acceptance.SealSha256, BoundedFiles.Hash(sealBytes)) &&
            acceptance.RecordIds is { Length: >= 2 } && acceptance.RecordIds.Length <= maximumRecords &&
            acceptance.RecordIds.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 256) &&
            acceptance.RecordIds.Distinct(StringComparer.Ordinal).Count() == acceptance.RecordIds.Length,
            "Actual human acceptance must bind intended use, all hashes, approved request/label mapping and selected IDs.");

        // No inventory or model request text is opened until all metadata above
        // has passed. This operation belongs to the authorized seal custodian.
        var inventoryBytes = BoundedFiles.Verify(auditRoot, audit.Records, token);
        var inventory = Lines(inventoryBytes, 1000, token).Select(line =>
            BoundedFiles.Json(Encoding.UTF8.GetBytes(line), SplitAuditJsonContext.Default.AuditRecord, token)).ToArray();
        BoundedFiles.Require(inventory.Length == report.RecordCount && inventory.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() == inventory.Length &&
            report.Splits.All(split => inventory.Count(row => row.Split == split.Split) == split.Count), "Inventory IDs/split counts differ from the admitted report.");
        var byId = inventory.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var requestsBytes = BoundedFiles.Verify(root, export.Requests, token);
        var rows = Lines(requestsBytes, maximumRecords, token).Select(line =>
            BoundedFiles.Json(Encoding.UTF8.GetBytes(line), ExportJsonContext.Default.RequestRow, token)).ToArray();
        BoundedFiles.Require(rows.Length == acceptance.RecordIds.Length && rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal).SetEquals(acceptance.RecordIds),
            "Requests do not exactly match the human-accepted ID set.");
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            BoundedFiles.Require(byId.TryGetValue(row.Id, out var admitted), "Request ID is absent from audited inventory.");
            var source = sources[admitted!.SourceId];
            var split = row.Split == "calibration" ? DataSplit.Calibration : row.Split == "sealed_test" ? DataSplit.SealedTest : DataSplit.Audit;
            BoundedFiles.Require(split != DataSplit.Audit && admitted.Split == split && row.Language == admitted.Language && row.Domain == admitted.Domain &&
                admitted.HumanReview == ReviewStatus.Approved && !string.IsNullOrWhiteSpace(admitted.Reviewer) &&
                admitted.ReviewedUtc is not null && admitted.ReviewedUtc <= acceptance.AcceptedUtc && !string.IsNullOrWhiteSpace(admitted.ReviewNote) &&
                source.AllowedSplits.Contains(split) && source.Origin is not (SourceOrigin.PawsViewedTest250 or SourceOrigin.NimbleViewedEval324) &&
                source.Exposure != Exposure.EvaluationViewed && admitted.Exposure != Exposure.EvaluationViewed &&
                (split != DataSplit.SealedTest || (source.Exposure == Exposure.Unseen && admitted.Exposure == Exposure.Unseen)),
                "Request split, source, human review or unseen test semantics differ from admission.");
        }
        return (new AdmissionBinding(BoundedFiles.Hash(manifestBytes), BoundedFiles.Hash(reportBytes), report.RecordsSha256,
            BoundedFiles.Hash(acceptanceBytes), BoundedFiles.Hash(sealBytes)), rows);
    }

    internal static string[] Lines(byte[] bytes, int maximum, CancellationToken token)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), false);
        var lines = new List<string>();
        for (var index = 0; index <= maximum && !reader.EndOfStream; index++)
        {
            token.ThrowIfCancellationRequested();
            BoundedFiles.Require(index < maximum, "JSONL record count exceeds fixed maximum.");
            var line = reader.ReadLine()!;
            BoundedFiles.Require(line.Length > 0 && Encoding.UTF8.GetByteCount(line) <= 65536, "JSONL line exceeds 65536 UTF-8 bytes or is empty.");
            lines.Add(line);
        }
        return lines.ToArray();
    }
}
