using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.RepositoryTrainer;

internal sealed record TrainingCandidate(string Id,string Text);
internal sealed record CorpusRowSource(string Dataset,string? Revision,string Path,string FileSha256,string OriginalSplit,int Row);
internal sealed record TrainingRow(string RecordId,string Family,string OriginalStateId,string Split,string Language,
    MarkerQuestionType Type,string State,string Instruction,TrainingCandidate[] Candidates,float[] Target,string SourceLineSha256,int SourceRow)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CorpusRowSource? Source {get;init;}
}
internal sealed record LedgerRow(int SourceRow,string SourceLineSha256,string? RecordId,string Action,string Reason);
internal sealed record DataManifest(int SchemaVersion,string DatasetRepository,string DatasetCommit,string SourceDataset,string SourceRevision,
    string SourcePath,string SourceSha256,string License,string LabelSemantics,string RecordsSha256,string LedgerSha256,int OriginalRows,
    int Train,int Development,int Quarantined,int DuplicateRows,string SplitMethod,bool FormalTrainingAdmitted)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? ParentManifestSha256 { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? StrictLengthReportSha256 { get; init; }
}
internal sealed record LengthRow(string RecordId,int? Tokens,bool Accepted,string? Failure);
internal sealed record LengthReport(string Status,int MaximumTokens,int Total,int Accepted,int Rejected,string DataManifestSha256,LengthRow[] Rows);

