using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

internal sealed record RunPlan(string RunId,string DataRoot,string DataManifestSha256,string BaseModelRoot,string SourceCommit,string DatasetCommit,string ModelCommit,
    int Seed,int MaximumUpdates,int MaximumTokens,int CheckpointEvery,int ValidationPerSplit,float EncoderRate,float HeadRate,float WeightDecay,
    ulong MaximumOwnedBytes,string ExpectedGpuUuid,bool ExperimentalOnly);
internal sealed record InputIdentity(string PlanSha256,string DataManifestSha256,string RecordsSha256,string BaseEncoderSha256,string TokenizerSha256,string OrderSha256,string BackwardManifestSha256);
internal sealed record CheckpointReceipt(int SchemaVersion,string RunId,long CompletedUpdates,InputIdentity Inputs,ModernBertConfig Config,string WeightsSha256,string MomentsSha256);
internal sealed record PackageReceipt(int SchemaVersion,string RunId,string Method,string SourceCommit,string DatasetCommit,string ModelCommit,
    InputIdentity Inputs,ModernBertConfig Config,long CompletedUpdates,string WeightsSha256,string TokenizerSha256,string CheckpointReceiptSha256,
    string TrainingReportSha256,string License,string CalibrationStatus,bool QualityAccepted,bool AotAccepted);

