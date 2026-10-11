using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

internal sealed record StorageReceipt(string Status,int Checks,long CompletedUpdates,int ComparedStateCoordinates,double MaximumDifference,ulong PeakOwnedBytes,bool RealModelTraining);
internal static class StorageChecks
{
    internal static void Run(string output,RunBudget budget)
    {
        if(Directory.Exists(output))throw new IOException("Storage-check output exists.");Directory.CreateDirectory(output);
        var config=new ModernBertConfig{VocabularySize=8,HiddenSize=4,IntermediateSize=6,LayerCount=2,HeadCount=2,MaxTokens=4,LocalAttention=2,GlobalAttentionEvery=2};var seed=17;
        float[] Values(int count){var values=new float[count];for(var i=0;i<count;i++){seed=unchecked(seed*1664525+1013904223);values[i]=((seed&65535)/65535f-0.5f)*0.2f;}return values;}
        var weights=new ModernBertWeights{TokenEmbeddings=Values(32),EmbeddingNorm=[1,1,1,1],FinalNorm=[1,1,1,1],Layers=Enumerable.Range(0,2).Select(i=>new ModernBertLayerWeights{AttentionNorm=i==0?null:[1,1,1,1],Qkv=Values(48),AttentionOutput=Values(16),MlpNorm=[1,1,1,1],MlpUp=Values(48),MlpDown=Values(24)}).ToArray()};
        var identity=new InputIdentity(new('1',64),new('2',64),new('3',64),new('4',64),new('5',64),new('6',64),CudaFullEncoderTrainer.BackwardManifestSha256);
        var plan=new RunPlan("synthetic-storage",output,new('2',64),output,new('1',40),new('2',40),new('3',40),17,4,4,2,1,0.00001f,0.001f,0.01f,64UL*1024*1024,new('0',32),true);
        int[] ids=[1,2,1,3];int[] markers=[1,3];float[] target=[0.2f,0.8f];var states=new Dictionary<string,float[]>();float[] probabilities;var checks=0;var coordinates=0;double maxDifference=0;
        using var device=CudaDevice.Open(0);var before=device.GetMemorySnapshot();
        void Step(CudaFullEncoderTrainer trainer){budget.Check();trainer.Backward(ids,markers,MarkerQuestionType.Choice,target,30,budget.Token);trainer.ApplyAdamW(plan.EncoderRate,plan.HeadRate,plan.WeightDecay,budget.Token);}
        var checkpoint=Data.Within(output,"checkpoint");
        using(var trainer=new CudaFullEncoderTrainer(device,config,weights,Values(12),plan.MaximumOwnedBytes,budget.Token))
        {
            Step(trainer);Step(trainer);Assets.SaveCheckpoint(checkpoint,plan,identity,trainer,budget);checks++;
            Step(trainer);Step(trainer);probabilities=trainer.Evaluate(ids,markers,MarkerQuestionType.Choice,target,budget.Token).Probabilities;
            foreach(var p in trainer.Parameters)foreach(var kind in new[]{FullEncoderStateKind.Weight,FullEncoderStateKind.FirstMoment,FullEncoderStateKind.SecondMoment}){budget.Check();states.Add(kind+"/"+p.Name,trainer.ReadState(p.Name,kind));}
        }
        using(var trainer=new CudaFullEncoderTrainer(device,config,weights,maximumOwnedBytes:plan.MaximumOwnedBytes,cancellationToken:budget.Token))
        {
            Assets.Restore(checkpoint,plan,identity,trainer,budget);if(trainer.CompletedUpdates!=2)throw new InvalidDataException("Saved update cursor differs.");checks++;
            Step(trainer);Step(trainer);var observed=trainer.Evaluate(ids,markers,MarkerQuestionType.Choice,target,budget.Token).Probabilities;
            if(!observed.SequenceEqual(probabilities))throw new ArithmeticException("Resumed predictions differ from uninterrupted updates.");checks++;
            foreach(var p in trainer.Parameters)foreach(var kind in new[]{FullEncoderStateKind.Weight,FullEncoderStateKind.FirstMoment,FullEncoderStateKind.SecondMoment})
            {
                budget.Check();var actual=trainer.ReadState(p.Name,kind);var expected=states[kind+"/"+p.Name];
                for(var i=0;i<actual.Length;i++){budget.Check();coordinates++;maxDifference=Math.Max(maxDifference,Math.Abs(actual[i]-expected[i]));if(BitConverter.SingleToInt32Bits(actual[i])!=BitConverter.SingleToInt32Bits(expected[i]))throw new ArithmeticException("Resumed AdamW state differs.");}
            }
            checks++;
        }
        var receipt=Assets.Read(Data.Within(checkpoint,"checkpoint.json"),TrainerJson.Default.CheckpointReceipt);
        using(var file=new TensorFile(Data.Within(checkpoint,"weights.safetensors"),receipt.WeightsSha256,budget))
        {
            var reloaded=Assets.LoadWeights(file,config,budget,out var head);try{reloaded.Validate(config);if(head.Length!=12)throw new InvalidDataException("Head shape differs.");checks++;}finally{Assets.ClearWeights(reloaded);Array.Clear(head);}
        }
        var rejected=false;try{using var invalid=new TensorFile(Data.Within(checkpoint,"weights.safetensors"),new('0',64),budget);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new InvalidDataException("Changed package identity was admitted.");checks++;
        var after=device.GetMemorySnapshot();if(after.OwnedBytes!=before.OwnedBytes||after.OwnedAllocationCount!=before.OwnedAllocationCount||after.LoadedModuleCount!=before.LoadedModuleCount||after.ReleaseFailureCount!=before.ReleaseFailureCount)throw new IOException("Storage tiny CUDA resources not restored.");device.Dispose();checks++;
        Data.WriteNew(Data.Within(output,"storage-checks.json"),new StorageReceipt("PASS_tiny_GPU_checkpoint_and_bitwise_resume_only",checks,4,coordinates,maxDifference,after.PeakOwnedBytes,false),TrainerJson.Default.StorageReceipt);
        Console.WriteLine($"storage checks={checks},bitwise compared coordinates={coordinates},real-model-training=false");
    }
}
