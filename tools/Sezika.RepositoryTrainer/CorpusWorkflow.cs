using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

internal static class CorpusWorkflow
{
    internal const string ScheduleAlgorithm="source_round_robin_v1;source_order=sha256(seed,epoch,source);within_source=manifest_shard_row_order;train_only;no_oversampling";
    private const long MaximumRecords=50000000;
    private const long MaximumUpdates=800000000;

    internal static CorpusRunPlan Plan(string path,RunBudget budget,out string sha)
    {
        sha=Data.FileHash(path,budget);var p=Assets.Read(path,CorpusWorkflowJson.Default.CorpusRunPlan);
        if(p.SchemaVersion!=1||p.RunId.Length is <3 or >64||p.RunId.Any(c=>!char.IsAsciiLetterLower(c)&&!char.IsAsciiDigit(c)&&c!='-')||
           new[]{p.SourceCommit,p.DatasetCommit,p.ModelCommit}.Any(c=>c.Length!=40||!c.All(char.IsAsciiHexDigit))||
           new[]{p.CorpusManifestSha256,p.CorpusBindingsSha256,p.LengthReportSha256}.Any(c=>c.Length!=64||!c.All(char.IsAsciiHexDigit))||
           p.TrainRecords is <2 or >MaximumRecords||p.Epochs is <1 or >16||checked(p.TrainRecords*p.Epochs)>MaximumUpdates||
           p.MaximumTokens is <2 or >8192||p.CheckpointEvery is <1 or >1000000||p.ValidationPerSource is <1 or >32||
           p.MaximumOwnedBytes is <1048576 or >25769803776||p.ExpectedGpuUuid.Length!=32||!p.ExpectedGpuUuid.All(char.IsAsciiHexDigit)||!p.ExperimentalOnly||
           !float.IsFinite(p.EncoderRate)||p.EncoderRate<=0||p.EncoderRate>0.01f||!float.IsFinite(p.HeadRate)||p.HeadRate<=0||p.HeadRate>0.1f||
           !float.IsFinite(p.WeightDecay)||p.WeightDecay<0||p.WeightDecay>1)
            throw new InvalidDataException("Pinned experimental corpus plan, bounded complete epochs and exact GPU UUID required.");
        return p;
    }

    // Assets already saves all encoder/head weights plus both AdamW moments. It
    // does not invoke Workflow.Plan, so its int update field can safely represent
    // our explicitly bounded <800M global cursor without changing the old CLI.
    private static RunPlan StoragePlan(CorpusRunPlan p)=>new(p.RunId,p.CorpusRoot,p.CorpusManifestSha256,p.BaseModelRoot,
        p.SourceCommit,p.DatasetCommit,p.ModelCommit,p.Seed,checked((int)(p.TrainRecords*p.Epochs)),p.MaximumTokens,
        p.CheckpointEvery,p.ValidationPerSource,p.EncoderRate,p.HeadRate,p.WeightDecay,p.MaximumOwnedBytes,p.ExpectedGpuUuid,true);

    private static string[] Sources(Corpus corpus,RunBudget budget)
    {
        budget.Check();var sources=corpus.Manifest.Shards.Where(s=>s.Split=="train").Select(s=>s.Source).Distinct(StringComparer.Ordinal).ToArray();
        if(sources.Length is <1 or >128||sources.Any(string.IsNullOrWhiteSpace))throw new InvalidDataException("Explicit source item bound128.");
        return sources;
    }

    internal static IEnumerable<(TrainingRow Row,string Source)> Epoch(Corpus corpus,int seed,long epoch,RunBudget budget)
    {
        if(epoch is <0 or >=16)throw new InvalidDataException("Epoch bound16.");
        var sources=Sources(corpus,budget).OrderBy(s=>Data.Hash(seed.ToString(CultureInfo.InvariantCulture)+"\n"+epoch.ToString(CultureInfo.InvariantCulture)+"\n"+s),StringComparer.Ordinal).ToArray();
        return Mix(sources,s=>corpus.Rows(budget,s,"train").Where(item=>!item.Excluded).Select(item=>(item.Row,item.Source)),corpus.Manifest.RecordsWritten,budget);
    }

    // At most one live row and one gzip reader per source. No array of states.
    // All enumerators are disposed on normal completion, cancellation or failure.
    private static IEnumerable<(TrainingRow Row,string Source)> Mix(string[] sources,
        Func<string,IEnumerable<(TrainingRow Row,string Source)>> rows,long maximum,RunBudget budget)
    {
        if(sources.Length is <1 or >128||maximum is <1 or >MaximumRecords)throw new InvalidDataException("Schedule item bounds.");
        var iterators=new IEnumerator<(TrainingRow Row,string Source)>?[sources.Length];long yielded=0;var active=sources.Length;
        try
        {
            for(var i=0;i<sources.Length;i++){budget.Check();iterators[i]=rows(sources[i]).GetEnumerator();}
            for(long round=0;round<=maximum&&active>0;round++)
            {
                budget.Check();
                for(var i=0;i<iterators.Length;i++)
                {
                    budget.Check();var iterator=iterators[i];if(iterator is null)continue;
                    if(!iterator.MoveNext()){iterator.Dispose();iterators[i]=null;active--;continue;}
                    if(++yielded>maximum)throw new InvalidDataException("Schedule exceeded pinned physical denominator.");
                    var item=iterator.Current;if(item.Row.Split!="train"||item.Source!=sources[i])throw new InvalidDataException("Held-out/source mismatch cannot enter reverse schedule.");
                    yield return item;
                }
            }
            if(active!=0)throw new InvalidDataException("Schedule exhausted iteration bound.");
        }
        finally{foreach(var iterator in iterators)iterator?.Dispose();}
    }

    private static IEnumerable<(TrainingRow Row,string Source)> From(Corpus corpus,CorpusRunPlan plan,long completed,RunBudget budget)
    {
        var global=checked(plan.TrainRecords*plan.Epochs);
        if(completed<0||completed>=global)throw new InvalidDataException("Resume cursor outside unfinished fixed global plan.");
        var firstEpoch=completed/plan.TrainRecords;var skipped=completed%plan.TrainRecords;
        for(var epoch=firstEpoch;epoch<plan.Epochs;epoch++)
        {
            long seen=0;
            foreach(var item in Epoch(corpus,plan.Seed,epoch,budget))
            {
                budget.Check();if(++seen>plan.TrainRecords)throw new InvalidDataException("Epoch training denominator differs.");
                if(epoch==firstEpoch&&seen<=skipped){if(seen%100000==0)Console.WriteLine($"resume scan={seen}/{skipped},epoch={epoch}");continue;}
                yield return item;
            }
            if(seen!=plan.TrainRecords)throw new InvalidDataException("Complete epoch did not cover every admitted training row.");
        }
    }

