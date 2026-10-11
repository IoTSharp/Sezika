using System.Text.Json.Serialization;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusAllowedAsset(string Path,string FileSha256,string? SourceRevision,string OriginalSplit);
internal sealed record CorpusDeniedSource(string Dataset,string Reason);
internal sealed record CorpusSourcePolicyDescriptor(int SchemaVersion,string Status,bool FormalTrainingAdmitted,bool FullTokenLengthAccepted,
    string DefaultAction,string[] SourceAllowlist,CorpusDeniedSource[] SourceDenylist,Dictionary<string,CorpusAllowedAsset[]> SourceAssetAllowlist);

[JsonSerializable(typeof(CorpusSourcePolicyDescriptor))]
internal partial class CorpusSourcePolicyJson:JsonSerializerContext;

internal sealed class CorpusSourcePolicy
{
    private readonly Dictionary<string,Dictionary<string,CorpusAllowedAsset>> _allowed=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _denied=new(StringComparer.Ordinal);
    internal string Sha256 {get;}
    internal CorpusSourcePolicy(string path,string expectedSha,RunBudget budget)
    {
        Sha256=Data.FileHash(path,budget);
        if(expectedSha.Length!=64||Sha256!=expectedSha)throw new InvalidDataException("Private source policy fixed SHA differs.");
        var policy=Assets.Read(path,CorpusSourcePolicyJson.Default.CorpusSourcePolicyDescriptor);
        if(policy.SourceAllowlist is null||policy.SourceAssetAllowlist is null||policy.SourceDenylist is null||
            policy.SchemaVersion!=1||policy.Status!="PRIVATE_EXPERIMENT_SCOPE_FIXED_METADATA_ONLY"||policy.FormalTrainingAdmitted||policy.FullTokenLengthAccepted||policy.DefaultAction!="deny"||
            policy.SourceAllowlist.Length is <1 or >128||policy.SourceAllowlist.Distinct(StringComparer.Ordinal).Count()!=policy.SourceAllowlist.Length||
            policy.SourceAssetAllowlist.Count!=policy.SourceAllowlist.Length||policy.SourceDenylist.Length>128)throw new InvalidDataException("Explicit private source scope contract differs.");
        var count=0;
        foreach(var dataset in policy.SourceAllowlist)
        {
            budget.Check();if(string.IsNullOrWhiteSpace(dataset)||!policy.SourceAssetAllowlist.TryGetValue(dataset,out var assets)||assets is null||assets.Length is <1 or >4096)throw new InvalidDataException("Allowed source assets absent/bounded.");
            var rows=new Dictionary<string,CorpusAllowedAsset>(StringComparer.Ordinal);
            foreach(var asset in assets)
            {
                budget.Check();if(asset is null||++count>4096||string.IsNullOrWhiteSpace(asset.Path)||asset.FileSha256 is null||asset.FileSha256.Length!=64||!asset.FileSha256.All(char.IsAsciiHexDigit)||
                    string.IsNullOrWhiteSpace(asset.SourceRevision)||asset.OriginalSplit is not("train" or "unsplit")||!rows.TryAdd(asset.Path.Replace('\\','/'),asset))throw new InvalidDataException("Source asset identity, original split or item limit differs.");
            }
            _allowed.Add(dataset,rows);
        }
        foreach(var denied in policy.SourceDenylist)
        {
            budget.Check();if(denied is null||string.IsNullOrWhiteSpace(denied.Dataset)||string.IsNullOrWhiteSpace(denied.Reason)||_allowed.ContainsKey(denied.Dataset)||!_denied.TryAdd(denied.Dataset,denied.Reason))throw new InvalidDataException("Explicit denied source scope differs.");
        }
    }
    internal string? Failure(TrainingRow row)
    {
        var source=row.Source;
        if(source is null||string.IsNullOrWhiteSpace(source.Dataset)||string.IsNullOrWhiteSpace(source.Path))return "source_scope_provenance_missing";
        if(_denied.TryGetValue(source.Dataset,out var reason))return reason;
        if(!_allowed.TryGetValue(source.Dataset,out var paths))return "source_scope_dataset_not_reviewed";
        if(!paths.TryGetValue(source.Path.Replace('\\','/'),out var asset))return "source_scope_configuration_not_reviewed";
        if(asset.FileSha256!=source.FileSha256||asset.SourceRevision!=source.Revision||asset.OriginalSplit!=source.OriginalSplit||source.Row!=row.SourceRow)return "source_scope_asset_identity_or_original_split_differs";
        return null;
    }
}
