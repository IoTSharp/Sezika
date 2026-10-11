using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sezika;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusScanIdentity(int SchemaVersion,string Algorithm,string ManifestSha256,string BindingsSha256,
    string OriginalExclusionsSha256,string TokenizerSha256,string SourcePolicySha256,int MaximumTokens,int ChunkRows,
    int MaximumExclusions,long PhysicalRows,int Shards,int Chunks);
internal sealed record CorpusScanChunk(string Status,CorpusScanIdentity Inputs,int Index,int ShardIndex,long FirstRow,long Rows,
    long OriginalExcluded,long SourceScopeExcluded,long TokenLimitExcluded,long Accepted,long Train,long Development,
    int LongestTokens,int LongestTrainTokens,string? LongestTrainId,string Source,string Split,bool ShardEofVerified,
    string ExclusionsPath,string ExclusionsSha256,string FamiliesPath,string FamiliesSha256);
internal sealed record CorpusScanReference(int Index,string Path,string Sha256);
internal sealed record CorpusScanCompletion(string Status,CorpusScanIdentity Inputs,string ScanPlanSha256,CorpusScanReference[] Chunks,
    long PhysicalRows,long OriginalExcluded,long SourceScopeExcluded,long TokenLimitExcluded,long Accepted,long Train,long Development,
    int LongestTokens,int LongestTrainTokens,string? LongestTrainId,string? LongestTrainSource,Dictionary<string,long> TrainingBySource,
    int Families,bool FamilyIsolationChecked,int ExclusionIdentities,string ExclusionsPath,string ExclusionsSha256,
    bool GpuFeaturesComputed,bool FormalTrainingAdmitted);
internal sealed record CorpusScanAttempt(string Status,int Attempt,int ProcessId,DateTime ProcessStartUtc,string ScanPlanSha256,
    int RequestedNewChunks,int NewChunks,int CompleteChunks,double ElapsedSeconds,string? Error);
internal sealed record CorpusScanExclusion([property:JsonPropertyName("record_id")]string RecordId,
    [property:JsonPropertyName("reason")]string Reason,[property:JsonPropertyName("category")]string Category,
    [property:JsonPropertyName("source")]string Source,[property:JsonPropertyName("split")]string Split);
internal sealed record CorpusLengthAdmission(int SchemaVersion,string Status,string ParentManifestSha256,string ParentBindingsSha256,
    string ScanCompletionSha256,string SourcePolicySha256,string ExclusionsSha256,long OriginalExcluded,long SourceScopeExcluded,long TokenLimitExcluded);