    internal static void Lengths(Corpus corpus,string bindingsSha,string tokenizerPath,int max,long maximum,string output,RunBudget budget)
    {
        if(max is <2 or >8192||maximum is <1 or >MaximumRecords||corpus.Manifest.RecordsWritten>maximum||Directory.Exists(output)||File.Exists(output))throw new InvalidDataException("Fresh full-corpus length output and explicit row/token budgets required.");
        Directory.CreateDirectory(output);var tokenizerSha=Data.FileHash(tokenizerPath,budget);var tokenizer=new TokenizerJson(tokenizerPath,budget.Token);
        var ledger=Data.Within(output,"length-failures.jsonl");long physical=0,excluded=0,accepted=0,rejected=0,train=0,development=0;int longest=0,longestTrain=0;string? longestId=null,longestSource=null;
        var counts=new Dictionary<string,long>(StringComparer.Ordinal);var families=new Dictionary<string,string>(StringComparer.Ordinal);
        using(var writer=new StreamWriter(new FileStream(ledger,FileMode.CreateNew),new UTF8Encoding(false)))
        foreach(var item in corpus.Rows(budget))
        {
            budget.Check();physical++;if(item.Excluded){excluded++;continue;}
            if(families.TryGetValue(item.Row.Family,out var existingSplit)){if(existingSplit!=item.Row.Split)throw new InvalidDataException("Family crossed train/development before length admission.");}
            else{if(families.Count>=4000000)throw new InvalidDataException("Length family index item bound4M.");families.Add(item.Row.Family,item.Row.Split);}
            if(item.Row.Split=="train"){train++;counts[item.Source]=counts.GetValueOrDefault(item.Source)+1;}else development++;
            try
            {
                var length=Workflow.Sequence(tokenizer,item.Row,max,false,budget).Sequence.TokenIds.Length;
                if(item.Row.Type==MarkerQuestionType.Choice)length=Math.Max(length,Workflow.Sequence(tokenizer,item.Row,max,true,budget).Sequence.TokenIds.Length);
                accepted++;longest=Math.Max(longest,length);
                if(item.Row.Split=="train"&&length>longestTrain){longestTrain=length;longestId=item.Row.RecordId;longestSource=item.Source;}
            }
            catch(DecisionException error){rejected++;writer.WriteLine(JsonSerializer.Serialize(new CorpusLengthFailure(item.Row.RecordId,item.Source,item.Row.Split,error.Code),CorpusWorkflowJson.Default.CorpusLengthFailure));}
            if(physical%10000==0)Console.WriteLine($"corpus strict lengths={physical}/{corpus.Manifest.RecordsWritten},accepted={accepted},rejected={rejected}");
        }
        if(physical!=corpus.Manifest.RecordsWritten||train<1||development<1||physical!=excluded+accepted+rejected)throw new InvalidDataException("Corpus length denominator differs.");
        Data.WriteNew(Data.Within(output,"lengths.json"),new CorpusLengthReceipt(rejected==0?"PASS_full_corpus_strict_lengths_only":"FAIL_preserve_rejected_corpus_records",
            corpus.ManifestSha256,bindingsSha,tokenizerSha,max,physical,excluded,accepted,rejected,train,development,longest,longestId,longestTrain,longestSource,counts,Data.FileHash(ledger,budget),false,false),CorpusWorkflowJson.Default.CorpusLengthReceipt);
        if(rejected!=0)throw new InvalidDataException("Overlength or unrepresentable rows preserved; no silent truncation or training admission.");
    }

    private static CorpusLengthReceipt Lengths(CorpusRunPlan p,Corpus corpus,RunBudget budget)
    {
        if(Data.FileHash(p.LengthReportPath,budget)!=p.LengthReportSha256)throw new InvalidDataException("Pinned full-corpus length receipt SHA differs.");
        var r=Assets.Read(p.LengthReportPath,CorpusWorkflowJson.Default.CorpusLengthReceipt);
        if(r.Status!="PASS_full_corpus_strict_lengths_only"||r.CorpusManifestSha256!=corpus.ManifestSha256||r.CorpusBindingsSha256!=p.CorpusBindingsSha256||r.TokenizerSha256!=IndependentModelLoader.TokenizerSha256||
            r.MaximumTokens!=p.MaximumTokens||r.PhysicalRows!=corpus.Manifest.RecordsWritten||r.PhysicalRows!=r.ExcludedRows+r.Accepted||r.Rejected!=0||r.Train!=p.TrainRecords||r.Development<1||
            r.Accepted!=r.Train+r.Development||r.LongestTokens is <2 or >8192||r.LongestTokens>p.MaximumTokens||r.LongestTrainingTokens<2||r.LongestTrainingTokens>r.LongestTokens||
            string.IsNullOrWhiteSpace(r.LongestTrainingRecordId)||string.IsNullOrWhiteSpace(r.LongestTrainingSource)||r.TrainingBySource.Count is <1 or >128||r.TrainingBySource.Values.Any(v=>v<1)||r.TrainingBySource.Values.Sum()!=r.Train||r.GpuFeaturesComputed||r.FormalTrainingAdmitted)
            throw new InvalidDataException("Complete, identity-bound experimental length coverage gate differs.");
        var sourceSet=Sources(corpus,budget).ToHashSet(StringComparer.Ordinal);
        if(r.TrainingBySource.Keys.Any(s=>!sourceSet.Contains(s)))throw new InvalidDataException("Length report/source identity differs.");
        var binding=Assets.Read(Data.Within(corpus.Root,"corpus-bindings.json"),TrainerJson.Default.CorpusBindings);
        if(binding.SourcePolicySha256 is not null||binding.LengthScanSha256 is not null||r.SourcePolicySha256 is not null||r.ScanCompletionSha256 is not null||r.CompleteScan)CorpusLengthScan.VerifyDerived(corpus,r,budget);
        return r;
    }

    private static InputIdentity Identity(CorpusRunPlan p,string planSha,Corpus corpus,string encoderSha,string tokenizerSha)=>new(planSha,corpus.ManifestSha256,
        Data.Hash(corpus.ManifestSha256+"\n"+p.CorpusBindingsSha256+"\n"+corpus.ExclusionsSha256+"\n"+p.LengthReportSha256),encoderSha,tokenizerSha,
        Data.Hash(ScheduleAlgorithm+"\n"+p.Seed.ToString(CultureInfo.InvariantCulture)+"\n"+p.Epochs.ToString(CultureInfo.InvariantCulture)+"\n"+p.TrainRecords.ToString(CultureInfo.InvariantCulture)+"\n"+corpus.ManifestSha256),CudaFullEncoderTrainer.BackwardManifestSha256);

    private static string GpuUuid(CorpusRunPlan plan,string? stageGpuUuid)
    {
        var uuid=stageGpuUuid??plan.ExpectedGpuUuid;if(uuid.Length!=32||!uuid.All(char.IsAsciiHexDigit))throw new InvalidDataException("Exact stage GPU UUID required.");return uuid;
    }
    private static CudaDevice Device(CorpusRunPlan plan,string? stageGpuUuid=null)
    {
        var device=CudaDevice.Open(0);
        try
        {
            GpuAdmission.RequireDevice(device.Info,GpuUuid(plan,stageGpuUuid));
            return device;
        }
        catch{device.Dispose();throw;}
    }

