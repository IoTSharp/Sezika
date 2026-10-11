using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

internal sealed record ProfileReceipt(string Status,string RunId,InputIdentity Inputs,string GpuUuid,int MaximumTokensTested,double MaximumStepSeconds,double ElapsedSeconds,ulong PeakOwnedBytes,bool SyntheticMaximumLengthProbe,bool QualityAccepted);
internal sealed record TrainReceipt(string Status,string RunId,InputIdentity Inputs,string GpuUuid,int ProcessId,DateTime ProcessStartUtc,long InitialUpdates,long CompletedUpdates,double MeanObservedLoss,double ElapsedSeconds,ulong PeakOwnedBytes,string CheckpointReceiptSha256,bool QualityAccepted,bool Calibrated);
internal sealed record ValidationRow(string RecordId,string Split,string Type,bool Rotated,string[] CandidateIds,float[] Target,float[] Probability,double Loss,string PredictedCandidateId,string TeacherArgmaxCandidateId);
internal sealed record ValidationReceipt(string Status,string RunId,string PackageSha256,string GpuUuid,int ProcessId,DateTime ProcessStartUtc,int OriginalRows,int RotatedRows,ValidationRow[] Rows,ulong PeakOwnedBytes,bool SavedBytesVerified,bool QualityAccepted);