internal static class Data
{
    internal const string SourcePath="raw/hf/kaivoss/system-one-270m-data/train.jsonl";
    internal const string RawSha256="c49ef3295387765178b29cdbd1565413ba6e04ed20880e72352f6a5834d7cf94";
    internal static string Hash(ReadOnlySpan<byte> value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    internal static string Hash(string value)=>Hash(Encoding.UTF8.GetBytes(value));
    internal static string FileHash(string path,RunBudget budget)
    {
        using var stream=File.OpenRead(path);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[4*1024*1024];
        var maximum=(stream.Length+buffer.Length-1)/buffer.Length+1;if(maximum>16385)throw new InvalidDataException("File hash byte bound64GiB.");
        for(long i=0;i<maximum;i++){budget.Check();var read=stream.Read(buffer);if(read==0)return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();hash.AppendData(buffer,0,read);}
        throw new IOException("File changed during bounded hash.");
    }
    internal static string Within(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Contains("..",StringComparison.Ordinal))throw new IOException("Unsafe relative path.");
        var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;var full=Path.GetFullPath(Path.Combine(prefix,relative));
        if(!full.StartsWith(prefix,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Path escaped root.");
        var cursor=full;var watch=System.Diagnostics.Stopwatch.StartNew();
        for(var depth=0;depth<64;depth++)
        {
            if(watch.Elapsed.TotalSeconds>=5)throw new IOException("Path ancestry deadline.");
            if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked path rejected.");
            var parent=Path.GetDirectoryName(cursor);if(parent is null)return full;cursor=parent;
        }
        throw new IOException("Path ancestry limit.");
    }
    internal static DataManifest Prepare(string repository,string output,string commit,int maximum,bool pilot,RunBudget budget)
    {
        if(commit.Length!=40||!commit.All(char.IsAsciiHexDigit)||maximum is <1 or >1000000)throw new ArgumentException("Pinned commit and record bound required.");
        if(Directory.Exists(output))throw new IOException("Derived output exists; preserve it.");
        using var assets=JsonDocument.Parse(File.ReadAllBytes(Within(repository,"ASSET_MANIFEST.json")));
        var asset=assets.RootElement.EnumerateArray().Single(a=>a.GetProperty("path").GetString()==SourcePath);
        if(asset.GetProperty("declared_license").GetString()!="Apache-2.0")throw new InvalidDataException("Source license changed; review it before conversion.");
        var raw=Within(repository,SourcePath);
        if(asset.GetProperty("sha256").GetString()!=RawSha256||new FileInfo(raw).Length!=asset.GetProperty("bytes").GetInt64()||FileHash(raw,budget)!=RawSha256)throw new InvalidDataException("Pinned raw source bytes/hash mismatch or LFS pointer.");
        using var rights=JsonDocument.Parse(File.ReadAllBytes(Within(repository,"RIGHTS_MANIFEST.json")));
        var right=rights.RootElement.EnumerateArray().Single(a=>a.GetProperty("dataset").GetString()=="kaivoss/system-one-270m-data");
        if(right.GetProperty("decision").GetString()!="eligible_under_declared_terms_with_attribution")throw new InvalidDataException("Source requires a new rights review.");
        Directory.CreateDirectory(output);var ledgerPath=Within(output,"derivation-ledger.jsonl");
        using var reader=new StreamReader(raw,Encoding.UTF8,detectEncodingFromByteOrderMarks:true);
        var accepted=new Dictionary<string,TrainingRow>(StringComparer.Ordinal);var conflicts=new HashSet<string>(StringComparer.Ordinal);
        var ledger=new List<LedgerRow>();var members=new Dictionary<string,List<int>>(StringComparer.Ordinal);
        var original=0;var reachedEof=false;
        for(var line=1;line<=maximum;line++)
        {
            budget.Check();var text=reader.ReadLine();if(text is null){reachedEof=true;break;}original++;
            if(text.Length>262144)throw new InvalidDataException("Source row exceeds256KiB character bound.");
            var sourceHash=Hash(text);TrainingRow? row=null;var action="quarantined";var reason="";
            try
            {
                row=ParseSource(text,line,sourceHash);
                if(conflicts.Contains(row.RecordId)){reason="conflicting_duplicate_family_question";}
                else if(accepted.TryGetValue(row.RecordId,out var old))
                {
                    if(!old.Target.SequenceEqual(row.Target))
                    {
                        accepted.Remove(row.RecordId);conflicts.Add(row.RecordId);reason="conflicting_targets_all_excluded";
                        foreach(var index in members[row.RecordId]){budget.Check();ledger[index]=ledger[index] with{Action="quarantined",Reason=reason};}
                    }
                    else{action="duplicate";reason="exact_question_and_target";}
                }
                else{accepted.Add(row.RecordId,row);action="accepted";reason="original_train_family_holdout_soft_targets_preserved";}
                if(!members.TryGetValue(row.RecordId,out var indexes)){indexes=[];members.Add(row.RecordId,indexes);}indexes.Add(ledger.Count);
            }
            catch(Exception error)when(error is JsonException or InvalidDataException or KeyNotFoundException or FormatException or InvalidOperationException)
            {reason=error.Message;}
            ledger.Add(new LedgerRow(line,sourceHash,row?.RecordId,action,reason));
            if(line%1000==0)Console.WriteLine($"data conversion {line}/{maximum},accepted={accepted.Count}");
            if(pilot&&line==2)break;
        }
        if(!pilot&&!reachedEof&&reader.ReadLine() is not null)throw new InvalidDataException("Source exceeds explicit record budget; increase it instead of silently truncating.");
        using(var writer=new StreamWriter(new FileStream(ledgerPath,FileMode.CreateNew),new UTF8Encoding(false)))foreach(var item in ledger){budget.Check();writer.WriteLine(JsonSerializer.Serialize(item,TrainerJson.Default.LedgerRow));}
        var quarantined=ledger.Count(r=>r.Action=="quarantined");var duplicates=ledger.Count(r=>r.Action=="duplicate");
        var rows=accepted.Values.OrderBy(r=>r.Family,StringComparer.Ordinal).ThenBy(r=>r.RecordId,StringComparer.Ordinal).ToArray();
        if(original!=rows.Length+quarantined+duplicates)throw new InvalidDataException("Original-row denominator does not reconcile.");
        var recordsPath=Within(output,"training-records.jsonl");using(var writer=new StreamWriter(new FileStream(recordsPath,FileMode.CreateNew),new UTF8Encoding(false)))foreach(var row in rows){budget.Check();writer.WriteLine(JsonSerializer.Serialize(row,TrainerJson.Default.TrainingRow));}
        var manifest=new DataManifest(1,"https://moark.com/maikebing/public-decision-data",commit,"kaivoss/system-one-270m-data",asset.GetProperty("source_revision").GetString()!,SourcePath,asset.GetProperty("sha256").GetString()!,"Apache-2.0","original_teacher_soft_target_distribution_not_measured_correctness",FileHash(recordsPath,budget),FileHash(ledgerPath,budget),original,rows.Count(r=>r.Split=="train"),rows.Count(r=>r.Split=="development"),quarantined,duplicates,"SHA256(normalized_original_state)mod10<2_development;original_train_only;no_test_consumed",false);
        if(!pilot&&(manifest.Train==0||manifest.Development==0))throw new InvalidDataException("Both train and held-out families are required.");
        WriteNew(Within(output,"data-manifest.json"),manifest,TrainerJson.Default.DataManifest);
        AttachNotices(repository,output,budget);return manifest;
    }
    private static TrainingRow ParseSource(string text,int line,string sourceHash)
    {
        using var document=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=32});var root=document.RootElement;
        var prompt=root.GetProperty("prompt").GetString()??throw new InvalidDataException("Missing prompt.");
        var start=prompt.IndexOf("<state>\n",StringComparison.Ordinal);var end=prompt.IndexOf("\n</state>",StringComparison.Ordinal);var question=prompt.IndexOf("\n\nQuestion: ",StringComparison.Ordinal);var options=prompt.IndexOf("\nOptions:\n",StringComparison.Ordinal);var answer=prompt.IndexOf("\n\nAnswer with one letter.",StringComparison.Ordinal);
        if(start!=0||end<8||question!=end+9||options<=question||answer<=options)throw new InvalidDataException("Unsupported original prompt layout; preserve and quarantine.");
        var state=prompt[8..end];var instruction=prompt[(question+12)..options];
        var letters=root.GetProperty("letters").EnumerateArray().Select(v=>v.GetString()!).ToArray();var target=root.GetProperty("target").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
        if(letters.Length is <2 or >32||letters.Distinct().Count()!=letters.Length||target.Length!=letters.Length||target.Any(v=>!float.IsFinite(v)||v<0)||Math.Abs(target.Sum()-1)>1e-5)throw new InvalidDataException("Candidate/soft-label contract invalid.");
        var optionLines=prompt[(options+10)..answer].Split('\n');if(optionLines.Length!=letters.Length)throw new InvalidDataException("Original option boundaries differ.");
        var candidates=optionLines.Select((option,i)=>option.StartsWith(letters[i]+". ",StringComparison.Ordinal)?new TrainingCandidate(letters[i],option[(letters[i].Length+2)..]):throw new InvalidDataException("Candidate identity differs.")).ToArray();
        var type=root.GetProperty("qtype").GetString()switch{"choice"=>MarkerQuestionType.Choice,"noul"=>MarkerQuestionType.Boolean,"score"=>MarkerQuestionType.Score,_=>throw new InvalidDataException("Unsupported original qtype.")};
        if(type==MarkerQuestionType.Boolean&&(candidates.Length!=2||!candidates[0].Text.StartsWith("no - ",StringComparison.OrdinalIgnoreCase)||!candidates[1].Text.StartsWith("yes - ",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Boolean false/true semantics require reviewed mapping.");
        if(type==MarkerQuestionType.Score&&(candidates.Length>10||candidates.Where((c,i)=>!c.Text.StartsWith((i+1).ToString(CultureInfo.InvariantCulture)+" - ",StringComparison.Ordinal)).Any()))throw new InvalidDataException("Ordinal score mapping requires reviewed ascending levels.");
        var family=Hash(state.Normalize(NormalizationForm.FormC).Replace("\r\n","\n",StringComparison.Ordinal));var holdout=uint.Parse(family[..8],NumberStyles.HexNumber,CultureInfo.InvariantCulture)%10<2;
        var key=Hash(family+"\n"+type+"\n"+instruction+"\n"+string.Join('\n',candidates.Select(c=>c.Id+"\t"+c.Text)));
        return new("repo-"+key,family,root.GetProperty("state_id").GetString()!,holdout?"development":"train","en",type,state,instruction,candidates,target,sourceHash,line);
    }
    internal static (DataManifest Manifest,TrainingRow[] Rows,string ManifestSha256) Open(string root,RunBudget budget)
    {
        var manifestPath=Within(root,"data-manifest.json");var bytes=File.ReadAllBytes(manifestPath);if(bytes.Length>1048576)throw new InvalidDataException("Manifest byte bound.");
        var manifest=JsonSerializer.Deserialize(bytes,TrainerJson.Default.DataManifest)??throw new InvalidDataException("Null manifest.");
        var path=Within(root,"training-records.jsonl");if(FileHash(path,budget)!=manifest.RecordsSha256||FileHash(Within(root,"derivation-ledger.jsonl"),budget)!=manifest.LedgerSha256)throw new InvalidDataException("Derived input changed.");
        var rows=new List<TrainingRow>();using var reader=File.OpenText(path);
        for(var i=0;i<1000000;i++){budget.Check();var line=reader.ReadLine();if(line is null)break;if(line.Length>262144)throw new InvalidDataException("Row bound.");var row=JsonSerializer.Deserialize(line,TrainerJson.Default.TrainingRow)??throw new InvalidDataException("Null row.");rows.Add(row);}
        var singleSource=manifest.SchemaVersion==1&&manifest.SourceSha256==RawSha256&&manifest.SourcePath==SourcePath&&manifest.License=="Apache-2.0";
        var mixedSource=manifest.SchemaVersion==2&&manifest.SourceDataset=="sezika/mixed-decision-corpus"&&manifest.SourcePath=="manifest.json"&&manifest.SourceSha256==manifest.ParentManifestSha256&&manifest.License=="component_specific_see_source-rights.json"&&!manifest.FormalTrainingAdmitted;
        if(reader.ReadLine() is not null||!(singleSource||mixedSource)||manifest.OriginalRows!=manifest.Train+manifest.Development+manifest.Quarantined+manifest.DuplicateRows||
            rows.Count!=manifest.Train+manifest.Development||rows.Count(r=>r.Split=="train")!=manifest.Train||rows.Count(r=>r.Split=="development")!=manifest.Development||rows.Select(r=>r.RecordId).Distinct().Count()!=rows.Count||rows.Any(r=>r.Split is not ("train" or "development"))||rows.GroupBy(r=>r.Family).Any(g=>g.Select(r=>r.Split).Distinct().Count()>1))throw new InvalidDataException("Counts, IDs, raw identity or family split isolation differ.");
        if(mixedSource)foreach(var row in rows){budget.Check();Corpus.Validate(row);}
        return(manifest,rows.ToArray(),Hash(bytes));
    }
    internal static DataManifest AdmitLengths(string root,string lengthPath,string output,RunBudget budget)
    {
        var input=Open(root,budget);var lengths=Assets.Read(lengthPath,TrainerJson.Default.LengthReport,32*1024*1024);
        if(lengths.DataManifestSha256!=input.ManifestSha256||lengths.Total!=input.Rows.Length||lengths.Rows.Length!=lengths.Total||lengths.Rows.Select(r=>r.RecordId).Distinct().Count()!=lengths.Total||
            !lengths.Rows.Select(r=>r.RecordId).Order(StringComparer.Ordinal).SequenceEqual(input.Rows.Select(r=>r.RecordId).Order(StringComparer.Ordinal)))throw new InvalidDataException("Strict length report identity/denominator differs.");
        if(Directory.Exists(output))throw new IOException("Admitted data output exists.");Directory.CreateDirectory(output);
        var excluded=lengths.Rows.Where(r=>!r.Accepted).ToDictionary(r=>r.RecordId,r=>r.Failure,StringComparer.Ordinal);var rows=input.Rows.Where(r=>!excluded.ContainsKey(r.RecordId)).ToArray();
        var path=Within(output,"training-records.jsonl");using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew),new UTF8Encoding(false)))foreach(var row in rows){budget.Check();writer.WriteLine(JsonSerializer.Serialize(row,TrainerJson.Default.TrainingRow));}
        var ledgerPath=Within(output,"derivation-ledger.jsonl");using(var reader=File.OpenText(Within(root,"derivation-ledger.jsonl")))using(var writer=new StreamWriter(new FileStream(ledgerPath,FileMode.CreateNew),new UTF8Encoding(false)))
        {
            for(var i=0;i<input.Manifest.OriginalRows;i++){budget.Check();var text=reader.ReadLine()??throw new InvalidDataException("Truncated original-row ledger.");var item=JsonSerializer.Deserialize(text,TrainerJson.Default.LedgerRow)??throw new InvalidDataException("Null ledger row.");if(item.Action=="accepted"&&item.RecordId is not null&&excluded.TryGetValue(item.RecordId,out var reason))item=item with{Action="quarantined",Reason="strict_length_excluded_original_text_preserved:"+reason};writer.WriteLine(JsonSerializer.Serialize(item,TrainerJson.Default.LedgerRow));}
            if(reader.ReadLine() is not null)throw new InvalidDataException("Ledger denominator differs.");
        }
        var manifest=input.Manifest with{RecordsSha256=FileHash(path,budget),LedgerSha256=FileHash(ledgerPath,budget),Train=rows.Count(r=>r.Split=="train"),Development=rows.Count(r=>r.Split=="development"),Quarantined=input.Manifest.Quarantined+excluded.Count,ParentManifestSha256=input.ManifestSha256,StrictLengthReportSha256=FileHash(lengthPath,budget)};
        WriteNew(Within(output,"data-manifest.json"),manifest,TrainerJson.Default.DataManifest);Assets.CopyNew(lengthPath,Within(output,"strict-lengths.json"),budget);
        foreach(var name in new[]{"SOURCE-README.md","source-rights.json"})Assets.CopyNew(Within(root,name),Within(output,name),budget);return manifest;
    }
    internal static void AttachNotices(string repository,string output,RunBudget budget)
    {
        budget.Check();var manifest=Assets.Read(Within(output,"data-manifest.json"),TrainerJson.Default.DataManifest);
        if(manifest.SourceDataset!="kaivoss/system-one-270m-data"||manifest.SourcePath!=SourcePath||manifest.SourceSha256!=RawSha256||manifest.License!="Apache-2.0")throw new InvalidDataException("Source notices do not match derived data identity.");
        foreach(var name in new[]{"SOURCE-README.md","source-rights.json"})if(File.Exists(Within(output,name)))throw new IOException("Source notices exist; preserve their bytes.");
        using var assets=JsonDocument.Parse(File.ReadAllBytes(Within(repository,"ASSET_MANIFEST.json")));
        var source=assets.RootElement.EnumerateArray().Single(a=>a.GetProperty("path").GetString()==SourcePath);
        if(source.GetProperty("sha256").GetString()!=manifest.SourceSha256||source.GetProperty("source_revision").GetString()!=manifest.SourceRevision)throw new InvalidDataException("Source notice revision differs from derived data.");
        var readme=SourcePath[..SourcePath.LastIndexOf('/')]+"/README.md";
        var asset=assets.RootElement.EnumerateArray().Single(a=>a.GetProperty("path").GetString()==readme);var path=Within(repository,readme);
        if(new FileInfo(path).Length!=asset.GetProperty("bytes").GetInt64()||FileHash(path,budget)!=asset.GetProperty("sha256").GetString())throw new InvalidDataException("Source notice bytes/hash differ.");
        using var rights=JsonDocument.Parse(File.ReadAllBytes(Within(repository,"RIGHTS_MANIFEST.json")));var right=rights.RootElement.EnumerateArray().Single(a=>a.GetProperty("dataset").GetString()=="kaivoss/system-one-270m-data");
        if(right.GetProperty("decision").GetString()!="eligible_under_declared_terms_with_attribution")throw new InvalidDataException("Source notice rights decision differs.");
        Assets.CopyNew(path,Within(output,"SOURCE-README.md"),budget);using var stream=new FileStream(Within(output,"source-rights.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None);var bytes=Encoding.UTF8.GetBytes(right.GetRawText());stream.Write(bytes);
    }
    internal static IndependentDecisionRequest Request(TrainingRow row,out IndependentDecisionQuestion question,bool rotate=false)
    {
        var candidates=rotate?row.Candidates.Skip(1).Concat(row.Candidates.Take(1)).ToArray():row.Candidates;
        question=row.Type switch
        {
            MarkerQuestionType.Choice=>new IndependentChoiceQuestion("decision",row.Instruction,candidates.Select(c=>new IndependentChoiceCandidate(c.Id,c.Text)).ToArray()),
            MarkerQuestionType.Boolean=>new IndependentBooleanQuestion("decision",row.Instruction,row.Instruction,candidates[0].Text,candidates[1].Text),
            MarkerQuestionType.Score=>new IndependentScoreQuestion("decision",row.Instruction,candidates.Select(c=>c.Text).ToArray()),
            _=>throw new InvalidDataException("Unsupported type.")
        };
        return new(1,IndependentModelLoader.ModelId,JsonSerializer.SerializeToElement(row.State,TrainerJson.Default.String),[question]);
    }
    internal static void WriteNew<T>(string path,T value,System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);JsonSerializer.Serialize(stream,value,info);}
}

[JsonSerializable(typeof(TrainingRow))]
[JsonSerializable(typeof(CorpusManifest))]
[JsonSerializable(typeof(CorpusExclusion))]
[JsonSerializable(typeof(CorpusCheck))]
[JsonSerializable(typeof(CorpusBindings))]
[JsonSerializable(typeof(DataManifest))]
[JsonSerializable(typeof(LedgerRow))]
[JsonSerializable(typeof(LengthReport))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(RunPlan))]
[JsonSerializable(typeof(InputIdentity))]
[JsonSerializable(typeof(CheckpointReceipt))]
[JsonSerializable(typeof(PackageReceipt))]
[JsonSerializable(typeof(ProfileReceipt))]
[JsonSerializable(typeof(TrainReceipt))]
[JsonSerializable(typeof(ValidationReceipt))]
[JsonSerializable(typeof(StorageReceipt))]
[JsonSerializable(typeof(EvaluationCase[]))]
[JsonSerializable(typeof(LoadReceipt))]
[JsonSerializable(typeof(EvaluationReceipt))]
[JsonSerializable(typeof(EvaluationRow))]
[JsonSourceGenerationOptions(WriteIndented=false,UseStringEnumConverter=true)]
internal partial class TrainerJson:JsonSerializerContext;