    private static long Resume(string? root,CorpusRunPlan p,InputIdentity identity,RunBudget budget)
    {
        if(root is null)return 0;
        var checkpointPath=Data.Within(root,"checkpoint.json");var cp=Assets.Read(checkpointPath,TrainerJson.Default.CheckpointReceipt);
        var cursor=Assets.Read(Data.Within(root,"corpus-cursor.json"),CorpusWorkflowJson.Default.CorpusCursorReceipt);
        if(cp.SchemaVersion!=1||cp.Inputs!=identity||cp.RunId!=p.RunId||cursor.SchemaVersion!=1||cursor.Inputs!=identity||cursor.RunId!=p.RunId||cursor.CompletedUpdates!=cp.CompletedUpdates||
           cursor.CompletedUpdates<1||cursor.CompletedUpdates>=p.TrainRecords*p.Epochs||cursor.TrainingRecordsPerEpoch!=p.TrainRecords||cursor.PlannedEpochs!=p.Epochs||
           cursor.Epoch!=cp.CompletedUpdates/p.TrainRecords||cursor.WithinEpoch!=cp.CompletedUpdates%p.TrainRecords||cursor.ScheduleAlgorithm!=ScheduleAlgorithm||cursor.CheckpointReceiptSha256!=Data.FileHash(checkpointPath,budget))
            throw new InvalidDataException("Corpus cursor/complete AdamW checkpoint does not match the immutable plan; old 48-update checkpoints cannot be repurposed.");
        var descriptors=Assets.Descriptors(cp.Config);
        using(var weights=new TensorFile(Data.Within(root,"weights.safetensors"),cp.WeightsSha256,budget))weights.RequireNames(descriptors.Select(d=>d.Name));
        using(var moments=new TensorFile(Data.Within(root,"moments.safetensors"),cp.MomentsSha256,budget))moments.RequireNames(descriptors.SelectMany(d=>new[]{"first."+d.Name,"second."+d.Name}));
        return cp.CompletedUpdates;
    }

    // The corpus cursor is the last completion marker. A failed/expired save
    // leaves a pending file and cannot become an accepted resume checkpoint.
    private static void SaveCursor(string path,CorpusCursorReceipt cursor,RunBudget budget)
    {
        budget.Check();var pending=Data.Within(Path.GetDirectoryName(Path.GetFullPath(path))!,"corpus-cursor.pending");
        using(var output=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(output,cursor,CorpusWorkflowJson.Default.CorpusCursorReceipt);output.Flush(true);}
        budget.Check();File.Move(pending,path,false);
    }

    internal static int CheckpointInterval(string command,int planInterval,int? requested)
    {
        if(requested is not null&&command!="train-corpus")throw new InvalidDataException("Checkpoint interval override is a training execution policy only.");
        var interval=requested??planInterval;
        if(interval is <1 or >1000000)throw new ArgumentOutOfRangeException(nameof(requested));
        return interval;
    }
    internal static bool CheckpointDue(long completed,long bound,int interval)=>completed%interval==0||completed==bound;