internal static class Assets
{
    internal static T Read<T>(string path,System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info,int maximum=1048576)
    {using var stream=File.OpenRead(path);if(stream.Length>maximum)throw new InvalidDataException("JSON byte bound.");return JsonSerializer.Deserialize(stream,info)??throw new InvalidDataException("Null JSON.");}
    internal static IReadOnlyList<FullEncoderParameter> Descriptors(ModernBertConfig config)
    {
        config.Validate();var result=new List<FullEncoderParameter>();var h=config.HiddenSize;var m=config.IntermediateSize;
        void Add(string n,int[] s,bool d=true,bool head=false)=>result.Add(new(n,s,s.Aggregate(1,(a,b)=>checked(a*b)),d,head));
        Add("model.embeddings.tok_embeddings.weight",[config.VocabularySize,h]);Add("model.embeddings.norm.weight",[h],false);Add("model.final_norm.weight",[h],false);
        for(var i=0;i<config.LayerCount;i++)
        {
            var p=$"model.layers.{i}.";if(i>0)Add(p+"attn_norm.weight",[h],false);Add(p+"attn.Wqkv.weight",[3*h,h]);Add(p+"attn.Wo.weight",[h,h]);Add(p+"mlp_norm.weight",[h],false);Add(p+"mlp.Wi.weight",[2*m,h]);Add(p+"mlp.Wo.weight",[h,m]);
        }
        Add("head.linear.weight",[3,h],true,true);return result;
    }
    internal static ModernBertWeights LoadWeights(TensorFile file,ModernBertConfig config,RunBudget budget,out float[] head)
    {
        var descriptors=Descriptors(config);file.RequireNames(descriptors.Select(p=>p.Name));
        float[] Tensor(string n)=>file.Read(n,descriptors.Single(p=>p.Name==n).Shape,budget);
        head=Tensor("head.linear.weight");var layers=new ModernBertLayerWeights[config.LayerCount];
        for(var i=0;i<layers.Length;i++)
        {
            budget.Check();var p=$"model.layers.{i}.";layers[i]=new(){AttentionNorm=i==0?null:Tensor(p+"attn_norm.weight"),Qkv=Tensor(p+"attn.Wqkv.weight"),AttentionOutput=Tensor(p+"attn.Wo.weight"),MlpNorm=Tensor(p+"mlp_norm.weight"),MlpUp=Tensor(p+"mlp.Wi.weight"),MlpDown=Tensor(p+"mlp.Wo.weight")};
        }
        var weights=new ModernBertWeights{TokenEmbeddings=Tensor("model.embeddings.tok_embeddings.weight"),EmbeddingNorm=Tensor("model.embeddings.norm.weight"),FinalNorm=Tensor("model.final_norm.weight"),Layers=layers};weights.Validate(config);return weights;
    }
    internal static string SaveCheckpoint(string root,RunPlan plan,InputIdentity inputs,CudaFullEncoderTrainer trainer,RunBudget budget)
    {
        if(Directory.Exists(root))throw new IOException("Checkpoint exists; preserve it.");Directory.CreateDirectory(root);
        var weights=TensorFile.Write(Data.Within(root,"weights.safetensors"),trainer.Parameters,p=>trainer.ReadState(p.Name,FullEncoderStateKind.Weight),budget);
        var moments=trainer.Parameters.SelectMany(p=>new[]{p with{Name="first."+p.Name},p with{Name="second."+p.Name}}).ToArray();
        var optimizer=TensorFile.Write(Data.Within(root,"moments.safetensors"),moments,p=>trainer.ReadState(p.Name[(p.Name.StartsWith("first.",StringComparison.Ordinal)?6:7)..],p.Name.StartsWith("first.",StringComparison.Ordinal)?FullEncoderStateKind.FirstMoment:FullEncoderStateKind.SecondMoment),budget);
        var receipt=new CheckpointReceipt(1,plan.RunId,trainer.CompletedUpdates,inputs,trainer.Config,weights,optimizer);
        // Open/validate headers and payload hashes before the complete marker exists.
        using(var wf=new TensorFile(Data.Within(root,"weights.safetensors"),weights,budget))wf.RequireNames(trainer.Parameters.Select(p=>p.Name));
        using(var mf=new TensorFile(Data.Within(root,"moments.safetensors"),optimizer,budget))mf.RequireNames(moments.Select(p=>p.Name));
        var path=Data.Within(root,"checkpoint.json");Data.WriteNew(path,receipt,TrainerJson.Default.CheckpointReceipt);return path;
    }
    internal static void Restore(string root,RunPlan plan,InputIdentity inputs,CudaFullEncoderTrainer trainer,RunBudget budget)
    {
        var checkpoint=Read(Data.Within(root,"checkpoint.json"),TrainerJson.Default.CheckpointReceipt);
        if(checkpoint.SchemaVersion!=1||checkpoint.RunId!=plan.RunId||checkpoint.Inputs!=inputs||checkpoint.Config!=trainer.Config||checkpoint.CompletedUpdates<1||checkpoint.CompletedUpdates>=plan.MaximumUpdates)throw new InvalidDataException("Resume checkpoint identity/configuration/cursor differs.");
        using var weights=new TensorFile(Data.Within(root,"weights.safetensors"),checkpoint.WeightsSha256,budget);
        using var moments=new TensorFile(Data.Within(root,"moments.safetensors"),checkpoint.MomentsSha256,budget);
        weights.RequireNames(trainer.Parameters.Select(p=>p.Name));moments.RequireNames(trainer.Parameters.SelectMany(p=>new[]{"first."+p.Name,"second."+p.Name}));
        trainer.RestoreState(checkpoint.CompletedUpdates,(p,kind)=>kind==FullEncoderStateKind.Weight?weights.Read(p.Name,p.Shape,budget):moments.Read((kind==FullEncoderStateKind.FirstMoment?"first.":"second.")+p.Name,p.Shape,budget),budget.Token);
    }
    internal static void CopyNew(string source,string destination,RunBudget budget)
    {
        using var input=File.OpenRead(source);using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);var buffer=new byte[1048576];
        var blocks=(input.Length+buffer.Length-1)/buffer.Length;if(blocks>8192)throw new IOException("Copy limit8GiB.");
        for(long i=0;i<blocks;i++){budget.Check();var count=(int)Math.Min(buffer.Length,input.Length-input.Position);input.ReadExactly(buffer.AsSpan(0,count));output.Write(buffer,0,count);}output.Flush(true);
    }
    internal static void ClearWeights(ModernBertWeights weights)
    {
        Array.Clear(weights.TokenEmbeddings);Array.Clear(weights.EmbeddingNorm);Array.Clear(weights.FinalNorm);
        foreach(var l in weights.Layers){if(l.AttentionNorm is not null)Array.Clear(l.AttentionNorm);Array.Clear(l.Qkv);Array.Clear(l.AttentionOutput);Array.Clear(l.MlpNorm);Array.Clear(l.MlpUp);Array.Clear(l.MlpDown);}
    }
}