[JsonSerializable(typeof(CorpusScanIdentity))]
[JsonSerializable(typeof(CorpusScanChunk))]
[JsonSerializable(typeof(CorpusScanCompletion))]
[JsonSerializable(typeof(CorpusScanAttempt))]
[JsonSerializable(typeof(CorpusScanExclusion))]
[JsonSerializable(typeof(CorpusLengthAdmission))]
[JsonSourceGenerationOptions(WriteIndented=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
internal partial class CorpusLengthScanJson:JsonSerializerContext;

// Offline renderer/tokenizer preparation only. No model features or GPU work.
// Completed chunk receipts are immutable; an interrupted attempt is retained.
internal static class CorpusLengthScan
{
    private const int MaximumChunks=32768,MaximumAttempts=32768,MaximumFamilies=4000000;
    internal const string CompleteStatus="PASS_complete_strict_scan_explicit_exclusions_private_only";
    private const string Algorithm="strict_decision_v1_choice_rotation_chunk4096_explicit_source_and_token_exclusions_v1";
    private readonly record struct Spec(int Index,int Shard,long First,int Rows,bool Last);
    private readonly record struct FamilyKey(ulong A,ulong B,ulong C,ulong D);
    private static bool Sha(string value)=>value.Length==64&&value.All(char.IsAsciiHexDigit);
    private static string Relative(string value)=>value.Replace('\\','/');
    private static string ChunkPath(int index)=>"chunks/chunk-"+index.ToString("D5",System.Globalization.CultureInfo.InvariantCulture)+".json";

    private static Spec[] Specs(Corpus corpus,int chunkRows,RunBudget budget)
    {
        if(chunkRows is <1 or >50000)throw new InvalidDataException("Length chunk row bound1..50000.");
        var result=new List<Spec>();
        for(var shard=0;shard<corpus.Manifest.Shards.Length;shard++)
        {
            var rows=corpus.Manifest.Shards[shard].Records;
            for(long first=0;first<rows;first+=chunkRows)
            {
                budget.Check();if(result.Count>=MaximumChunks)throw new InvalidDataException("Length scan chunk bound32768; increase chunk rows explicitly.");
                var count=(int)Math.Min(chunkRows,rows-first);result.Add(new(result.Count,shard,first,count,first+count==rows));
            }
        }
        return result.ToArray();
    }

    private static void Atomic<T>(string path,T value,JsonTypeInfo<T> type,int attempt,RunBudget budget)
    {
        budget.Check();var pending=path+".attempt-"+attempt.ToString("D5",System.Globalization.CultureInfo.InvariantCulture)+".pending";
        using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(stream,value,type);stream.Flush(true);}
        budget.Check();File.Move(pending,path,false);
    }

    private static int Attempt(string output,RunBudget budget)
    {
        var directory=Data.Within(output,"attempts");Directory.CreateDirectory(directory);var maximum=-1;var count=0;
        foreach(var path in Directory.EnumerateDirectories(directory,"attempt-*",SearchOption.TopDirectoryOnly).Take(MaximumAttempts+1))
        {
            budget.Check();if(++count>MaximumAttempts)throw new InvalidDataException("Length scan attempt bound32768.");
            var name=Path.GetFileName(path);if(!int.TryParse(name.AsSpan(8),out var number)||number<0||number>=MaximumAttempts)throw new InvalidDataException("Length scan historical attempt identity differs.");maximum=Math.Max(maximum,number);
        }
        if(maximum+1>=MaximumAttempts)throw new InvalidDataException("Length scan attempt budget exhausted; preserve history.");
        return maximum+1;
    }

    internal static void Run(Corpus corpus,string bindingsSha,string tokenizerPath,TokenizerJson tokenizer,int max,long maximum,
        string output,bool resume,int chunkRows,int stageChunks,string policyPath,string policySha,
        Func<TrainingRow,string?> sourceScopeFailure,RunBudget budget,int maximumExclusions=1000000)
    {
        if(max is <2 or >8192||maximum is <1 or >50000000||corpus.Manifest.RecordsWritten>maximum||stageChunks is <1 or >MaximumChunks||maximumExclusions is <1 or >1000000||!Sha(policySha)||Data.FileHash(policyPath,budget)!=policySha)throw new InvalidDataException("Pinned private policy and explicit length/record/chunk/exclusion budgets required.");
        var specs=Specs(corpus,chunkRows,budget);var inputs=new CorpusScanIdentity(1,Algorithm,corpus.ManifestSha256,bindingsSha,corpus.ExclusionsSha256,
            Data.FileHash(tokenizerPath,budget),policySha,max,chunkRows,maximumExclusions,corpus.Manifest.RecordsWritten,corpus.Manifest.Shards.Length,specs.Length);
        if(!resume&&(Directory.Exists(output)||File.Exists(output)))throw new IOException("Fresh scan output required; resume must be explicit.");
        if(resume&&!Directory.Exists(output))throw new IOException("Pinned existing scan output required for resume.");
        Directory.CreateDirectory(output);Directory.CreateDirectory(Data.Within(output,"chunks"));
        using var ownership=new FileStream(Data.Within(output,"scan.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
        var planPath=Data.Within(output,"scan-plan.json");
        if(resume)
        {
            if(Assets.Read(planPath,CorpusLengthScanJson.Default.CorpusScanIdentity)!=inputs)throw new InvalidDataException("Length scan resume corpus/tokenizer/policy/chunk identity changed.");
        }
        else Atomic(planPath,inputs,CorpusLengthScanJson.Default.CorpusScanIdentity,0,budget);
        var planSha=Data.FileHash(planPath,budget);var attempt=Attempt(output,budget);var attemptRelative="attempts/attempt-"+attempt.ToString("D5",System.Globalization.CultureInfo.InvariantCulture);var attemptRoot=Data.Within(output,attemptRelative);Directory.CreateDirectory(attemptRoot);
        using var process=Process.GetCurrentProcess();var watch=Stopwatch.StartNew();var newChunks=0;var completeChunks=0;string? error=null;var status="PARTIAL_complete_chunks_only";
        Data.WriteNew(Data.Within(attemptRoot,"started.json"),new CorpusScanAttempt("STARTED",attempt,Environment.ProcessId,process.StartTime.ToUniversalTime(),planSha,stageChunks,0,0,0,null),CorpusLengthScanJson.Default.CorpusScanAttempt);
        try
        {
            var completed=new Dictionary<int,CorpusScanChunk>();var gap=false;
            foreach(var spec in specs)
            {
                budget.Check();var path=Data.Within(output,ChunkPath(spec.Index));
                if(!File.Exists(path)){gap=true;continue;}
                if(gap)throw new InvalidDataException("Length scan receipt prefix has a gap; preserve all artifacts.");
                var receipt=ReadChunk(corpus,output,inputs,spec,budget);completed.Add(spec.Index,receipt);completeChunks++;
            }
            var completionPath=Data.Within(output,"length-scan-complete.json");
            if(File.Exists(completionPath))
            {
                var already=Completion(output,Data.FileHash(completionPath,budget),corpus,bindingsSha,budget);
                if(already.Inputs!=inputs||completeChunks!=specs.Length)throw new InvalidDataException("Complete length scan is missing chunk receipts or inputs differ.");status="PASS_previously_completed_scan_verified";return;
            }
            for(var shard=0;shard<corpus.Manifest.Shards.Length&&newChunks<stageChunks;shard++)
            {
                budget.Check();var inShard=specs.Where(s=>s.Shard==shard).ToArray();var unfinished=inShard.FirstOrDefault(s=>!completed.ContainsKey(s.Index));
                if(inShard.All(s=>completed.ContainsKey(s.Index)))continue;
                using var rows=corpus.RowsForShard(shard,budget).GetEnumerator();
                for(long skipped=0;skipped<unfinished.First;skipped++){budget.Check();if(!rows.MoveNext())throw new InvalidDataException("Resume gzip prefix truncated.");}
                foreach(var spec in inShard.Where(s=>s.First>=unfinished.First))
                {
                    budget.Check();if(newChunks>=stageChunks)break;
                    var receipt=ScanChunk(corpus,inputs,spec,rows,output,attemptRelative,attempt,budget,tokenizer,sourceScopeFailure);
                    completed.Add(spec.Index,receipt);newChunks++;completeChunks++;
                    Console.WriteLine($"strict scan chunks={completeChunks}/{specs.Length}; shard={shard}; physical_chunk={spec.Rows};accepted={receipt.Accepted};scope={receipt.SourceScopeExcluded};token={receipt.TokenLimitExcluded};elapsed={watch.Elapsed.TotalSeconds:F1}s");
                }
            }
            if(completeChunks==specs.Length)
            {
                Finalize(corpus,inputs,planSha,specs,output,attemptRoot,attempt,budget);status=CompleteStatus;
            }
        }
        catch(Exception exception){error=exception.Message.Length<=2048?exception.Message:exception.Message[..2048];status="FAIL_attempt_preserved_not_complete";throw;}
        finally
        {
            // A small diagnostic receipt remains writable after cancellation;
            // it cannot create a completion marker or change pinned inputs.
            try{Data.WriteNew(Data.Within(attemptRoot,"finished.json"),new CorpusScanAttempt(status,attempt,Environment.ProcessId,process.StartTime.ToUniversalTime(),planSha,stageChunks,newChunks,completeChunks,watch.Elapsed.TotalSeconds,error),CorpusLengthScanJson.Default.CorpusScanAttempt);}catch(IOException){ }
        }
    }

    private static CorpusScanChunk ScanChunk(Corpus corpus,CorpusScanIdentity inputs,Spec spec,IEnumerator<(TrainingRow Row,string Source,bool Excluded)> rows,
        string output,string attemptRelative,int attempt,RunBudget budget,TokenizerJson tokenizer,Func<TrainingRow,string?> scope)
    {
        var relative=attemptRelative+"/chunk-"+spec.Index.ToString("D5",System.Globalization.CultureInfo.InvariantCulture);var directory=Data.Within(output,relative);Directory.CreateDirectory(directory);
        var exclusionsRelative=relative+"/exclusions.jsonl";var familiesRelative=relative+"/families.bin";
        long original=0,source=0,tokens=0,accepted=0,train=0,development=0;int longest=0,longestTrain=0;string? longestId=null;
        var shard=corpus.Manifest.Shards[spec.Shard];
        using(var exclusions=new FileStream(Data.Within(output,exclusionsRelative),FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer=new StreamWriter(exclusions,new UTF8Encoding(false),4096,true))
        using(var families=new FileStream(Data.Within(output,familiesRelative),FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            for(var rowNumber=0;rowNumber<spec.Rows;rowNumber++)
            {
                budget.Check();if(!rows.MoveNext())throw new InvalidDataException("Scan chunk gzip denominator truncated.");var item=rows.Current;
                if(item.Source!=shard.Source||item.Row.Split!=shard.Split)throw new InvalidDataException("Scan shard/source/split identity changed.");
                if(item.Excluded){original++;continue;}
                var failure=scope(item.Row);
                if(failure is not null)
                {
                    if(string.IsNullOrWhiteSpace(failure)||failure.Length>512)throw new InvalidDataException("Source scope exclusion requires a bounded explicit reason.");
                    source++;Exclude(writer,item,"source_scope",failure);continue;
                }
                int length;
                try
                {
                    length=Workflow.Sequence(tokenizer,item.Row,inputs.MaximumTokens,false,budget).Sequence.TokenIds.Length;
                    if(item.Row.Type==MarkerQuestionType.Choice)length=Math.Max(length,Workflow.Sequence(tokenizer,item.Row,inputs.MaximumTokens,true,budget).Sequence.TokenIds.Length);
                }
                catch(DecisionException exception) when(exception.Code is "decision_v1_token_budget_exceeded" or "decision_token_limit_exceeded")
                {tokens++;Exclude(writer,item,"token_limit",exception.Code+";strict_max="+inputs.MaximumTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));continue;}
                catch(DecisionException exception)
                {
                    static string Bounded(string value)=>value.Length<=512?value:value[..512];
                    throw new InvalidDataException($"Strict renderer failure; source={Bounded(item.Source)};split={item.Row.Split};record_id={Bounded(item.Row.RecordId)};shard={spec.Shard};row={spec.First+rowNumber};code={exception.Code};no exclusion admitted.");
                }
                accepted++;longest=Math.Max(longest,length);
                if(item.Row.Split=="train"){train++;if(length>longestTrain){longestTrain=length;longestId=item.Row.RecordId;}}else development++;
                if(!Sha(item.Row.Family))throw new InvalidDataException("Admitted Family must be an exact 256-bit supplied identity.");
                families.Write(Convert.FromHexString(item.Row.Family));families.WriteByte(item.Row.Split=="train"?(byte)0:(byte)1);
                if(rowNumber%512==511)Console.WriteLine($"strict chunk={spec.Index};rows={rowNumber+1}/{spec.Rows};remaining={budget.RemainingSeconds:F0}s");
            }
            if(spec.Last&&rows.MoveNext())throw new InvalidDataException("Length scan shard has extra rows after pinned denominator.");
            writer.Flush();exclusions.Flush(true);families.Flush(true);
        }
        var receipt=new CorpusScanChunk("PASS_complete_chunk_strict_renderer_only",inputs,spec.Index,spec.Shard,spec.First,spec.Rows,original,source,tokens,accepted,train,development,longest,longestTrain,longestId,shard.Source,shard.Split,spec.Last,
            exclusionsRelative,Data.FileHash(Data.Within(output,exclusionsRelative),budget),familiesRelative,Data.FileHash(Data.Within(output,familiesRelative),budget));
        Atomic(Data.Within(output,ChunkPath(spec.Index)),receipt,CorpusLengthScanJson.Default.CorpusScanChunk,attempt,budget);return receipt;
    }

    private static void Exclude(StreamWriter writer,(TrainingRow Row,string Source,bool Excluded) item,string category,string reason)
    {
        if(item.Row.RecordId.Length>512)throw new InvalidDataException("Explicit exclusion RecordId bound512.");
        writer.WriteLine(JsonSerializer.Serialize(new CorpusScanExclusion(item.Row.RecordId,reason,category,item.Source,item.Row.Split),CorpusLengthScanJson.Default.CorpusScanExclusion));
    }

    private static CorpusScanChunk ReadChunk(Corpus corpus,string output,CorpusScanIdentity inputs,Spec spec,RunBudget budget)
    {
        var receipt=Assets.Read(Data.Within(output,ChunkPath(spec.Index)),CorpusLengthScanJson.Default.CorpusScanChunk);
        var shard=corpus.Manifest.Shards[spec.Shard];
        if(receipt.Status!="PASS_complete_chunk_strict_renderer_only"||receipt.Inputs!=inputs||receipt.Index!=spec.Index||receipt.ShardIndex!=spec.Shard||receipt.FirstRow!=spec.First||receipt.Rows!=spec.Rows||receipt.ShardEofVerified!=spec.Last||
            receipt.Source!=shard.Source||receipt.Split!=shard.Split||(shard.Split=="train"?receipt.Development!=0:receipt.Train!=0)||
            receipt.Rows!=receipt.OriginalExcluded+receipt.SourceScopeExcluded+receipt.TokenLimitExcluded+receipt.Accepted||receipt.Accepted!=receipt.Train+receipt.Development||
            new[]{receipt.OriginalExcluded,receipt.SourceScopeExcluded,receipt.TokenLimitExcluded,receipt.Accepted,receipt.Train,receipt.Development}.Any(v=>v<0)||receipt.LongestTokens>inputs.MaximumTokens||receipt.LongestTokens<0||receipt.LongestTrainTokens>receipt.LongestTokens||receipt.LongestTrainTokens<0)
            throw new InvalidDataException("Length chunk receipt/identity/denominator differs.");
        if(!receipt.ExclusionsPath.StartsWith("attempts/attempt-",StringComparison.Ordinal)||!receipt.FamiliesPath.StartsWith("attempts/attempt-",StringComparison.Ordinal)||
            new FileInfo(Data.Within(output,receipt.FamiliesPath)).Length!=receipt.Accepted*33||Data.FileHash(Data.Within(output,receipt.FamiliesPath),budget)!=receipt.FamiliesSha256||Data.FileHash(Data.Within(output,receipt.ExclusionsPath),budget)!=receipt.ExclusionsSha256)
            throw new InvalidDataException("Completed chunk sidecar bytes/SHA changed.");
        return receipt;
    }

    private static int Families(IEnumerable<CorpusScanChunk> chunks,string output,RunBudget budget)
    {
        var values=new Dictionary<FamilyKey,byte>();var bytes=new byte[33];long rows=0;
        foreach(var chunk in chunks)
        {
            budget.Check();using var stream=File.OpenRead(Data.Within(output,chunk.FamiliesPath));
            for(long index=0;index<chunk.Accepted;index++)
            {
                budget.Check();stream.ReadExactly(bytes);var split=bytes[32];if(split>1)throw new InvalidDataException("Family split sidecar invalid.");
                var key=new FamilyKey(BinaryPrimitives.ReadUInt64LittleEndian(bytes),BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)),BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16)),BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24)));
                if(values.TryGetValue(key,out var old)){if(old!=split)throw new InvalidDataException("Admitted Family crossed train/development.");}
                else{if(values.Count>=MaximumFamilies)throw new InvalidDataException("Exact Family index bound4M; no silent drop.");values.Add(key,split);}
                if(++rows%1000000==0)Console.WriteLine($"family exact review={rows};distinct={values.Count};remaining={budget.RemainingSeconds:F0}s");
            }
            if(stream.ReadByte()!=-1)throw new InvalidDataException("Family sidecar has trailing rows.");
        }
        return values.Count;
    }

    private static void Finalize(Corpus corpus,CorpusScanIdentity inputs,string planSha,Spec[] specs,string output,string attemptRoot,int attempt,RunBudget budget)
    {
        var chunks=specs.Select(s=>ReadChunk(corpus,output,inputs,s,budget)).ToArray();var familyCount=Families(chunks,output,budget);
        long physical=chunks.Sum(c=>c.Rows),original=chunks.Sum(c=>c.OriginalExcluded),scope=chunks.Sum(c=>c.SourceScopeExcluded),token=chunks.Sum(c=>c.TokenLimitExcluded),accepted=chunks.Sum(c=>c.Accepted),train=chunks.Sum(c=>c.Train),development=chunks.Sum(c=>c.Development);
        if(physical!=inputs.PhysicalRows||physical!=original+scope+token+accepted||accepted!=train+development||train<1||development<1)throw new InvalidDataException("Complete length scan physical/admitted denominator differs.");
        var identifiers=new HashSet<string>(StringComparer.Ordinal);var pendingLedger=Data.Within(attemptRoot,"complete-exclusions.jsonl");long originalIds=0,extraIds=0;
        using(var stream=new FileStream(pendingLedger,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer=new StreamWriter(stream,new UTF8Encoding(false),4096,true))
        {
            using(var reader=File.OpenText(Data.Within(corpus.Root,"exclusions.jsonl")))
            for(var index=0;index<=inputs.MaximumExclusions;index++)
            {
                budget.Check();var line=reader.ReadLine();if(line is null)break;if(line.Length>8192)throw new InvalidDataException("Original exclusion line bound8192.");
                var row=JsonSerializer.Deserialize(line,TrainerJson.Default.CorpusExclusion)??throw new InvalidDataException("Null original exclusion.");
                if(identifiers.Count>=inputs.MaximumExclusions||!identifiers.Add(row.RecordId))throw new InvalidDataException("Total explicit exclusion identity limit or duplicate original ID; no truncation.");
                originalIds++;writer.WriteLine(line);
            }
            foreach(var chunk in chunks)
            {
                using var reader=File.OpenText(Data.Within(output,chunk.ExclusionsPath));long seen=0;
                for(long index=0;index<=chunk.Rows;index++)
                {
                    budget.Check();var line=reader.ReadLine();if(line is null)break;if(line.Length>4096)throw new InvalidDataException("Additional exclusion line bound4096.");
                    var row=JsonSerializer.Deserialize(line,CorpusLengthScanJson.Default.CorpusScanExclusion)??throw new InvalidDataException("Null explicit length/source exclusion.");
                    if(row.Category is not("source_scope" or "token_limit")||row.Source!=chunk.Source||row.Split!=chunk.Split||identifiers.Count>=inputs.MaximumExclusions||!identifiers.Add(row.RecordId))throw new InvalidDataException("Total original+scope+token exclusion bound or identity/category differs; no truncation.");
                    seen++;extraIds++;writer.WriteLine(line);
                }
                if(seen!=chunk.SourceScopeExcluded+chunk.TokenLimitExcluded)throw new InvalidDataException("Chunk exclusion ledger denominator differs.");
            }
            writer.Flush();stream.Flush(true);
        }
        if(originalIds!=original||extraIds!=scope+token||identifiers.Count!=original+scope+token||Data.FileHash(Data.Within(corpus.Root,"exclusions.jsonl"),budget)!=inputs.OriginalExclusionsSha256)throw new InvalidDataException("Original and derived exclusion coverage differs.");
        var ledger=Data.Within(output,"scan-exclusions.jsonl");budget.Check();
        if(File.Exists(ledger))
        {
            // A crash may leave the ledger after its durable move but before
            // completion. Reuse exact bytes; retain this attempt's copy.
            if(Data.FileHash(ledger,budget)!=Data.FileHash(pendingLedger,budget))throw new InvalidDataException("Historical full exclusion ledger differs; preserve both attempts.");
        }
        else File.Move(pendingLedger,ledger,false);
        var longest=chunks.Max(c=>c.LongestTokens);var longestTrain=chunks.Where(c=>c.Train>0).OrderByDescending(c=>c.LongestTrainTokens).ThenBy(c=>c.Index).First();
        var bySource=chunks.Where(c=>c.Train>0).GroupBy(c=>c.Source,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Sum(c=>c.Train),StringComparer.Ordinal);
        var refs=specs.Select(s=>new CorpusScanReference(s.Index,ChunkPath(s.Index),Data.FileHash(Data.Within(output,ChunkPath(s.Index)),budget))).ToArray();
        var receipt=new CorpusScanCompletion(CompleteStatus,inputs,planSha,refs,physical,original,scope,token,accepted,train,development,longest,longestTrain.LongestTrainTokens,longestTrain.LongestTrainId,longestTrain.Source,bySource,familyCount,true,identifiers.Count,"scan-exclusions.jsonl",Data.FileHash(ledger,budget),false,false);
        CheckCompletion(receipt,inputs);Atomic(Data.Within(output,"length-scan-complete.json"),receipt,CorpusLengthScanJson.Default.CorpusScanCompletion,attempt,budget);
    }

    internal static void CheckCompletion(CorpusScanCompletion value,CorpusScanIdentity inputs)
    {
        if(inputs.SchemaVersion!=1||inputs.Algorithm!=Algorithm||inputs.MaximumTokens is <2 or >8192||inputs.ChunkRows is <1 or >50000||inputs.MaximumExclusions is <1 or >1000000||
            inputs.PhysicalRows is <1 or >50000000||inputs.Shards is <1 or >4096||inputs.Chunks is <1 or >MaximumChunks||
            new[]{inputs.ManifestSha256,inputs.BindingsSha256,inputs.OriginalExclusionsSha256,inputs.TokenizerSha256,inputs.SourcePolicySha256}.Any(s=>!Sha(s))||
            value.Chunks is null||value.TrainingBySource is null||value.Status!=CompleteStatus||value.Inputs!=inputs||value.Chunks.Length!=inputs.Chunks||value.PhysicalRows!=inputs.PhysicalRows||
            new[]{value.PhysicalRows,value.OriginalExcluded,value.SourceScopeExcluded,value.TokenLimitExcluded,value.Accepted,value.Train,value.Development}.Any(v=>v<0)||
            value.Chunks.Where((c,i)=>c.Index!=i||c.Path!=ChunkPath(i)||!Sha(c.Sha256)).Any()||value.ExclusionsPath!="scan-exclusions.jsonl"||
            value.PhysicalRows!=value.OriginalExcluded+value.SourceScopeExcluded+value.TokenLimitExcluded+value.Accepted||value.Accepted!=value.Train+value.Development||value.Train<1||value.Development<1||
            value.ExclusionIdentities!=value.OriginalExcluded+value.SourceScopeExcluded+value.TokenLimitExcluded||value.ExclusionIdentities>inputs.MaximumExclusions||!value.FamilyIsolationChecked||value.Families<1||value.Families>MaximumFamilies||
            value.LongestTokens is <2 or >8192||value.LongestTokens>inputs.MaximumTokens||value.LongestTrainTokens<2||value.LongestTrainTokens>value.LongestTokens||string.IsNullOrWhiteSpace(value.LongestTrainId)||string.IsNullOrWhiteSpace(value.LongestTrainSource)||
            value.TrainingBySource.Count is <1 or >128||value.TrainingBySource.Values.Any(v=>v<1)||value.TrainingBySource.Values.Sum()!=value.Train||value.GpuFeaturesComputed||value.FormalTrainingAdmitted||!Sha(value.ExclusionsSha256)||!Sha(value.ScanPlanSha256))throw new InvalidDataException("Incomplete or inconsistent full length scan cannot admit training.");
    }

    internal static CorpusScanCompletion Completion(string scanRoot,string expectedSha,Corpus corpus,string bindingsSha,RunBudget budget)
    {
        var path=Data.Within(scanRoot,"length-scan-complete.json");if(!Sha(expectedSha)||Data.FileHash(path,budget)!=expectedSha)throw new InvalidDataException("Exact complete length-scan SHA required; partial/pending is not admitted.");
        var value=Assets.Read(path,CorpusLengthScanJson.Default.CorpusScanCompletion,16*1024*1024);CheckCompletion(value,value.Inputs);
        if(value.Inputs.ManifestSha256!=corpus.ManifestSha256||value.Inputs.BindingsSha256!=bindingsSha||value.Inputs.OriginalExclusionsSha256!=corpus.ExclusionsSha256)throw new InvalidDataException("Full length scan belongs to another corpus/binding.");
        var specs=Specs(corpus,value.Inputs.ChunkRows,budget);
        if(Data.FileHash(Data.Within(scanRoot,"scan-plan.json"),budget)!=value.ScanPlanSha256||Assets.Read(Data.Within(scanRoot,"scan-plan.json"),CorpusLengthScanJson.Default.CorpusScanIdentity)!=value.Inputs||value.Chunks.Length!=specs.Length||Data.FileHash(Data.Within(scanRoot,value.ExclusionsPath),budget)!=value.ExclusionsSha256)throw new InvalidDataException("Complete scan plan/exclusion bytes differ.");
        for(var index=0;index<specs.Length;index++)
        {
            budget.Check();var reference=value.Chunks[index];if(reference.Index!=index||reference.Path!=ChunkPath(index)||Data.FileHash(Data.Within(scanRoot,reference.Path),budget)!=reference.Sha256)throw new InvalidDataException("Complete scan chunk identity/order differs.");
            _=ReadChunk(corpus,scanRoot,value.Inputs,specs[index],budget);
        }
        return value;
    }

    internal static void Admit(Corpus corpus,string bindingsSha,string scanRoot,string completeSha,string output,string policyPath,string policySha,string thirdPartyNoticePath,string thirdPartyNoticeSha,RunBudget budget)
    {
        var scan=Completion(scanRoot,completeSha,corpus,bindingsSha,budget);
        if(!Sha(policySha)||scan.Inputs.SourcePolicySha256!=policySha||Data.FileHash(policyPath,budget)!=policySha||!Sha(thirdPartyNoticeSha)||Data.FileHash(thirdPartyNoticePath,budget)!=thirdPartyNoticeSha||Directory.Exists(output)||File.Exists(output))throw new InvalidDataException("Fresh derived root and pinned complete scan/private policy/notices required.");
        Directory.CreateDirectory(output);var parent=Assets.Read(Data.Within(corpus.Root,"corpus-bindings.json"),TrainerJson.Default.CorpusBindings);
        var notices=new Dictionary<string,string>(StringComparer.Ordinal);var copied=new HashSet<string>(StringComparer.Ordinal);long metadataBytes=0;
        void Copy(string original,string relative,string sha,bool notice)
        {
            budget.Check();if(!copied.Add(relative)||copied.Count>512||new FileInfo(original).Length>16*1024*1024||(metadataBytes+=new FileInfo(original).Length)>128*1024*1024||Data.FileHash(original,budget)!=sha)throw new InvalidDataException("Derived metadata file/count/byte/SHA gate differs.");
            var destination=Data.Within(output,Relative(relative));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);Assets.CopyNew(original,destination,budget);
            if(Data.FileHash(destination,budget)!=sha||Data.FileHash(original,budget)!=sha)throw new InvalidDataException("Derived source metadata bytes changed.");if(notice)notices.Add(relative,sha);
        }
        using(var input=File.OpenRead(Data.Within(corpus.Root,"manifest.json")))
        using(var document=JsonDocument.Parse(input,new(){MaxDepth=32}))
        {
            if(document.RootElement.TryGetProperty("source_notice_bindings",out var originalNotices))
            foreach(var item in originalNotices.EnumerateObject())
            {budget.Check();var sha=item.Value.GetString()??throw new InvalidDataException("Parent notice SHA absent.");Copy(Data.Within(corpus.Root,Relative(item.Name)),item.Name,sha,true);}
            Copy(Data.Within(corpus.Root,"manifest.json"),"components/length-parent/manifest.json",corpus.ManifestSha256,true);
            Copy(Data.Within(corpus.Root,"corpus-bindings.json"),"components/length-parent/corpus-bindings.json",bindingsSha,true);
            Copy(Data.Within(corpus.Root,"exclusions.jsonl"),"components/length-parent/exclusions.jsonl",corpus.ExclusionsSha256,true);
            Copy(Data.Within(corpus.Root,"source-rights.json"),"source-rights.json",parent.SourceRightsSha256,false);
            var sourceAssets=parent.SourceAssetsPath is not null?Data.Within(corpus.Root,Relative(parent.SourceAssetsPath)):
                File.Exists(Data.Within(corpus.Root,"source-assets.json"))?Data.Within(corpus.Root,"source-assets.json"):
                parent.SourceRoot is not null?Data.Within(Path.GetFullPath(parent.SourceRoot),"ASSET_MANIFEST.json"):throw new InvalidDataException("Parent source asset metadata absent.");
            Copy(sourceAssets,"source-assets.json",parent.SourceAssetsSha256,false);
            Copy(policyPath,"source-scope-policy.json",policySha,true);Copy(thirdPartyNoticePath,"THIRD_PARTY_NOTICES.md",thirdPartyNoticeSha,true);
            Copy(Data.Within(scanRoot,"length-scan-complete.json"),"length-scan-complete.json",completeSha,true);
            Assets.CopyNew(Data.Within(scanRoot,scan.ExclusionsPath),Data.Within(output,"exclusions.jsonl"),budget);
            if(Data.FileHash(Data.Within(output,"exclusions.jsonl"),budget)!=scan.ExclusionsSha256)throw new InvalidDataException("Derived explicit exclusions copy differs.");
            for(var index=0;index<corpus.Manifest.Shards.Length;index++)
            {
                budget.Check();var shard=corpus.Manifest.Shards[index];var source=Data.Within(corpus.Root,Relative(shard.Path));var destination=Data.Within(output,Relative(shard.Path));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if(new FileInfo(source).Length!=shard.Bytes||Data.FileHash(source,budget)!=shard.Sha256)throw new InvalidDataException("Immutable original shard changed before admission.");
                Hardlink(source,destination);
                if(new FileInfo(destination).Length!=shard.Bytes||Data.FileHash(destination,budget)!=shard.Sha256)throw new InvalidDataException("Derived original shard bytes differ.");
                if(index%16==15)Console.WriteLine($"derived immutable shards={index+1}/{corpus.Manifest.Shards.Length}");
            }
            var admission=new CorpusLengthAdmission(1,"COMPLETE_SCAN_APPLIED_EXPLICIT_EXCLUSIONS",corpus.ManifestSha256,bindingsSha,completeSha,policySha,scan.ExclusionsSha256,scan.OriginalExcluded,scan.SourceScopeExcluded,scan.TokenLimitExcluded);
            using var manifest=new FileStream(Data.Within(output,"manifest.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None);using(var writer=new Utf8JsonWriter(manifest))
            {
                writer.WriteStartObject();foreach(var property in document.RootElement.EnumerateObject())
                {budget.Check();if(property.Name is "source_notice_bindings" or "length_admission")continue;property.WriteTo(writer);}
                writer.WritePropertyName("source_notice_bindings");writer.WriteStartObject();foreach(var entry in notices){budget.Check();writer.WriteString(entry.Key,entry.Value);}writer.WriteEndObject();
                writer.WritePropertyName("length_admission");JsonSerializer.Serialize(writer,admission,CorpusLengthScanJson.Default.CorpusLengthAdmission);writer.WriteEndObject();writer.Flush();
            }
            manifest.Flush(true);
        }
        var manifestSha=Data.FileHash(Data.Within(output,"manifest.json"),budget);var binding=parent with{ManifestSha256=manifestSha,ExclusionsSha256=scan.ExclusionsSha256,SourceRoot=null,SourceAssetsPath="source-assets.json",SourcePolicySha256=policySha,LengthScanSha256=completeSha};
        Atomic(Data.Within(output,"corpus-bindings.json"),binding,TrainerJson.Default.CorpusBindings,0,budget);var derivedBindingsSha=Data.FileHash(Data.Within(output,"corpus-bindings.json"),budget);
        var lengths=new CorpusLengthReceipt("PASS_full_corpus_strict_lengths_only",manifestSha,derivedBindingsSha,scan.Inputs.TokenizerSha256,scan.Inputs.MaximumTokens,scan.PhysicalRows,scan.ExclusionIdentities,scan.Accepted,0,scan.Train,scan.Development,scan.LongestTokens,scan.LongestTrainId,scan.LongestTrainTokens,scan.LongestTrainSource,scan.TrainingBySource,scan.ExclusionsSha256,false,false)
        {CompleteScan=true,SourcePolicySha256=policySha,ScanCompletionSha256=completeSha,ParentCorpusManifestSha256=corpus.ManifestSha256,OriginalExcludedRows=scan.OriginalExcluded,SourceScopeExcludedRows=scan.SourceScopeExcluded,TokenLimitExcludedRows=scan.TokenLimitExcluded};
        Atomic(Data.Within(output,"lengths.json"),lengths,CorpusWorkflowJson.Default.CorpusLengthReceipt,0,budget);
    }

    internal static void VerifyDerived(Corpus corpus,CorpusLengthReceipt lengths,RunBudget budget)
    {
        var binding=Assets.Read(Data.Within(corpus.Root,"corpus-bindings.json"),TrainerJson.Default.CorpusBindings);
        if(!lengths.CompleteScan||lengths.SourcePolicySha256 is null||lengths.ScanCompletionSha256 is null||binding.SourcePolicySha256!=lengths.SourcePolicySha256||binding.LengthScanSha256!=lengths.ScanCompletionSha256||Data.FileHash(Data.Within(corpus.Root,"length-scan-complete.json"),budget)!=lengths.ScanCompletionSha256||Data.FileHash(Data.Within(corpus.Root,"source-scope-policy.json"),budget)!=lengths.SourcePolicySha256)throw new InvalidDataException("Full plan cannot consume an incomplete/unbound source/length admission.");
        var scan=Assets.Read(Data.Within(corpus.Root,"length-scan-complete.json"),CorpusLengthScanJson.Default.CorpusScanCompletion,16*1024*1024);CheckCompletion(scan,scan.Inputs);
        var parentRoot=Data.Within(corpus.Root,"components/length-parent");
        var parentManifestPath=Data.Within(parentRoot,"manifest.json");var parentBindingPath=Data.Within(parentRoot,"corpus-bindings.json");
        if(Data.FileHash(parentManifestPath,budget)!=scan.Inputs.ManifestSha256||Data.FileHash(parentBindingPath,budget)!=scan.Inputs.BindingsSha256||Data.FileHash(Data.Within(parentRoot,"exclusions.jsonl"),budget)!=scan.Inputs.OriginalExclusionsSha256)throw new InvalidDataException("Archived length parent metadata identity differs.");
        var parentBinding=Assets.Read(parentBindingPath,TrainerJson.Default.CorpusBindings);
        var parentManifest=Assets.Read(parentManifestPath,TrainerJson.Default.CorpusManifest,16*1024*1024);
        if(parentBinding.ManifestSha256!=scan.Inputs.ManifestSha256||parentBinding.ExclusionsSha256!=scan.Inputs.OriginalExclusionsSha256||parentBinding.SourceAssetsSha256!=binding.SourceAssetsSha256||parentBinding.SourceRightsSha256!=binding.SourceRightsSha256||parentBinding.FormalTrainingAdmitted||parentManifest.RecordsWritten!=corpus.Manifest.RecordsWritten||!parentManifest.Shards.SequenceEqual(corpus.Manifest.Shards))throw new InvalidDataException("Derived corpus changed parent shards or source metadata semantics.");
        using(var stream=File.OpenRead(Data.Within(corpus.Root,"manifest.json")))
        using(var document=JsonDocument.Parse(stream,new(){MaxDepth=32}))
        {
            if(!document.RootElement.TryGetProperty("length_admission",out var element))throw new InvalidDataException("Derived manifest length admission missing.");
            var admission=element.Deserialize(CorpusLengthScanJson.Default.CorpusLengthAdmission);
            var expected=new CorpusLengthAdmission(1,"COMPLETE_SCAN_APPLIED_EXPLICIT_EXCLUSIONS",scan.Inputs.ManifestSha256,scan.Inputs.BindingsSha256,lengths.ScanCompletionSha256,lengths.SourcePolicySha256,scan.ExclusionsSha256,scan.OriginalExcluded,scan.SourceScopeExcluded,scan.TokenLimitExcluded);
            if(admission!=expected)throw new InvalidDataException("Derived manifest length admission identity differs.");
        }
        if(scan.Inputs.ManifestSha256!=lengths.ParentCorpusManifestSha256||scan.Inputs.SourcePolicySha256!=lengths.SourcePolicySha256||scan.Inputs.TokenizerSha256!=lengths.TokenizerSha256||scan.Inputs.MaximumTokens!=lengths.MaximumTokens||scan.ExclusionsSha256!=corpus.ExclusionsSha256||scan.PhysicalRows!=lengths.PhysicalRows||scan.ExclusionIdentities!=lengths.ExcludedRows||scan.Accepted!=lengths.Accepted||scan.Train!=lengths.Train||scan.Development!=lengths.Development||scan.OriginalExcluded!=lengths.OriginalExcludedRows||scan.SourceScopeExcluded!=lengths.SourceScopeExcludedRows||scan.TokenLimitExcluded!=lengths.TokenLimitExcludedRows||scan.LongestTokens!=lengths.LongestTokens||scan.LongestTrainTokens!=lengths.LongestTrainingTokens||scan.LongestTrainId!=lengths.LongestTrainingRecordId||scan.LongestTrainSource!=lengths.LongestTrainingSource||!scan.TrainingBySource.OrderBy(p=>p.Key).SequenceEqual(lengths.TrainingBySource.OrderBy(p=>p.Key)))throw new InvalidDataException("Derived full length receipt/complete scan denominator differs.");
    }

    [DllImport("kernel32.dll",EntryPoint="CreateHardLinkW",ExactSpelling=true,SetLastError=true,CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool WindowsHardlink(string target,string source,nint security);
    [DllImport("libc",EntryPoint="link",ExactSpelling=true,SetLastError=true,CharSet=CharSet.Ansi)]private static extern int UnixHardlink(string source,string target);
    private static void Hardlink(string source,string destination)
    {
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("Fresh immutable hardlink destination required.");
        var ok=OperatingSystem.IsWindows()?WindowsHardlink(destination,source,0):UnixHardlink(source,destination)==0;
        if(!ok)throw new IOException("Same-volume immutable corpus hardlink failed; no implicit large copy.",new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }
}