    internal static void Run(string command,string planPath,string output,string? profilePath,string? resume,long stageUpdates,RunBudget budget,string? stageGpuUuid=null,bool resident=false,string? stopFile=null,int? checkpointEvery=null)
    {
        if(command is not("profile-corpus" or "train-corpus"))throw new ArgumentException("Unknown corpus workflow.");
        if(command=="profile-corpus"&&(resident||stopFile is not null))throw new ArgumentException("Safe-stop/resident options apply only to training.");
        if(stopFile is not null&&File.Exists(stopFile))throw new OperationCanceledException("Safe stop already requested; existing checkpoint retained.");
        var watch=Stopwatch.StartNew();var p=Plan(planPath,budget,out var planSha);var corpus=new Corpus(p.CorpusRoot,p.CorpusManifestSha256,p.CorpusBindingsSha256,budget);
        // Execution cadence never modifies the immutable plan or optimizer identity.
        var interval=CheckpointInterval(command,p.CheckpointEvery,checkpointEvery);
        if(corpus.Manifest.Pilot)throw new InvalidDataException("Pilot corpus cannot claim complete training coverage.");
        var lengths=Lengths(p,corpus,budget);var global=checked(p.TrainRecords*p.Epochs);var storage=StoragePlan(p);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output exists; preserve it.");
        using var model=IndependentModelLoader.Load(p.BaseModelRoot,new(){Kernel=EncoderKernelMode.Scalar},budget.Token);
        var identity=Identity(p,planSha,corpus,model.EncoderSha256,model.TokenizerSha256);var initial=Resume(resume,p,identity,budget);
        if(command=="profile-corpus"&&resume is not null)throw new InvalidDataException("Profile starts from fixed base and zero head.");
        if(command=="train-corpus"&&(stageUpdates<1||stageUpdates>MaximumUpdates||stageUpdates>global-initial))throw new InvalidDataException("Explicit stage update count must fit remaining fixed global plan.");
        CorpusProfileReceipt? profile=null;double saveReserve=0;
        if(command=="train-corpus")
        {
            if(profilePath is null)throw new InvalidDataException("Complete same-instance corpus profile required.");
            profile=Assets.Read(profilePath,CorpusWorkflowJson.Default.CorpusProfileReceipt);
            if(profile.Status!="PASS_real_base_corpus_profile_only"||profile.Inputs!=identity||profile.RunId!=p.RunId||profile.GpuUuid!=GpuUuid(p,stageGpuUuid)||profile.MaximumTokensTested<lengths.LongestTokens||!double.IsFinite(profile.MaximumStepSeconds)||profile.MaximumStepSeconds<=0||
                !File.Exists(Data.Within(Path.GetDirectoryName(Path.GetFullPath(profilePath))!,"resources-reclaimed.txt")))throw new InvalidDataException("Full corpus/profile/GPU identity gate differs.");
            var saves=1+(initial+stageUpdates)/interval-initial/interval;
            var estimate=4*profile.MaximumStepSeconds*stageUpdates+600+120*saves;
            if(resident)saveReserve=CorpusStopPolicy.SaveReserve(profile.CheckpointSeconds);
            else if(estimate>budget.RemainingSeconds)throw new InvalidDataException($"This phase needs conservative {estimate:F0}s; available {budget.RemainingSeconds:F0}s. Lower --stage-updates without changing the global plan.");
        }
        // Stream a resume prefix before opening GPU ownership. No previous epoch
        // is rescanned; only the compressed current epoch prefix is necessary.
        using var stream=From(corpus,p,initial,budget).GetEnumerator();
        if(!stream.MoveNext())throw new InvalidDataException("No unfinished training row.");
        var first=stream.Current;TrainingRow? longest=null;
        if(command=="profile-corpus")
        {
            foreach(var item in corpus.Rows(budget,lengths.LongestTrainingSource,"train"))
            {
                budget.Check();if(!item.Excluded&&item.Row.Split=="train"&&item.Row.RecordId==lengths.LongestTrainingRecordId){longest=item.Row;break;}
            }
            if(longest is null)throw new InvalidDataException("Profile longest training identity absent.");
        }
        using var device=Device(p,stageGpuUuid);var baseline=device.GetMemorySnapshot();
        GpuAdmission.RequireMemory(baseline.FreeBytes,p.MaximumOwnedBytes);
        if(profile is not null)GpuAdmission.RequireProfile(profilePath!,p,profile,device.Info,budget);
        var profileMaximumTokens=GpuAdmission.ProfileMaximumTokens(device.Info,p.MaximumTokens,lengths.LongestTokens);
        Directory.CreateDirectory(output);double loss=0,maxStep=0;var syntheticPadded=false;string? checkpointPath=null,cursorPath=null,stopReason=null;var bound=command=="profile-corpus"?3:initial+stageUpdates;
        using(var trainer=new CudaFullEncoderTrainer(device,model.Encoder.Config,model.Encoder.Weights,maximumOwnedBytes:p.MaximumOwnedBytes,cancellationToken:budget.Token))
        {
            if(resume is not null)Assets.Restore(resume,storage,identity,trainer,budget);
            if(trainer.CompletedUpdates!=initial)throw new InvalidDataException("Restored AdamW cursor differs.");
            long lastSaved=-1;
            void Save()
            {
                budget.Check();if(lastSaved==trainer.CompletedUpdates)return;
                var timer=Stopwatch.StartNew();
                var root=Data.Within(output,"checkpoint-"+trainer.CompletedUpdates.ToString("D10",CultureInfo.InvariantCulture));checkpointPath=Assets.SaveCheckpoint(root,storage,identity,trainer,budget);
                cursorPath=Data.Within(root,"corpus-cursor.json");SaveCursor(cursorPath,new CorpusCursorReceipt(1,p.RunId,identity,trainer.CompletedUpdates,trainer.CompletedUpdates/p.TrainRecords,trainer.CompletedUpdates%p.TrainRecords,p.TrainRecords,p.Epochs,Data.FileHash(checkpointPath,budget),ScheduleAlgorithm),budget);
                Trim(output,p.RunId,identity,budget);lastSaved=trainer.CompletedUpdates;
                if(resident)saveReserve=Math.Max(saveReserve,CorpusStopPolicy.SaveReserve(timer.Elapsed.TotalSeconds));
            }
            for(var step=initial;step<bound;step++)
            {
                budget.Check();
                if(command=="train-corpus")
                {
                    stopReason=CorpusStopPolicy.Reason(resident,stopFile,budget.RemainingSeconds,Math.Max(profile!.MaximumStepSeconds,maxStep),saveReserve);
                    if(stopReason is not null)
                    {
                        if(trainer.CompletedUpdates==0)throw new OperationCanceledException("No committed update before safe stop; fixed base retained.");
                        Save();break;
                    }
                }
                var row=command=="profile-corpus"?(step==2?longest!:first.Row):stream.Current.Row;
                if(row.Split!="train")throw new InvalidDataException("Held-out row cannot enter reverse/update path.");
                var item=Workflow.Sequence(model.Tokenizer,row,p.MaximumTokens,command=="train-corpus"&&step%2==1,budget);var ids=item.Sequence.TokenIds;
                if(command=="profile-corpus"&&step==2&&ids.Length<profileMaximumTokens){syntheticPadded=true;ids=ids.Concat(Enumerable.Repeat(model.Tokenizer.EosId,profileMaximumTokens-ids.Length)).ToArray();}
                var timer=Stopwatch.StartNew();var result=trainer.Backward(ids,item.Sequence.MarkerPositions,row.Type,item.Target,600,budget.Token);
                if(command=="profile-corpus"&&step==1)
                {
                    var gradient=trainer.ReadParameter("model.layers.0.attn.Wqkv.weight",true);
                    try{if(!gradient.Any(v=>float.IsFinite(v)&&v!=0))throw new ArithmeticException("Base encoder gradients remain zero after head update.");}finally{Array.Clear(gradient);}
                }
                trainer.ApplyAdamW(p.EncoderRate,p.HeadRate,p.WeightDecay,budget.Token);maxStep=Math.Max(maxStep,timer.Elapsed.TotalSeconds);loss+=result.Loss;
                var advancedAtEpochBoundary=false;
                if(command=="train-corpus"&&trainer.CompletedUpdates%p.TrainRecords==0)
                {
                    // Validate trailing gzip/source EOF and epoch/global denominator
                    // before accepting the complete checkpoint/cursor marker.
                    var hasNext=stream.MoveNext();
                    if(hasNext!=(trainer.CompletedUpdates<global))throw new InvalidDataException("Epoch/global trailing denominator differs.");
                    advancedAtEpochBoundary=true;
                }
                if(trainer.CompletedUpdates%10==0||step==initial||trainer.CompletedUpdates==bound)Console.WriteLine($"{command} updates={trainer.CompletedUpdates}/{global},phase_end={bound},epoch={trainer.CompletedUpdates/p.TrainRecords},tokens={ids.Length},loss={result.Loss:R},elapsed={watch.Elapsed.TotalSeconds:F1}s");
                if(command=="train-corpus"&&CheckpointDue(trainer.CompletedUpdates,bound,interval))
                {
                    Save();
                }
                if(command=="train-corpus"&&trainer.CompletedUpdates<bound&&!advancedAtEpochBoundary&&!stream.MoveNext())throw new InvalidDataException("Global plan exhausted before requested complete phase.");
            }
            if(command=="profile-corpus")
            {
                var saveWatch=Stopwatch.StartNew();Assets.SaveCheckpoint(Data.Within(output,"profile-state-not-a-corpus-checkpoint"),storage,identity,trainer,budget);
                Data.WriteNew(Data.Within(output,"profile.json"),new CorpusProfileReceipt("PASS_real_base_corpus_profile_only",p.RunId,identity,device.Info.Uuid,profileMaximumTokens,maxStep,watch.Elapsed.TotalSeconds,device.GetMemorySnapshot().PeakOwnedBytes,syntheticPadded,false){CheckpointSeconds=saveWatch.Elapsed.TotalSeconds},CorpusWorkflowJson.Default.CorpusProfileReceipt);
            }
            else
            {
                if(checkpointPath is null||cursorPath is null||lastSaved!=trainer.CompletedUpdates||trainer.CompletedUpdates!=bound&&stopReason is null)throw new InvalidDataException("Requested stage or safe boundary not fully saved.");
                using var process=Process.GetCurrentProcess();var complete=trainer.CompletedUpdates==global;
                var receipt=new CorpusStageReceipt(complete?"PASS_all_requested_corpus_epochs_GPU_saved_only":stopReason is null?"PASS_bounded_corpus_GPU_phase_saved_only":"PARTIAL_safe_boundary_checkpoint_saved",p.RunId,identity,device.Info.Uuid,Environment.ProcessId,process.StartTime.ToUniversalTime(),initial,trainer.CompletedUpdates,stageUpdates,global,trainer.CompletedUpdates/p.TrainRecords,trainer.CompletedUpdates%p.TrainRecords,trainer.CompletedUpdates==initial?0:loss/(trainer.CompletedUpdates-initial),watch.Elapsed.TotalSeconds,device.GetMemorySnapshot().PeakOwnedBytes,Data.FileHash(checkpointPath,budget),Data.FileHash(cursorPath,budget),complete,false,false){StopReason=stopReason,CheckpointEvery=interval};
                var report=Data.Within(output,"training-report.json");Data.WriteNew(report,receipt,CorpusWorkflowJson.Default.CorpusStageReceipt);
                Assets.CopyNew(planPath,Data.Within(output,"run-plan.json"),budget);
                if(complete)Package(output,p,identity,trainer,checkpointPath,report,model.TokenizerSha256,budget);
            }
        }
        Reclaimed(device,baseline);device.Dispose();
        if(command=="profile-corpus")GpuAdmission.SaveProfileBinding(output,p,device.Info,baseline.FreeBytes,budget);
        File.WriteAllText(Data.Within(output,"resources-reclaimed.txt"),"PASS_owned_buffers_modules_restored_and_context_disposed\n",new UTF8Encoding(false));
    }

