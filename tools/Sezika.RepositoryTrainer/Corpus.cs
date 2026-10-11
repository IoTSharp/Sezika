using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusShard(
    [property:JsonPropertyName("path")]string Path,[property:JsonPropertyName("source")]string Source,[property:JsonPropertyName("split")]string Split,
    [property:JsonPropertyName("records")]long Records,[property:JsonPropertyName("bytes")]long Bytes,[property:JsonPropertyName("sha256")]string Sha256);
internal sealed record CorpusManifest(
    [property:JsonPropertyName("schema_version")]int SchemaVersion,[property:JsonPropertyName("status")]string Status,[property:JsonPropertyName("pilot")]bool Pilot,
    [property:JsonPropertyName("formal_training_admitted")]bool FormalTrainingAdmitted,[property:JsonPropertyName("records_written")]long RecordsWritten,[property:JsonPropertyName("shards")]CorpusShard[] Shards);
internal sealed record CorpusExclusion([property:JsonPropertyName("record_id")]string RecordId,[property:JsonPropertyName("reason")]string Reason);
internal sealed record CorpusBindings(string ManifestSha256,string ExclusionsSha256,string SourceRightsSha256,string SourceAssetsSha256,bool FormalTrainingAdmitted,string? SourceRoot=null,string? SourceAssetsPath=null);
internal sealed record CorpusCheck(string Status,string ManifestSha256,string ExclusionsSha256,long PhysicalRows,long ExcludedRows,long Train,long Development,
    int Families,int Shards,bool FormalTrainingAdmitted,bool TokenLengthAccepted,bool GpuTrainingAccepted,string FamilyReview,Dictionary<string,long> BySourceAndType,Dictionary<string,CorpusTargetStats> TargetDistributions);
internal sealed record CorpusTargetStats(long Records,long OneHotRecords,double[] MeanTargetMassByCandidatePosition);