internal static class Workflow
{
    internal static RunPlan Plan(string path,RunBudget budget,out string hash)
    {
        hash=Data.FileHash(path,budget);var plan=Assets.Read(path,TrainerJson.Default.RunPlan);
        if(plan.RunId.Length is <3 or >64||plan.RunId.Any(c=>!char.IsAsciiLetterLower(c)&&!char.IsAsciiDigit(c)&&c!='-')||
            new[]{plan.SourceCommit,plan.DatasetCommit,plan.ModelCommit}.Any(c=>c.Length!=40||!c.All(char.IsAsciiHexDigit))||
            plan.DataManifestSha256.Length!=64||!plan.DataManifestSha256.All(char.IsAsciiHexDigit)||plan.MaximumUpdates is <2 or >1000000||plan.MaximumTokens is <2 or >8192||
            plan.CheckpointEvery is <1 or >4096||plan.ValidationPerSplit is <1 or >256||plan.MaximumOwnedBytes is <1048576 or >25769803776||
            !plan.ExperimentalOnly||string.IsNullOrWhiteSpace(plan.ExpectedGpuUuid)||plan.ExpectedGpuUuid.Length!=32||!plan.ExpectedGpuUuid.All(char.IsAsciiHexDigit)||
            !float.IsFinite(plan.EncoderRate)||plan.EncoderRate<=0||plan.EncoderRate>0.01f||!float.IsFinite(plan.HeadRate)||plan.HeadRate<=0||plan.HeadRate>0.1f||!float.IsFinite(plan.WeightDecay)||plan.WeightDecay<0||plan.WeightDecay>1)
            throw new InvalidDataException("Explicit pinned, experimental full-encoder plan, expected GPU UUID and bounded hyperparameters required.");
        return plan;
    }
    private static CudaDevice Device(RunPlan plan)
    {
        var device=CudaDevice.Open(0);
        try
        {
            if(device.Info.Uuid!=plan.ExpectedGpuUuid||device.Info.Name.IndexOf("5090",StringComparison.OrdinalIgnoreCase)<0||device.Info.TotalMemoryBytes<30UL*1024*1024*1024||device.Info.DriverVersion<12080)throw new InvalidDataException("New-instance identity, RTX5090/32GB or CUDA12.8 driver gate failed.");
            return device;
        }
        catch{device.Dispose();throw;}
    }
    private static TrainingRow[] Order(TrainingRow[] rows,int seed)
    {
        var groups=rows.Where(r=>r.Split=="train").GroupBy(r=>r.Type).OrderBy(g=>g.Key).Select(g=>g.OrderBy(r=>Data.Hash(seed.ToString(CultureInfo.InvariantCulture)+"\n"+r.RecordId),StringComparer.Ordinal).ToArray()).ToArray();
        var order=new List<TrainingRow>();for(var i=0;i<(groups.Length==0?0:groups.Max(g=>g.Length));i++)foreach(var group in groups)if(i<group.Length)order.Add(group[i]);return order.ToArray();
    }
    private static InputIdentity Identity(RunPlan plan,string hash,(DataManifest Manifest,TrainingRow[] Rows,string ManifestSha256) data,IndependentEncoderModelPackage model,TrainingRow[] order)
    {
        if(data.ManifestSha256!=plan.DataManifestSha256||data.Manifest.DatasetCommit!=plan.DatasetCommit||data.Manifest.Train<1||data.Manifest.Development<1||order.Length!=data.Manifest.Train)throw new InvalidDataException("Pinned data identity, denominator or family isolation failed.");
        return new(hash,data.ManifestSha256,data.Manifest.RecordsSha256,model.EncoderSha256,model.TokenizerSha256,Data.Hash(string.Join('\n',order.Select(r=>r.RecordId))),CudaFullEncoderTrainer.BackwardManifestSha256);
    }
    internal static (IndependentDecisionSequence Sequence,float[] Target) Sequence(TokenizerJson tokenizer,TrainingRow row,int max,bool rotate,RunBudget budget)
    {
        budget.Check();rotate=rotate&&row.Type==MarkerQuestionType.Choice;var request=Data.Request(row,out var question,rotate);
        var sequence=IndependentDecisionV1SequenceBuilder.Build(tokenizer,request,question,new(){MaxTokens=max},budget.Token);
        // Boolean/Score builders use semantic false/true or ordinal IDs; order is fixed.
        var target=rotate?row.Target.Skip(1).Concat(row.Target.Take(1)).ToArray():row.Target.ToArray();
        if(sequence.Type!=row.Type||sequence.MarkerPositions.Length!=target.Length||(row.Type==MarkerQuestionType.Choice&&!sequence.CandidateIds.SequenceEqual(rotate?row.Candidates.Skip(1).Concat(row.Candidates.Take(1)).Select(c=>c.Id):row.Candidates.Select(c=>c.Id))))throw new InvalidDataException("Candidate identity/soft target mapping differs.");
        return(sequence,target);
    }
    internal static void Run(string command,string planPath,string output,string? profilePath,string? resume,RunBudget budget)
    {
        var watch=Stopwatch.StartNew();var plan=Plan(planPath,budget,out var planHash);if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output exists; preserve it.");
        var data=Data.Open(plan.DataRoot,budget);var order=Order(data.Rows,plan.Seed);if(order.Length==0)throw new InvalidDataException("No training rows.");
        using var model=IndependentModelLoader.Load(plan.BaseModelRoot,new(){Kernel=EncoderKernelMode.Scalar},budget.Token);
        var identity=Identity(plan,planHash,data,model,order);var maximumTokens=0;var maximumTrainingTokens=0;TrainingRow? longest=null;
        foreach(var row in data.Rows)
        {
            budget.Check();var item=Sequence(model.Tokenizer,row,plan.MaximumTokens,false,budget);maximumTokens=Math.Max(maximumTokens,item.Sequence.TokenIds.Length);
            if(row.Split=="train"&&item.Sequence.TokenIds.Length>maximumTrainingTokens){maximumTrainingTokens=item.Sequence.TokenIds.Length;longest=row;}
        }
        using var device=Device(plan);var baseline=device.GetMemorySnapshot();
        if(baseline.FreeBytes<plan.MaximumOwnedBytes+1024UL*1024*1024)throw new InvalidDataException("Insufficient free GPU memory with reserved driver overhead.");
        if(command=="train")
        {
            if(profilePath is null)throw new InvalidDataException("Hash-bound new-instance profile required before training.");
            var profile=Assets.Read(profilePath,TrainerJson.Default.ProfileReceipt);
            if(!File.Exists(Data.Within(Path.GetDirectoryName(Path.GetFullPath(profilePath))!,"resources-reclaimed.txt"))||profile.Status!="PASS_real_base_profile_only"||profile.RunId!=plan.RunId||profile.Inputs!=identity||profile.GpuUuid!=device.Info.Uuid||profile.MaximumTokensTested<maximumTokens||!double.IsFinite(profile.MaximumStepSeconds)||profile.MaximumStepSeconds<=0)throw new InvalidDataException("Profile/data/GPU/maximum-length gate differs.");
            // Bound this train phase and its saves. Independent validation and
            // transfer/publishing have separate lease reserves and stage limits.
            var resumeUpdates=resume is null?0:Assets.Read(Data.Within(resume,"checkpoint.json"),TrainerJson.Default.CheckpointReceipt).CompletedUpdates;
            var worst=4*profile.MaximumStepSeconds*(plan.MaximumUpdates-resumeUpdates)+600+120*(1+(plan.MaximumUpdates-resumeUpdates)/plan.CheckpointEvery);
            if(worst>budget.RemainingSeconds)throw new InvalidDataException($"Insufficient stage budget: conservative estimate {worst:F0}s, remaining {budget.RemainingSeconds:F0}s. Preserve fixed plan; request a sufficient lease or revise scope explicitly.");
        }
        Directory.CreateDirectory(output);double observedLoss=0;string? checkpointPath=null;long initial=0;double maxStep=0;
        using(var trainer=new CudaFullEncoderTrainer(device,model.Encoder.Config,model.Encoder.Weights,maximumOwnedBytes:plan.MaximumOwnedBytes,cancellationToken:budget.Token))
        {
            if(resume is not null){Assets.Restore(resume,plan,identity,trainer,budget);initial=trainer.CompletedUpdates;}
            // Repeat the same type after its zero-head first update so the second
            // reverse pass must propagate a meaningful nonzero encoder gradient.
            var bound=command=="profile"?3:plan.MaximumUpdates;var selected=command=="profile"?new[]{order[0],order[0],longest!}:order;
            for(long step=initial;step<bound;step++)
            {
                budget.Check();var row=selected[(int)(step%selected.Length)];if(row.Split!="train")throw new InvalidDataException("Held-out rows cannot enter any reverse/update path, including profile.");
                var item=Sequence(model.Tokenizer,row,plan.MaximumTokens,command=="train"&&step%2==1,budget);var timer=Stopwatch.StartNew();var ids=item.Sequence.TokenIds;
                // Test the largest required frame without sending development labels to
                // reverse mode. This explicit synthetic shape probe keeps train targets.
                if(command=="profile"&&step==2&&ids.Length<maximumTokens)ids=ids.Concat(Enumerable.Repeat(model.Tokenizer.EosId,maximumTokens-ids.Length)).ToArray();
                var result=trainer.Backward(ids,item.Sequence.MarkerPositions,row.Type,item.Target,600,budget.Token);
                if(command=="profile"&&step==1)
                {
                    var gradient=trainer.ReadParameter("model.layers.0.attn.Wqkv.weight",true);
                    try{if(!gradient.Any(v=>float.IsFinite(v)&&v!=0))throw new ArithmeticException("Real base encoder gradients remain zero after the first head update.");}finally{Array.Clear(gradient);}
                }
                trainer.ApplyAdamW(plan.EncoderRate,plan.HeadRate,plan.WeightDecay,budget.Token);
                maxStep=Math.Max(maxStep,timer.Elapsed.TotalSeconds);observedLoss+=result.Loss;
                Console.WriteLine($"{command} updates={trainer.CompletedUpdates}/{bound},tokens={result.TokenCount},loss={result.Loss:R},elapsed={watch.Elapsed.TotalSeconds:F1}s");
                if(command=="train"&&(trainer.CompletedUpdates%plan.CheckpointEvery==0||trainer.CompletedUpdates==bound))
                {
                    var root=Data.Within(output,"checkpoint-"+trainer.CompletedUpdates.ToString("D7",CultureInfo.InvariantCulture));checkpointPath=Assets.SaveCheckpoint(root,plan,identity,trainer,budget);TrimCheckpoints(output,plan.RunId,budget);
                }
            }
            if(command=="profile")
            {
                Data.WriteNew(Data.Within(output,"profile.json"),new ProfileReceipt("PASS_real_base_profile_only",plan.RunId,identity,device.Info.Uuid,maximumTokens,maxStep,watch.Elapsed.TotalSeconds,device.GetMemorySnapshot().PeakOwnedBytes,maximumTokens>maximumTrainingTokens,false),TrainerJson.Default.ProfileReceipt);
            }
            else
            {
                if(checkpointPath is null||trainer.CompletedUpdates!=plan.MaximumUpdates)throw new InvalidDataException("Full requested update budget not completed.");
                var checkpointSha=Data.FileHash(checkpointPath,budget);using var process=Process.GetCurrentProcess();var report=new TrainReceipt("PASS_experimental_GPU_updates_saved_only",plan.RunId,identity,device.Info.Uuid,Environment.ProcessId,process.StartTime.ToUniversalTime(),initial,trainer.CompletedUpdates,observedLoss/(trainer.CompletedUpdates-initial),watch.Elapsed.TotalSeconds,device.GetMemorySnapshot().PeakOwnedBytes,checkpointSha,false,false);
                var reportPath=Data.Within(output,"training-report.json");Data.WriteNew(reportPath,report,TrainerJson.Default.TrainReceipt);
                var checkpoint=Assets.Read(checkpointPath,TrainerJson.Default.CheckpointReceipt);Assets.CopyNew(Data.Within(Path.GetDirectoryName(checkpointPath)!,"weights.safetensors"),Data.Within(output,"weights.safetensors"),budget);
                Directory.CreateDirectory(Data.Within(output,"tokenizer"));Assets.CopyNew(Data.Within(plan.BaseModelRoot,"tokenizer/tokenizer.json"),Data.Within(output,"tokenizer/tokenizer.json"),budget);
                Directory.CreateDirectory(Data.Within(output,"notices"));
                foreach(var name in new[]{"LICENSE-COMPONENTS.md","license-provenance.json"})Assets.CopyNew(Data.Within(plan.BaseModelRoot,name),Data.Within(output,"base-"+name),budget);
                foreach(var name in new[]{"BoolQ-CC-BY-SA-3.0-legalcode.html","BoolQ-source-README.md","Gemma-prohibited-use-policy-current.html","Gemma-terms-current.html","MASSIVE-CC-BY-4.0-LICENSE.txt","MIT-LICENSE-TEMPLATE.txt","mmbert-pinned-README.md"})Assets.CopyNew(Data.Within(plan.BaseModelRoot,"notices/"+name),Data.Within(output,"notices/"+name),budget);
                foreach(var name in new[]{"SOURCE-README.md","source-rights.json"})Assets.CopyNew(Data.Within(plan.DataRoot,name),Data.Within(output,"notices/"+name),budget);
                var bundleRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));foreach(var name in new[]{"code-LICENSE.txt","code-NOTICE.txt"})Assets.CopyNew(Data.Within(bundleRoot,name),Data.Within(output,"notices/"+name),budget);
                File.WriteAllText(Data.Within(output,"LICENSE-COMPONENTS.md"),"# 本轮完整微调实验资产\n\n模型包采用分项声明，整体许可证 unknown，不将 encoder MIT 或代码 Apache-2.0 套给 tokenizer/训练输出。\n\nEncoder: jhu-clsp/mmBERT-base，固定模型卡声明 MIT；本轮完整微调、零初始化新 head，来源和超参见 package.json/run-plan.json。\nTokenizer: 同一固定 SHA，原仓库说明 Gemma 来源但精确 Google model/revision 未知；保留 Gemma Notice 与原条款快照，原 base-LICENSE-COMPONENTS.md 是历史底座说明，不是新训练报告。\n\nGemma is provided under and subject to the Gemma Terms of Use found at ai.google.dev/gemma/terms.\n\nData: kaivoss/system-one-270m-data，Apache-2.0 declared，固定 revision、teacher soft targets 与 rights decision 见 input-data-manifest.json、notices/SOURCE-README.md、notices/source-rights.json。署名/来源及派生账本保留；训练输出许可证不自动从数据声明推导。\nCode: Sezika Apache-2.0，原 LICENSE/NOTICE 在 notices/code-*.txt。\n\nFormal quality / calibration / new-weight AOT / performance: NOT_ACCEPTED。\n");
                Assets.CopyNew(planPath,Data.Within(output,"run-plan.json"),budget);Assets.CopyNew(Data.Within(plan.DataRoot,"data-manifest.json"),Data.Within(output,"input-data-manifest.json"),budget);
                Data.WriteNew(Data.Within(output,"package.json"),new PackageReceipt(1,plan.RunId,"full_encoder_adamw_fp32",plan.SourceCommit,plan.DatasetCommit,plan.ModelCommit,identity,trainer.Config,trainer.CompletedUpdates,checkpoint.WeightsSha256,model.TokenizerSha256,checkpointSha,Data.FileHash(reportPath,budget),"unknown_overall_component_specific_see_LICENSE-COMPONENTS.md","uncalibrated",false,false),TrainerJson.Default.PackageReceipt);
            }
        }
        var after=device.GetMemorySnapshot();if(after.OwnedBytes!=baseline.OwnedBytes||after.OwnedAllocationCount!=baseline.OwnedAllocationCount||after.LoadedModuleCount!=baseline.LoadedModuleCount||after.ReleaseFailureCount!=baseline.ReleaseFailureCount)throw new IOException("CUDA resources not restored after training/profile.");
        // A separate success marker is written only after CUDA buffers/modules were reclaimed.
        device.Dispose();File.WriteAllText(Data.Within(output,"resources-reclaimed.txt"),"PASS_owned_buffers_modules_restored_and_context_disposed\n");
    }
    private static void TrimCheckpoints(string output,string runId,RunBudget budget)
    {
        var owned=Directory.EnumerateDirectories(output,"checkpoint-*",SearchOption.TopDirectoryOnly).Take(4).Order(StringComparer.Ordinal).ToArray();if(owned.Length>3)throw new IOException("Checkpoint directory bound3.");
        if(owned.Length<=2)return;var oldest=owned[0];var receipt=Assets.Read(Data.Within(oldest,"checkpoint.json"),TrainerJson.Default.CheckpointReceipt);if(receipt.RunId!=runId)throw new IOException("Checkpoint ownership differs.");
        using(var weights=new TensorFile(Data.Within(oldest,"weights.safetensors"),receipt.WeightsSha256,budget)){}
        using(var moments=new TensorFile(Data.Within(oldest,"moments.safetensors"),receipt.MomentsSha256,budget)){}
        if(Directory.EnumerateFileSystemEntries(oldest).Take(4).Count()!=3)throw new IOException("Unexpected retained checkpoint file.");
        foreach(var name in new[]{"weights.safetensors","moments.safetensors","checkpoint.json"}){budget.Check();File.Delete(Data.Within(oldest,name));}Directory.Delete(oldest,false);
    }
    internal static void Validate(string packageRoot,string expectedPackageSha,string planPath,string output,RunBudget budget)
    {
        var plan=Plan(planPath,budget,out var planHash);var packagePath=Data.Within(packageRoot,"package.json");if(Data.FileHash(packagePath,budget)!=expectedPackageSha)throw new InvalidDataException("Explicit new package SHA mismatch.");
        var package=Assets.Read(packagePath,TrainerJson.Default.PackageReceipt);var data=Data.Open(plan.DataRoot,budget);var order=Order(data.Rows,plan.Seed);
        if(package.SchemaVersion!=1||package.RunId!=plan.RunId||package.Method!="full_encoder_adamw_fp32"||package.SourceCommit!=plan.SourceCommit||package.DatasetCommit!=plan.DatasetCommit||package.ModelCommit!=plan.ModelCommit||package.Inputs.PlanSha256!=planHash||package.Inputs.DataManifestSha256!=data.ManifestSha256||package.Inputs.RecordsSha256!=data.Manifest.RecordsSha256||package.Inputs.OrderSha256!=Data.Hash(string.Join('\n',order.Select(r=>r.RecordId)))||package.Inputs.BackwardManifestSha256!=CudaFullEncoderTrainer.BackwardManifestSha256||package.CompletedUpdates!=plan.MaximumUpdates||package.CalibrationStatus!="uncalibrated")throw new InvalidDataException("Fine-tuned package identity/input/update contract differs.");
        var report=Assets.Read(Data.Within(packageRoot,"training-report.json"),TrainerJson.Default.TrainReceipt);
        if(Data.FileHash(Data.Within(packageRoot,"training-report.json"),budget)!=package.TrainingReportSha256||report.Inputs!=package.Inputs||report.CompletedUpdates!=package.CompletedUpdates||report.ProcessId==Environment.ProcessId||!File.Exists(Data.Within(packageRoot,"resources-reclaimed.txt")))throw new InvalidDataException("Training report/resources marker or new-process identity mismatch.");
        var tokenizerPath=Data.Within(packageRoot,"tokenizer/tokenizer.json");if(Data.FileHash(tokenizerPath,budget)!=package.TokenizerSha256||package.TokenizerSha256!=IndependentModelLoader.TokenizerSha256||package.Inputs.BaseEncoderSha256!=IndependentModelLoader.ConvertedEncoderSha256)throw new InvalidDataException("Base/tokenizer contract differs.");
        using var file=new TensorFile(Data.Within(packageRoot,"weights.safetensors"),package.WeightsSha256,budget);var weights=Assets.LoadWeights(file,package.Config,budget,out var head);var tokenizer=new TokenizerJson(tokenizerPath,budget.Token);var rows=new List<ValidationRow>();
        using var device=Device(plan);var baseline=device.GetMemorySnapshot();
        try
        {
            using(var trainer=new CudaFullEncoderTrainer(device,package.Config,weights,head,plan.MaximumOwnedBytes,budget.Token))
            {
                foreach(var split in new[]{"train","development"})
                {
                    var subset=data.Rows.Where(r=>r.Split==split).OrderBy(r=>Data.Hash("validation-v1\n"+r.RecordId),StringComparer.Ordinal).Take(plan.ValidationPerSplit).ToArray();
                    if(subset.Length!=plan.ValidationPerSplit)throw new InvalidDataException("Validation denominator smaller than fixed contract.");
                    foreach(var row in subset)
                    {
                        foreach(var rotated in row.Type==MarkerQuestionType.Choice?new[]{false,true}:new[]{false})
                        {
                            var item=Sequence(tokenizer,row,plan.MaximumTokens,rotated,budget);var result=trainer.Evaluate(item.Sequence.TokenIds,item.Sequence.MarkerPositions,row.Type,item.Target,budget.Token);
                            var predicted=Array.IndexOf(result.Probabilities,result.Probabilities.Max());var target=Array.IndexOf(item.Target,item.Target.Max());
                            rows.Add(new(row.RecordId,split,row.Type.ToString(),rotated,item.Sequence.CandidateIds,item.Target,result.Probabilities,result.Loss,item.Sequence.CandidateIds[predicted],item.Sequence.CandidateIds[target]));
                            Console.WriteLine($"independent validation {rows.Count}/{4*plan.ValidationPerSplit}");
                        }
                    }
                }
            }
            var after=device.GetMemorySnapshot();if(after.OwnedBytes!=baseline.OwnedBytes||after.OwnedAllocationCount!=baseline.OwnedAllocationCount||after.LoadedModuleCount!=baseline.LoadedModuleCount||after.ReleaseFailureCount!=baseline.ReleaseFailureCount)throw new IOException("Validation CUDA resources did not return.");
            var process=Process.GetCurrentProcess();Data.WriteNew(output,new ValidationReceipt("PASS_new_package_GPU_experimental_validation_only",plan.RunId,expectedPackageSha,device.Info.Uuid,Environment.ProcessId,process.StartTime.ToUniversalTime(),rows.Count(r=>!r.Rotated),rows.Count(r=>r.Rotated),rows.ToArray(),after.PeakOwnedBytes,true,false),TrainerJson.Default.ValidationReceipt);
        }
        finally{Assets.ClearWeights(weights);Array.Clear(head);}
    }
}