    private static void Reclaimed(CudaDevice device,CudaMemorySnapshot baseline)
    {
        var after=device.GetMemorySnapshot();
        if(after.OwnedBytes!=baseline.OwnedBytes||after.OwnedAllocationCount!=baseline.OwnedAllocationCount||after.LoadedModuleCount!=baseline.LoadedModuleCount||after.ReleaseFailureCount!=baseline.ReleaseFailureCount)
            throw new IOException("Owned CUDA allocations/modules did not return to baseline.");
    }

    private static void Trim(string output,string runId,InputIdentity identity,RunBudget budget)
    {
        var owned=Directory.EnumerateDirectories(output,"checkpoint-*",SearchOption.TopDirectoryOnly).Take(4).Order(StringComparer.Ordinal).ToArray();if(owned.Length>3)throw new IOException("Checkpoint directory bound3.");
        if(owned.Length<=2)return;var root=owned[0];var cp=Assets.Read(Data.Within(root,"checkpoint.json"),TrainerJson.Default.CheckpointReceipt);var cursor=Assets.Read(Data.Within(root,"corpus-cursor.json"),CorpusWorkflowJson.Default.CorpusCursorReceipt);
        if(cp.RunId!=runId||cp.Inputs!=identity||cursor.RunId!=runId||cursor.Inputs!=identity||cursor.CheckpointReceiptSha256!=Data.FileHash(Data.Within(root,"checkpoint.json"),budget))throw new IOException("Retained checkpoint ownership differs.");
        using(var weights=new TensorFile(Data.Within(root,"weights.safetensors"),cp.WeightsSha256,budget)){}
        using(var moments=new TensorFile(Data.Within(root,"moments.safetensors"),cp.MomentsSha256,budget)){}
        if(Directory.EnumerateFileSystemEntries(root).Take(5).Count()!=4)throw new IOException("Unexpected checkpoint files; preserve directory.");
        foreach(var name in new[]{"weights.safetensors","moments.safetensors","checkpoint.json","corpus-cursor.json"}){budget.Check();File.Delete(Data.Within(root,name));}Directory.Delete(root,false);
    }

    private static void Package(string output,CorpusRunPlan p,InputIdentity identity,CudaFullEncoderTrainer trainer,string cpPath,string report,string tokenizerSha,RunBudget budget)
    {
        var cp=Assets.Read(cpPath,TrainerJson.Default.CheckpointReceipt);Assets.CopyNew(Data.Within(Path.GetDirectoryName(cpPath)!,"weights.safetensors"),Data.Within(output,"weights.safetensors"),budget);
        Directory.CreateDirectory(Data.Within(output,"tokenizer"));Assets.CopyNew(Data.Within(p.BaseModelRoot,"tokenizer/tokenizer.json"),Data.Within(output,"tokenizer/tokenizer.json"),budget);
        foreach(var name in new[]{"LICENSE-COMPONENTS.md","license-provenance.json"})Assets.CopyNew(Data.Within(p.BaseModelRoot,name),Data.Within(output,"base-"+name),budget);
        var baseNotices=Data.Within(p.BaseModelRoot,"notices");var notices=Directory.EnumerateFiles(baseNotices,"*",SearchOption.TopDirectoryOnly).Take(129).ToArray();
        if(notices.Length>128||Directory.EnumerateDirectories(baseNotices,"*",SearchOption.TopDirectoryOnly).Take(1).Any())throw new InvalidDataException("Base notice item/depth bounds; preserve complete notices explicitly.");
        Directory.CreateDirectory(Data.Within(output,"notices"));
        foreach(var notice in notices){budget.Check();Assets.CopyNew(Data.Within(baseNotices,Path.GetFileName(notice)),Data.Within(output,"notices/"+Path.GetFileName(notice)),budget);}
        var sourceProvenanceSha=CorpusProvenancePackage.Export(p.CorpusRoot,p.CorpusManifestSha256,p.CorpusBindingsSha256,output,budget);
        Assets.CopyNew(p.LengthReportPath,Data.Within(output,"input-lengths.json"),budget);
        File.WriteAllText(Data.Within(output,"LICENSE-COMPONENTS.md"),"# 完整来源混合语料实验模型\n\nEncoder/tokenizer 的来源、许可和限制见 base-LICENSE-COMPONENTS.md、base-license-provenance.json。数据按 input-source-rights.json 分项声明，原 source-assets 与已绑定 notices/组件元数据在 corpus-provenance.json 的路径/SHA映射中独立核验。归一化语料及教师标注不能自动视作人工正确答案；整体训练输出许可证 unknown。代码 Sezika Apache-2.0。\n\nPrivateSourceMetadata=true，PublicPublicationAccepted=false。本私有包可能保留 components 的逐记录排除ID，公开模型发布必须另制去逐题ID的来源摘要映射，不可直接发布本包；未复制原训练shards或教师gold。\n\n本包保留固定语料/排除账本绑定、全部 encoder/head 权重和完整 epoch 回执。Quality / calibration / Native AOT / performance: NOT_ACCEPTED。禁止将语料及派生训练记录随模型对外发布。\n",new UTF8Encoding(false));
        Data.WriteNew(Data.Within(output,"corpus-package.json"),new CorpusPackageReceipt(1,p.RunId,"full_encoder_adamw_fp32_streamed_corpus",p.SourceCommit,p.DatasetCommit,p.ModelCommit,identity,trainer.Config,trainer.CompletedUpdates,cp.WeightsSha256,tokenizerSha,Data.FileHash(cpPath,budget),Data.FileHash(report,budget),p.CorpusManifestSha256,p.CorpusBindingsSha256,"unknown_overall_component_specific","uncalibrated",false,false){SourceProvenanceSha256=sourceProvenanceSha},CorpusWorkflowJson.Default.CorpusPackageReceipt);
    }