// Offline data handling only; no model feature calculation or training.
internal sealed class Corpus
{
    private readonly string _root;
    private readonly HashSet<string> _excluded=new(StringComparer.Ordinal);
    internal CorpusManifest Manifest {get;}
    internal string ManifestSha256 {get;}
    internal string ExclusionsSha256 {get;}
    internal Corpus(string root,string expectedSha,string expectedBindingsSha,RunBudget budget)
    {
        _root=Path.GetFullPath(root);var path=Data.Within(_root,"manifest.json");ManifestSha256=Data.FileHash(path,budget);
        if(expectedSha.Length!=64||ManifestSha256!=expectedSha)throw new InvalidDataException("Pinned corpus manifest SHA differs.");
        Manifest=Assets.Read(path,TrainerJson.Default.CorpusManifest,16*1024*1024);
        if(Manifest.SchemaVersion!=1||Manifest.Status!="COMPLETE_PREPARATION_ONLY"||Manifest.Shards.Length is <1 or >4096||Manifest.RecordsWritten is <1 or >50000000||Manifest.Shards.Sum(s=>s.Records)!=Manifest.RecordsWritten)
            throw new InvalidDataException("Incomplete corpus, unsupported schema or denominator.");
        if(Manifest.Shards.Select(s=>s.Path).Distinct(StringComparer.Ordinal).Count()!=Manifest.Shards.Length)throw new InvalidDataException("Duplicate shard path.");
        var ledger=Data.Within(_root,"exclusions.jsonl");ExclusionsSha256=Data.FileHash(ledger,budget);
        var bindingsPath=Data.Within(_root,"corpus-bindings.json");
        if(Data.FileHash(bindingsPath,budget)!=expectedBindingsSha)throw new InvalidDataException("Pinned corpus bindings SHA differs.");
        var binding=Assets.Read(bindingsPath,TrainerJson.Default.CorpusBindings);
        if(binding.ManifestSha256!=ManifestSha256||binding.ExclusionsSha256!=ExclusionsSha256||Data.FileHash(Data.Within(_root,"source-rights.json"),budget)!=binding.SourceRightsSha256||binding.FormalTrainingAdmitted)throw new InvalidDataException("Corpus bindings/rights/exclusions differ.");
        var sourceAssets=binding.SourceAssetsPath is not null?Data.Within(_root,binding.SourceAssetsPath):
            File.Exists(Data.Within(_root,"source-assets.json"))?Data.Within(_root,"source-assets.json"):
            binding.SourceRoot is not null?Data.Within(Path.GetFullPath(binding.SourceRoot),"ASSET_MANIFEST.json"):
            throw new InvalidDataException("Bound source assets manifest is absent.");
        if(binding.SourceAssetsSha256.Length!=64||Data.FileHash(sourceAssets,budget)!=binding.SourceAssetsSha256)throw new InvalidDataException("Bound source assets manifest SHA differs.");
        using var reader=File.OpenText(ledger);
        for(var i=0;i<1000000;i++)
        {
            budget.Check();var line=reader.ReadLine();if(line is null)return;
            if(line.Length>8192)throw new InvalidDataException("Exclusion row bound.");
            var row=JsonSerializer.Deserialize(line,TrainerJson.Default.CorpusExclusion)??throw new InvalidDataException("Null exclusion.");
            if(string.IsNullOrWhiteSpace(row.RecordId)||!_excluded.Add(row.RecordId))throw new InvalidDataException("Invalid/duplicate exclusion identity.");
        }
        if(reader.ReadLine() is not null)throw new InvalidDataException("Exclusion item bound.");
    }
    internal IEnumerable<(TrainingRow Row,string Source,bool Excluded)> Rows(RunBudget budget,string? sourceFilter=null,string? splitFilter=null)
    {
        foreach(var shard in Manifest.Shards)
        {
            if((sourceFilter is not null&&shard.Source!=sourceFilter)||(splitFilter is not null&&shard.Split!=splitFilter))continue;
            budget.Check();var path=Data.Within(_root,shard.Path);
            if(shard.Records is <1 or >50000||shard.Bytes is <1 or >2L*1024*1024*1024||new FileInfo(path).Length!=shard.Bytes||Data.FileHash(path,budget)!=shard.Sha256||shard.Split is not("train" or "development"))throw new InvalidDataException("Shard identity/count/split differs.");
            using var compressed=File.OpenRead(path);using var gzip=new GZipStream(compressed,CompressionMode.Decompress);
            using var reader=new StreamReader(gzip,new UTF8Encoding(false,true));
            for(long i=0;i<shard.Records;i++)
            {
                budget.Check();var line=reader.ReadLine()??throw new InvalidDataException("Truncated shard.");
                if(line.Length>4*1024*1024)throw new InvalidDataException("Corpus row character bound.");
                var row=JsonSerializer.Deserialize(line,TrainerJson.Default.TrainingRow)??throw new InvalidDataException("Null corpus record.");
                Validate(row);if(row.Split!=shard.Split)throw new InvalidDataException("Row/shard split differs.");
                yield return(row,shard.Source,_excluded.Contains(row.RecordId));
            }
            if(reader.ReadLine() is not null)throw new InvalidDataException("Shard denominator differs.");
        }
    }
    internal static void Validate(TrainingRow row)
    {
        if(string.IsNullOrWhiteSpace(row.RecordId)||row.Family.Length!=64||row.SourceLineSha256.Length!=64||row.SourceRow<1||row.State.Length is <1 or >262144||string.IsNullOrWhiteSpace(row.Instruction)||row.Instruction.Length>65536||row.Split is not("train" or "development"))throw new InvalidDataException("Corpus record metadata bounds.");
        if(row.Candidates.Length is <2 or >32||row.Candidates.Length!=row.Target.Length||row.Candidates.Any(c=>string.IsNullOrWhiteSpace(c.Id)||string.IsNullOrWhiteSpace(c.Text))||row.Candidates.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=row.Candidates.Length||row.Target.Any(p=>!float.IsFinite(p)||p<0)||Math.Abs(row.Target.Sum(p=>(double)p)-1)>2e-5)throw new InvalidDataException("Corpus candidate/target mapping invalid.");
        if(row.Type==MarkerQuestionType.Boolean&&(row.Candidates.Length!=2||row.Candidates[0].Id!="false"||row.Candidates[1].Id!="true"))throw new InvalidDataException("Boolean false/true ordering differs.");
        if(row.Type==MarkerQuestionType.Score&&(row.Candidates.Length>10||row.Candidates.Where((c,i)=>c.Id!=i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Any()))throw new InvalidDataException("Ascending score identity differs.");
        if(row.Type is not(MarkerQuestionType.Choice or MarkerQuestionType.Boolean or MarkerQuestionType.Score))throw new InvalidDataException("Unsupported corpus question type.");
    }
    internal CorpusCheck Check(long maximum,RunBudget budget)
    {
        if(Manifest.RecordsWritten>maximum)throw new InvalidDataException("Corpus exceeds explicit row budget; no silent prefix accepted.");
        var families=new Dictionary<string,string>(StringComparer.Ordinal);var counts=new Dictionary<string,long>(StringComparer.Ordinal);
        var targetMass=new Dictionary<string,double[]>(StringComparer.Ordinal);var targetCount=new Dictionary<string,long>(StringComparer.Ordinal);var oneHotCount=new Dictionary<string,long>(StringComparer.Ordinal);
        long physical=0,excluded=0,train=0,development=0;
        foreach(var item in Rows(budget))
        {
            physical++;if(item.Excluded){excluded++;continue;}
            var row=item.Row;
            if(families.TryGetValue(row.Family,out var split)){if(split!=row.Split)throw new InvalidDataException("Family crossed train/development.");}
            else{if(families.Count>=4000000)throw new InvalidDataException("Family index item budget4M.");families.Add(row.Family,row.Split);}
            if(row.Split=="train")train++;else development++;
            var key=item.Source+"|"+row.Split+"|"+row.Type;counts[key]=counts.GetValueOrDefault(key)+1;
            var targetKey=key+"|candidates="+row.Target.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(!targetMass.TryGetValue(targetKey,out var mass)){mass=new double[row.Target.Length];targetMass.Add(targetKey,mass);}
            for(var i=0;i<mass.Length;i++)mass[i]+=row.Target[i];
            targetCount[targetKey]=targetCount.GetValueOrDefault(targetKey)+1;
            if(row.Target.Count(p=>p==1f)==1&&row.Target.All(p=>p==0f||p==1f))oneHotCount[targetKey]=oneHotCount.GetValueOrDefault(targetKey)+1;
            if(physical%100000==0)Console.WriteLine($"corpus checked={physical}/{Manifest.RecordsWritten}");
        }
        if(physical!=Manifest.RecordsWritten||excluded!=_excluded.Count)throw new InvalidDataException("Physical/excluded denominator differs.");
        var distributions=targetMass.ToDictionary(p=>p.Key,p=>new CorpusTargetStats(targetCount[p.Key],oneHotCount.GetValueOrDefault(p.Key),p.Value.Select(v=>v/targetCount[p.Key]).ToArray()),StringComparer.Ordinal);
        return new("PASS_structural_streaming_only",ManifestSha256,ExclusionsSha256,physical,excluded,train,development,families.Count,Manifest.Shards.Length,false,false,false,"Exact supplied family isolation checked; cross-source/semantic families and original benchmark leakage require separate review. Target mass is supervision balance, not accuracy or calibrated probability.",counts,distributions);
    }
    internal void Export(string output,string datasetCommit,int maximumPerSource,RunBudget budget)
    {
        if(Directory.Exists(output)||File.Exists(output)||datasetCommit.Length!=40||!datasetCommit.All(char.IsAsciiHexDigit))throw new InvalidDataException("Fresh output and pinned source dataset commit required.");
        Directory.CreateDirectory(output);var counts=new Dictionary<string,int>(StringComparer.Ordinal);var families=new Dictionary<string,string>(StringComparer.Ordinal);int total=0,train=0,development=0;
        var records=Data.Within(output,"training-records.jsonl");var ledger=Data.Within(output,"derivation-ledger.jsonl");
        using(var writer=new StreamWriter(new FileStream(records,FileMode.CreateNew),new UTF8Encoding(false)))
        using(var derivation=new StreamWriter(new FileStream(ledger,FileMode.CreateNew),new UTF8Encoding(false)))
        foreach(var item in Rows(budget))
        {
            var countKey=item.Source+"|"+item.Row.Split;
            var limit=item.Row.Split=="development"?Math.Max(1,maximumPerSource/5):maximumPerSource-Math.Max(1,maximumPerSource/5);
            if(item.Excluded||counts.GetValueOrDefault(countKey)>=limit)continue;
            if(total>=1000000)throw new InvalidDataException("Legacy trainer subset bound1M; complete corpus needs streaming training scheduling.");
            var row=item.Row;
            if(families.TryGetValue(row.Family,out var split)&&split!=row.Split)throw new InvalidDataException("Export family isolation differs.");families[row.Family]=row.Split;
            counts[countKey]=counts.GetValueOrDefault(countKey)+1;total++;if(row.Split=="train")train++;else development++;
            writer.WriteLine(JsonSerializer.Serialize(row,TrainerJson.Default.TrainingRow));
            derivation.WriteLine(JsonSerializer.Serialize(new LedgerRow(row.SourceRow,row.SourceLineSha256,row.RecordId,"accepted","explicit_multisource_bounded_subset;source="+item.Source),TrainerJson.Default.LedgerRow));
        }
        if(train==0||development==0)throw new InvalidDataException("Both training and development required.");
        var manifest=new DataManifest(2,"private_source_repository",datasetCommit,"sezika/mixed-decision-corpus",ManifestSha256,"manifest.json",ManifestSha256,"component_specific_see_source-rights.json","source_specific_reference_semantics;not_calibrated_accuracy",Data.FileHash(records,budget),Data.FileHash(ledger,budget),total,train,development,0,0,"supplied_family_holdout;explicit_bounded_subset;no_sealed_test_claim",false){ParentManifestSha256=ManifestSha256};
        Data.WriteNew(Data.Within(output,"data-manifest.json"),manifest,TrainerJson.Default.DataManifest);
        File.WriteAllText(Data.Within(output,"SOURCE-README.md"),$"Mixed corpus subset. Original manifest SHA: {ManifestSha256}; exclusions SHA: {ExclusionsSha256}. Maximum per source: {maximumPerSource}. Source-specific label semantics, token lengths and GPU training are not accepted.\n",new UTF8Encoding(false));
        File.Copy(Data.Within(_root,"source-rights.json"),Data.Within(output,"source-rights.json"),false);
        Console.WriteLine($"exported explicit bounded subset={total},train={train},development={development};GPU training=NOT_RUN");
    }
}
