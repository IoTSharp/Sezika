using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal enum ModelAssetRole { Config, Tokenizer, Weights, License }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ModelMirrorAsset(string Path, ModelAssetRole Role, long Bytes, string DigestKind, string Digest,
    string? ETag = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ModelMirrorManifest(int SchemaVersion, string Repository, string Revision, string LicenseIdentity,
    ModelMirrorAsset[] Assets);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ModelMirrorRequest(int SchemaVersion, string Root, string ManifestFile, string ManifestSha256,
    string CacheDirectory, string OutputDirectory, string RuntimeIdentifier);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ModelMirrorVerifyRequest(int SchemaVersion, string Directory, string ManifestSha256,
    string RuntimeIdentifier);
internal sealed record ModelMirrorAssetResult(string Path, ModelAssetRole Role, long Bytes, string Sha256,
    string CacheStatus, int Requests, long ResumedBytes, bool IndependentlyReadBack);
internal sealed record ModelMirrorVerification(string Status, string Directory, string Repository, string Revision,
    string LicenseIdentity, string ManifestSha256, int Assets, long Bytes, bool AllManifestAssetsVerified,
    string Coverage = "CALLER_PINNED_ASSET_SET_ONLY", string Quality = "NOT_ASSESSED", string Publication = "NOT_RUN");
internal sealed record ModelMirrorReport(string Status, ModelMirrorVerification Verification, ModelMirrorAssetResult[] Assets);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ModelMirrorManifest))]
[JsonSerializable(typeof(ModelMirrorRequest))]
[JsonSerializable(typeof(ModelMirrorVerifyRequest))]
[JsonSerializable(typeof(ModelMirrorReport))]
[JsonSerializable(typeof(ModelMirrorVerification))]
internal partial class ModelMirrorJsonContext : JsonSerializerContext;