    internal static void Validate(string packageRoot,string expectedPackageSha,string planPath,string output,RunBudget budget,string? stageGpuUuid=null)
    {
        var p=Plan(planPath,budget,out var planSha);var packagePath=Data.Within(packageRoot,"corpus-package.json");
        if(File.Exists(output)||Directory.Exists(output)||Data.FileHash(packagePath,budget)!=expectedPackageSha)throw new InvalidDataException("Fresh output and exact corpus-package identity required.");
        var corpus=new Corpus(p.CorpusRoot,p.CorpusManifestSha256,p.CorpusBindingsSha256,budget);_ = Lengths(p,corpus,budget);
        var identity=Identity(p,planSha,corpus,IndependentModelLoader.ConvertedEncoderSha256,IndependentModelLoader.TokenizerSha256);
        var package=Assets.Read(packagePath,CorpusWorkflowJson.Default.CorpusPackageReceipt);var global=p.TrainRecords*p.Epochs;
        if(package.SchemaVersion!=1||package.RunId!=p.RunId||package.Method!="full_encoder_adamw_fp32_streamed_corpus"||package.SourceCommit!=p.SourceCommit||package.DatasetCommit!=p.DatasetCommit||package.ModelCommit!=p.ModelCommit||
            package.Inputs!=identity||package.CompletedUpdates!=global||package.CorpusManifestSha256!=p.CorpusManifestSha256||package.CorpusBindingsSha256!=p.CorpusBindingsSha256||package.TokenizerSha256!=identity.TokenizerSha256||package.CalibrationStatus!="uncalibrated"||package.QualityAccepted||package.AotAccepted)
            throw new InvalidDataException("Final full-corpus package contract differs.");
        CorpusProvenancePackage.Verify(packageRoot,package.SourceProvenanceSha256??throw new InvalidDataException("Independent source metadata provenance is absent."),p.CorpusManifestSha256,p.CorpusBindingsSha256,budget);
        var reportPath=Data.Within(packageRoot,"training-report.json");var report=Assets.Read(reportPath,CorpusWorkflowJson.Default.CorpusStageReceipt);
        if(Data.FileHash(reportPath,budget)!=package.TrainingReportSha256||report.Status!="PASS_all_requested_corpus_epochs_GPU_saved_only"||report.Inputs!=identity||report.RunId!=p.RunId||report.CompletedUpdates!=global||report.GlobalUpdates!=global||!report.FullCorpusEpochCoverage||report.CompletedEpochs!=p.Epochs||report.WithinEpoch!=0||report.CheckpointReceiptSha256!=package.CheckpointReceiptSha256||report.ProcessId==Environment.ProcessId||
            !File.Exists(Data.Within(packageRoot,"resources-reclaimed.txt")))throw new InvalidDataException("Full epoch/training process/resource receipt differs.");
        var checkpoint=Data.Within(packageRoot,"checkpoint-"+global.ToString("D10",CultureInfo.InvariantCulture));var cpPath=Data.Within(checkpoint,"checkpoint.json");
        if(Data.FileHash(cpPath,budget)!=package.CheckpointReceiptSha256||Data.FileHash(Data.Within(checkpoint,"corpus-cursor.json"),budget)!=report.CursorReceiptSha256)throw new InvalidDataException("Final checkpoint/cursor receipt differs.");
        var cp=Assets.Read(cpPath,TrainerJson.Default.CheckpointReceipt);var cursor=Assets.Read(Data.Within(checkpoint,"corpus-cursor.json"),CorpusWorkflowJson.Default.CorpusCursorReceipt);
        if(cp.Inputs!=identity||cp.RunId!=p.RunId||cp.Config!=package.Config||cp.WeightsSha256!=package.WeightsSha256||cp.CompletedUpdates!=global||cursor.SchemaVersion!=1||cursor.RunId!=p.RunId||cursor.Inputs!=identity||cursor.CompletedUpdates!=global||cursor.Epoch!=p.Epochs||cursor.WithinEpoch!=0||cursor.TrainingRecordsPerEpoch!=p.TrainRecords||cursor.PlannedEpochs!=p.Epochs||cursor.CheckpointReceiptSha256!=package.CheckpointReceiptSha256||cursor.ScheduleAlgorithm!=ScheduleAlgorithm)throw new InvalidDataException("Final persisted states do not match epoch cursor.");
        // Keep only bounded smallest SHA priorities per source/split; reading all
        // shards establishes the denominator without retaining all input states.
        var selected=new Dictionary<string,PriorityQueue<(TrainingRow Row,string Source),string>>(StringComparer.Ordinal);
        var reverse=Comparer<string>.Create((a,b)=>StringComparer.Ordinal.Compare(b,a));
        foreach(var item in corpus.Rows(budget))
        {
            budget.Check();if(item.Excluded)continue;var key=item.Source+"|"+item.Row.Split;
            if(!selected.TryGetValue(key,out var queue)){if(selected.Count>=256)throw new InvalidDataException("Validation source/split bound256.");queue=new(reverse);selected.Add(key,queue);}
            var priority=Data.Hash("corpus-independent-validation-v1\n"+item.Source+"\n"+item.Row.RecordId);
            if(queue.Count<p.ValidationPerSource)queue.Enqueue((item.Row,item.Source),priority);
            else if(queue.TryPeek(out _,out var worst)&&StringComparer.Ordinal.Compare(priority,worst)<0){queue.Dequeue();queue.Enqueue((item.Row,item.Source),priority);}
        }
        if(!selected.Keys.Any(k=>k.EndsWith("|development",StringComparison.Ordinal)))throw new InvalidDataException("Held-out validation is empty.");
        var tally=selected.ToDictionary(k=>k.Key,k=>k.Value.Count,StringComparer.Ordinal);var rows=new List<ValidationRow>();
        var tokenizerPath=Data.Within(packageRoot,"tokenizer/tokenizer.json");if(Data.FileHash(tokenizerPath,budget)!=package.TokenizerSha256)throw new InvalidDataException("Final tokenizer SHA differs.");
        var tokenizer=new TokenizerJson(tokenizerPath,budget.Token);using var file=new TensorFile(Data.Within(packageRoot,"weights.safetensors"),package.WeightsSha256,budget);
        var weights=Assets.LoadWeights(file,package.Config,budget,out var head);
        try
        {
            using var device=Device(p,stageGpuUuid);var baseline=device.GetMemorySnapshot();
            GpuAdmission.RequireMemory(baseline.FreeBytes,p.MaximumOwnedBytes);
            using(var trainer=new CudaFullEncoderTrainer(device,package.Config,weights,head,p.MaximumOwnedBytes,budget.Token))
            foreach(var queue in selected.OrderBy(k=>k.Key,StringComparer.Ordinal).Select(k=>k.Value))
            {
                for(var i=0;i<p.ValidationPerSource&&queue.TryDequeue(out var item,out _);i++)
                {
                    budget.Check();
                    foreach(var rotated in item.Row.Type==MarkerQuestionType.Choice?new[]{false,true}:new[]{false})
                    {
                        budget.Check();var sequence=Workflow.Sequence(tokenizer,item.Row,p.MaximumTokens,rotated,budget);
                        var result=trainer.Evaluate(sequence.Sequence.TokenIds,sequence.Sequence.MarkerPositions,item.Row.Type,sequence.Target,budget.Token);
                        var prediction=Array.IndexOf(result.Probabilities,result.Probabilities.Max());var target=Array.IndexOf(sequence.Target,sequence.Target.Max());
                        rows.Add(new(item.Row.RecordId,item.Row.Split,item.Row.Type.ToString(),rotated,sequence.Sequence.CandidateIds,sequence.Target,result.Probabilities,result.Loss,sequence.Sequence.CandidateIds[prediction],sequence.Sequence.CandidateIds[target]));
                        if(rows.Count%10==0)Console.WriteLine($"corpus independent GPU forward validation={rows.Count}/{2*tally.Values.Sum()}");
                    }
                }
            }
            Reclaimed(device,baseline);var peak=device.GetMemorySnapshot().PeakOwnedBytes;device.Dispose();using var process=Process.GetCurrentProcess();
            Data.WriteNew(output,new CorpusValidationReceipt("PASS_new_process_saved_corpus_GPU_forward_validation_only",p.RunId,expectedPackageSha,identity,GpuUuid(p,stageGpuUuid),Environment.ProcessId,process.StartTime.ToUniversalTime(),rows.Count(r=>!r.Rotated),rows.Count(r=>r.Rotated),tally,rows.ToArray(),peak,false,false),CorpusWorkflowJson.Default.CorpusValidationReceipt);
        }
        finally{Assets.ClearWeights(weights);Array.Clear(head);}
    }

