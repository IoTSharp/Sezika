using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Sezika.Tooling;

internal static partial class AssetTransport
{
    private static async Task<(int Uploaded, int Prior, int Replaced, string? Checkpoint)> UploadMultipartAsync(PublicationPlan plan,
        string sourceRoot, AssetFile file, Uri endpoint, LfsAction upload, long chunkBytes, HttpClient client, Budget budget,
        CancellationToken cancellation, string? resumeCandidate, string? checkpointRoot)
    {
        var count = (file.Bytes + chunkBytes - 1) / chunkBytes;
        var completionUri = ValidateUri(upload.Href, true); ActionScope(endpoint, completionUri, plan.Provider);
        var sessionSha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(upload.Href)));
        var completed = new Dictionary<int, MultipartPartReceipt>(); var carried = new HashSet<int>(); var snapshots = 0;
        string? latest = null; string? checkpointDirectory = null;
        if (checkpointRoot is not null) checkpointDirectory = FreshDirectory(checkpointRoot, sourceRoot, budget);
        if (resumeCandidate is not null)
        {
            Require(checkpointDirectory is not null, "Multipart recovery requires a fresh checkpoint directory.");
            var candidate = ReadJson(resumeCandidate, TransportJsonContext.Default.MultipartResumeState, budget);
            Require(candidate.SchemaVersion == 1 && candidate.Repository == plan.Repository && candidate.Oid == file.Sha256 && candidate.Bytes == file.Bytes &&
                candidate.ChunkSize == chunkBytes && candidate.CompletedParts.Length <= count, "Prior multipart source/repository/chunk identity differs.");
            ValidateSha(candidate.SessionSha256);
            foreach (var receipt in candidate.CompletedParts)
            {
                budget.Check(); Require(receipt.PartNumber >= 1 && receipt.PartNumber <= count && carried.Add(receipt.PartNumber), "Prior multipart part identity duplicate/outside bound.");
                ValidateETag(receipt.Etag); ValidateSha(receipt.Sha256);
                var bytes = ReadPart(sourceRoot, file, receipt.PartNumber, chunkBytes, budget);
                Require(receipt.Bytes == bytes.LongLength && receipt.Sha256 == Convert.ToHexStringLower(SHA256.HashData(bytes)), "Prior multipart source part byte/SHA differs.");
                completed.Add(receipt.PartNumber, receipt);
            }
        }
        void Save(string status)
        {
            budget.Check(); if (checkpointDirectory is null) return;
            if (snapshots >= 260) throw new InvalidDataException("Multipart checkpoint count exceeded.");
            latest = Path.Combine(checkpointDirectory, $"state-{snapshots++:0000}.json");
            WriteJson(latest, new MultipartResumeState(1, plan.Repository, file.Sha256, file.Bytes, chunkBytes, sessionSha,
                completed.Values.OrderBy(p => p.PartNumber).ToArray(), status), TransportJsonContext.Default.MultipartResumeState, budget);
        }
        async Task<MultipartPartReceipt> Put(int number)
        {
            budget.Check(); cancellation.ThrowIfCancellationRequested();
            if (upload.Header is null || !upload.Header.TryGetValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture), out var signed))
                throw new InvalidDataException("Current multipart session omitted a part URL.");
            var uri = ValidateUri(signed, true); ActionScope(endpoint, uri, plan.Provider);
            var bytes = ReadPart(sourceRoot, file, number, chunkBytes, budget);
            using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = new ByteArrayContent(bytes) };
            using var deadline = HttpDeadline(budget, cancellation, 1800);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            Require(response.IsSuccessStatusCode && response.Headers.ETag is not null, "Multipart PUT was not acknowledged; inspect remote state before retry.");
            var tag = response.Headers.ETag!.ToString(); ValidateETag(tag);
            return new(number, tag, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        async Task<HttpStatusCode> Complete()
        {
            budget.Check(); Require(completed.Count == count && completed.Values.Sum(p => p.Bytes) == file.Bytes, "Multipart complete part-set/byte denominator differs.");
            using var request = JsonRequest(completionUri, new MultipartCompletion(file.Sha256,
                completed.Values.OrderBy(p => p.PartNumber).Select(p => new MultipartPart(p.PartNumber, p.Etag)).ToArray()), TransportJsonContext.Default.MultipartCompletion, null);
            using var deadline = HttpDeadline(budget, cancellation, 30);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.StatusCode;
        }
        Save(carried.Count == 0 ? "PARTS_PENDING" : "PRIOR_PARTS_CANDIDATE_NOT_SESSION_VERIFIED");
        var uploaded = 0; var replaced = 0;
        for (var part = 1; part <= count; part++)
        {
            budget.Check(); if (carried.Contains(part)) continue;
            completed.Add(part, await Put(part)); uploaded++; Save("PART_ACKNOWLEDGED");
        }
        var result = await Complete();
        if ((result is HttpStatusCode.BadRequest or HttpStatusCode.NotFound) && carried.Count > 0)
        {
            // A definite session rejection is reconciled. Uncertain network/timeouts never trigger another write.
            foreach (var part in carried) completed.Remove(part);
            Save("PRIOR_SESSION_CANDIDATE_REJECTED_REUPLOAD_CARRIED_PARTS");
            foreach (var part in carried.Order())
            {
                budget.Check(); completed.Add(part, await Put(part)); uploaded++; replaced++; Save("CURRENT_SESSION_REPLACEMENT_ACKNOWLEDGED");
            }
            result = await Complete();
        }
        Require((int)result is >= 200 and < 300, "Multipart completion failed; inspect remote session/object before further writes.");
        Save("MULTIPART_COMPLETION_ACKNOWLEDGED_OBJECT_READBACK_PENDING");
        return (uploaded, carried.Count, replaced, latest);
    }

    private static byte[] ReadPart(string root, AssetFile file, int number, long chunkBytes, Budget budget)
    {
        budget.Check(); var start = checked((number - 1) * chunkBytes); var bytes = Math.Min(chunkBytes, file.Bytes - start);
        Require(number >= 1 && start >= 0 && bytes is >= 1 and <= 64 * 1024 * 1024, "Multipart source part bound differs.");
        var path = AssetPath(root, file.Path, budget); RegularFiles.Require(path, budget);
        using var input = File.OpenRead(path); Require(input.Length == file.Bytes, "Multipart source length changed.");
        input.Position = start; var result = new byte[(int)bytes]; input.ReadExactly(result); return result;
    }
}