    internal static void SchedulerChecks(string output,RunBudget budget)
    {
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("Fresh scheduler receipt required.");var checks=0;
        TrainingRow Row(string id,string split="train")=>new(id,new('1',64),id,split,"en",MarkerQuestionType.Boolean,"state","question",[new("false","no"),new("true","yes")],[0,1],new('2',64),1);
        var data=new Dictionary<string,(TrainingRow Row,string Source)[]>(StringComparer.Ordinal)
        { ["a"]=[(Row("a1"),"a"),(Row("a2"),"a"),(Row("a3"),"a")],["b"]=[(Row("b1"),"b")],["c"]=[(Row("c1"),"c"),(Row("c2"),"c")] };
        var mixed=Mix(["a","b","c"],s=>data[s],6,budget).Select(i=>i.Row.RecordId).ToArray();
        if(!mixed.SequenceEqual(new[]{"a1","b1","c1","a2","c2","a3"})||mixed.Distinct(StringComparer.Ordinal).Count()!=6)throw new InvalidDataException("Uneven source schedule did not cover each row exactly once.");checks++;
        for(var cursor=0;cursor<mixed.Length;cursor++)
        {
            budget.Check();var resumed=Mix(["a","b","c"],s=>data[s],6,budget).Skip(cursor).Select(i=>i.Row.RecordId).ToArray();
            if(!mixed[..cursor].Concat(resumed).SequenceEqual(mixed))throw new InvalidDataException("Resume prefix/suffix order differs.");checks++;
        }
        var heldoutRejected=false;try{foreach(var _ in Mix(["a"],_=>new[]{(Row("heldout","development"),"a")},1,budget))budget.Check();}catch(InvalidDataException){heldoutRejected=true;}
        if(!heldoutRejected)throw new InvalidDataException("Held-out row entered reverse schedule.");checks++;
        var overflowRejected=false;try{foreach(var _ in Mix(["a"],s=>data[s],2,budget))budget.Check();}catch(InvalidDataException){overflowRejected=true;}
        if(!overflowRejected)throw new InvalidDataException("Schedule silently truncated extra rows.");checks++;
        // Cursor arithmetic uses Int64 and remains exact far beyond the old1M bound.
        const long train=15000001,completed=45000006;var epoch=completed/train;var within=completed%train;
        if(epoch!=3||within!=3||epoch*train+within!=completed)throw new InvalidDataException("64-bit epoch/cursor arithmetic differs.");checks++;
        // A real gzip fixture exercises shard EOF/hash checks, exclusion-ledger
        // filtering, split filtering and recovery on either side of an epoch.
        var fixture=Path.GetFullPath(output+".fixture");if(Directory.Exists(fixture)||File.Exists(fixture))throw new IOException("Scheduler fixture exists; preserve it.");Directory.CreateDirectory(fixture);
        var shards=new List<CorpusShard>();
        void Shard(string source,string split,TrainingRow[] values)
        {
            budget.Check();var name=source+"-"+split+".jsonl.gz";var path=Data.Within(fixture,name);
            using(var stream=new FileStream(path,FileMode.CreateNew))
            using(var gzip=new GZipStream(stream,CompressionLevel.Fastest))
            using(var writer=new StreamWriter(gzip,new UTF8Encoding(false)))
            foreach(var row in values){budget.Check();writer.WriteLine(JsonSerializer.Serialize(row with{Family=Data.Hash(row.RecordId)},TrainerJson.Default.TrainingRow));}
            shards.Add(new(name,source,split,values.Length,new FileInfo(path).Length,Data.FileHash(path,budget)));
        }
        Shard("a","train",[Row("a1"),Row("a2"),Row("a3"),Row("excluded")]);Shard("b","train",[Row("b1")]);Shard("c","train",[Row("c1"),Row("c2")]);Shard("a","development",[Row("heldout","development")]);
        var manifestPath=Data.Within(fixture,"manifest.json");Data.WriteNew(manifestPath,new CorpusManifest(1,"COMPLETE_PREPARATION_ONLY",false,false,8,shards.ToArray()),TrainerJson.Default.CorpusManifest);
        File.WriteAllText(Data.Within(fixture,"exclusions.jsonl"),JsonSerializer.Serialize(new CorpusExclusion("excluded","synthetic_conflict"),TrainerJson.Default.CorpusExclusion)+"\n",new UTF8Encoding(false));
        File.WriteAllText(Data.Within(fixture,"source-rights.json"),"[]",new UTF8Encoding(false));File.WriteAllText(Data.Within(fixture,"source-assets.json"),"[]",new UTF8Encoding(false));
        var manifestSha=Data.FileHash(manifestPath,budget);var bindingsPath=Data.Within(fixture,"corpus-bindings.json");
        Data.WriteNew(bindingsPath,new CorpusBindings(manifestSha,Data.FileHash(Data.Within(fixture,"exclusions.jsonl"),budget),Data.FileHash(Data.Within(fixture,"source-rights.json"),budget),Data.FileHash(Data.Within(fixture,"source-assets.json"),budget),false),TrainerJson.Default.CorpusBindings);
        var bindingsSha=Data.FileHash(bindingsPath,budget);var corpus=new Corpus(fixture,manifestSha,bindingsSha,budget);var checkedFixture=corpus.Check(8,budget);
        if(checkedFixture.Train!=6||checkedFixture.Development!=1||checkedFixture.ExcludedRows!=1)throw new InvalidDataException("Gzip fixture denominator differs.");checks++;
        var epochs=Epoch(corpus,7,0,budget).Concat(Epoch(corpus,7,1,budget)).Select(i=>i.Row.RecordId).ToArray();
        if(epochs.Length!=12||epochs.Any(id=>id is "heldout" or "excluded")||epochs[..6].Distinct().Count()!=6||epochs[6..].Distinct().Count()!=6)throw new InvalidDataException("Real gzip epochs omitted or leaked a row.");checks++;
        var fixturePlan=new CorpusRunPlan(1,"synthetic-scheduler",fixture,manifestSha,bindingsSha,"unused",new('3',64),"unused",new('1',40),new('2',40),new('3',40),7,2,6,8,2,1,0.00001f,0.001f,0.01f,1048576,new('0',32),true);
        for(var cursor=0;cursor<epochs.Length;cursor++)
        {
            budget.Check();var observed=From(corpus,fixturePlan,cursor,budget).Select(i=>i.Row.RecordId).ToArray();
            if(!observed.SequenceEqual(epochs[cursor..]))throw new InvalidDataException("Actual gzip epoch/cursor resume differs.");checks++;
        }
        var changedPath=Data.Within(fixture,shards[0].Path);var originalBytes=File.ReadAllBytes(changedPath);var changedRejected=false;
        try
        {
            var changed=originalBytes.ToArray();changed[0]^=1;File.WriteAllBytes(changedPath,changed);
            try{foreach(var _ in Epoch(corpus,7,0,budget))budget.Check();}catch(InvalidDataException){changedRejected=true;}
        }
        finally{File.WriteAllBytes(changedPath,originalBytes);}
        if(!changedRejected)throw new InvalidDataException("Changed source bytes entered pinned schedule.");checks++;
        var checkpoint=Data.Within(fixture,"synthetic-checkpoint");Directory.CreateDirectory(checkpoint);
        var config=new ModernBertConfig{VocabularySize=8,HiddenSize=4,IntermediateSize=6,LayerCount=2,HeadCount=2,MaxTokens=4,LocalAttention=2,GlobalAttentionEvery=2};var parameters=Assets.Descriptors(config);
        var weightsSha=TensorFile.Write(Data.Within(checkpoint,"weights.safetensors"),parameters,d=>new float[d.Elements],budget);
        var moments=parameters.SelectMany(d=>new[]{d with{Name="first."+d.Name},d with{Name="second."+d.Name}}).ToArray();
        var momentsSha=TensorFile.Write(Data.Within(checkpoint,"moments.safetensors"),moments,d=>new float[d.Elements],budget);
        var identity=Identity(fixturePlan,Data.Hash("synthetic-plan"),corpus,new('4',64),new('5',64));var cpPath=Data.Within(checkpoint,"checkpoint.json");
        Data.WriteNew(cpPath,new CheckpointReceipt(1,fixturePlan.RunId,3,identity,config,weightsSha,momentsSha),TrainerJson.Default.CheckpointReceipt);
        var partialRejected=false;try{_ = Resume(checkpoint,fixturePlan,identity,budget);}catch(FileNotFoundException){partialRejected=true;}
        if(!partialRejected)throw new InvalidDataException("Checkpoint without final corpus cursor was admitted.");checks++;
        var cursorReceipt=new CorpusCursorReceipt(1,fixturePlan.RunId,identity,3,0,3,6,2,Data.FileHash(cpPath,budget),ScheduleAlgorithm);
        File.WriteAllText(Data.Within(checkpoint,"corpus-cursor.pending"),JsonSerializer.Serialize(cursorReceipt,CorpusWorkflowJson.Default.CorpusCursorReceipt),new UTF8Encoding(false));
        var pendingRejected=false;try{_ = Resume(checkpoint,fixturePlan,identity,budget);}catch(FileNotFoundException){pendingRejected=true;}
        if(!pendingRejected)throw new InvalidDataException("Pending cursor masqueraded as completion.");checks++;
        // Pending is retained as interruption evidence; use a separate synthetic
        // root for the complete checkpoint so no fixture artifact is deleted.
        var completeCheckpoint=Data.Within(fixture,"synthetic-complete-checkpoint");Directory.CreateDirectory(completeCheckpoint);
        foreach(var name in new[]{"weights.safetensors","moments.safetensors","checkpoint.json"})Assets.CopyNew(Data.Within(checkpoint,name),Data.Within(completeCheckpoint,name),budget);
        SaveCursor(Data.Within(completeCheckpoint,"corpus-cursor.json"),cursorReceipt,budget);
        if(Resume(completeCheckpoint,fixturePlan,identity,budget)!=3)throw new InvalidDataException("Complete tiny checkpoint cursor rejected.");checks++;
        var planChangedRejected=false;try{_ = Resume(completeCheckpoint,fixturePlan,identity with{PlanSha256=Data.Hash("changed-plan")},budget);}catch(InvalidDataException){planChangedRejected=true;}
        if(!planChangedRejected)throw new InvalidDataException("Changed global plan admitted old optimizer state.");checks++;
        var optimizerPath=Data.Within(completeCheckpoint,"moments.safetensors");var optimizerBytes=File.ReadAllBytes(optimizerPath);var optimizerChangedRejected=false;
        try{var changed=optimizerBytes.ToArray();changed[^1]^=1;File.WriteAllBytes(optimizerPath,changed);try{_ = Resume(completeCheckpoint,fixturePlan,identity,budget);}catch(InvalidDataException){optimizerChangedRejected=true;}}
        finally{File.WriteAllBytes(optimizerPath,optimizerBytes);}
        if(!optimizerChangedRejected)throw new InvalidDataException("Changed AdamW moments admitted for GPU restoration.");checks++;
        var disposed=0;
        IEnumerable<(TrainingRow Row,string Source)> Tracked(string source)
        {
            try{foreach(var item in data[source]){budget.Check();yield return item;}}finally{disposed++;}
        }
        var deadlineRejected=false;
        using(var deadline=new RunBudget(2,null))
        using(var iterator=Mix(["a","b","c"],Tracked,6,deadline).GetEnumerator())
        {
            for(var i=0;i<3;i++)if(!iterator.MoveNext())throw new InvalidDataException("Deadline fixture empty.");
            deadline.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
            try{iterator.MoveNext();}catch(OperationCanceledException){deadlineRejected=true;}
        }
        if(!deadlineRejected||disposed!=3)throw new InvalidDataException("Deadline did not dispose every owned source iterator.");checks++;
        checks+=CorpusProvenancePackage.Checks(fixture,budget);
        checks+=GpuAdmission.Checks(budget);
        Data.WriteNew(output,new CorpusSchedulerReceipt("PASS_tiny_CPU_schedule_negative_checks_only",checks,ScheduleAlgorithm,false,false),CorpusWorkflowJson.Default.CorpusSchedulerReceipt);
        Console.WriteLine($"corpus scheduler checks={checks};GPU training=NOT_RUN;real corpus coverage=NOT_RUN");
    }
}
