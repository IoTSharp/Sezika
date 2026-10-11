using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

if(args.Length!=4||args[0]!="--checks"||args[2]!="--output"||!int.TryParse(args[1],out var checks)||checks is <1 or >4)
    throw new ArgumentException("--checks 1..4 --output <new-json>; synthetic GPU gradients only, no real model training");
if(File.Exists(args[3]))throw new IOException("Output exists; preserve original receipt.");
using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(120));Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
var watch=Stopwatch.StartNew();void Guard(){cancel.Token.ThrowIfCancellationRequested();if(watch.Elapsed.TotalSeconds>=120)throw new TimeoutException("Gradient checks deadline.");}
var config=new ModernBertConfig{VocabularySize=8,HiddenSize=4,IntermediateSize=6,LayerCount=2,HeadCount=2,MaxTokens=4,GlobalAttentionEvery=2,LocalAttention=2};
var seed=17;float[] Values(int count,float scale=0.12f){var result=new float[count];for(var i=0;i<count;i++){seed=unchecked(seed*1664525+1013904223);result[i]=((seed&65535)/65535f-0.5f)*scale;}return result;}
var weights=new ModernBertWeights{TokenEmbeddings=Values(32),EmbeddingNorm=[1,0.9f,1.1f,0.95f],FinalNorm=[1,1.1f,0.9f,1],Layers=Enumerable.Range(0,2).Select(i=>new ModernBertLayerWeights{AttentionNorm=i==0?null:[0.9f,1,1.1f,1],Qkv=Values(48),AttentionOutput=Values(16),MlpNorm=[1,0.95f,1.05f,1],MlpUp=Values(48),MlpDown=Values(24)}).ToArray()};
var head=Values(12,0.8f);int[] ids=[1,2,1,3];int[] markers=[1,3];float[] target=[0.2f,0.8f];
var source=new Dictionary<string,float[]>(StringComparer.Ordinal){["model.embeddings.tok_embeddings.weight"]=weights.TokenEmbeddings,["model.embeddings.norm.weight"]=weights.EmbeddingNorm,["model.final_norm.weight"]=weights.FinalNorm,["head.linear.weight"]=head};
for(var i=0;i<2;i++){var l=weights.Layers[i];var p=$"model.layers.{i}.";if(l.AttentionNorm is not null)source[p+"attn_norm.weight"]=l.AttentionNorm;source[p+"attn.Wqkv.weight"]=l.Qkv;source[p+"attn.Wo.weight"]=l.AttentionOutput;source[p+"mlp_norm.weight"]=l.MlpNorm;source[p+"mlp.Wi.weight"]=l.MlpUp;source[p+"mlp.Wo.weight"]=l.MlpDown;}
using var reference=new ModernBertEncoder(config,weights,new EncoderExecutionOptions{Kernel=EncoderKernelMode.Scalar,Deadline=TimeSpan.FromSeconds(2)});
double Loss(MarkerQuestionType type){Guard();var hidden=reference.Encode(ids,cancel.Token);var logits=markers.Select(p=>Enumerable.Range(0,4).Sum(d=>(double)hidden[p*4+d]*head[(int)type*4+d])).ToArray();var max=logits.Max();return max+Math.Log(logits.Sum(v=>Math.Exp(v-max)))-logits.Zip(target).Sum(p=>p.First*p.Second);}
using var device=CudaDevice.Open(0);var before=device.GetMemorySnapshot();
var rows=new List<GradientCheck>();double maxError=0;var coordinates=0;
using(var trainer=new CudaFullEncoderTrainer(device,config,weights,head,maximumOwnedBytes:64UL*1024*1024,cancellationToken:cancel.Token))
{
    var types=new[]{MarkerQuestionType.Choice,MarkerQuestionType.Boolean,MarkerQuestionType.Score};
    for(var test=0;test<Math.Min(checks,3);test++)
    {
        Guard();var type=types[test];var observed=trainer.Backward(ids,markers,type,target,30,cancel.Token);var expectedLoss=Loss(type);
        if(Math.Abs(observed.Loss-expectedLoss)>2e-5)throw new ArithmeticException("GPU/CPU full-graph loss differs.");
        double maximum=0;var checkedCoordinates=0;var gradientNonzero=false;
        foreach(var p in trainer.Parameters)
        {
            Guard();var gradient=trainer.ReadParameter(p.Name,true);var values=source[p.Name];
            for(var coordinate=0;coordinate<values.Length;coordinate++)
            {
                Guard();if(++coordinates>1600)throw new TimeoutException("Coordinate limit1600.");var value=values[coordinate];const float epsilon=0.0001f;
                double plus,minus;try{values[coordinate]=value+epsilon;plus=Loss(type);values[coordinate]=value-epsilon;minus=Loss(type);}finally{values[coordinate]=value;}
                var numerical=(plus-minus)/(2*epsilon);var error=Math.Abs(numerical-gradient[coordinate]);maximum=Math.Max(maximum,error);
                if(error>0.0003+0.005*Math.Abs(numerical))throw new ArithmeticException($"Gradient mismatch {type}/{p.Name}/{coordinate}: GPU={gradient[coordinate]:R},finite_difference={numerical:R},error={error:R}");
                if(!p.IsHead&&Math.Abs(gradient[coordinate])>1e-7)gradientNonzero=true;checkedCoordinates++;
            }
        }
        if(!gradientNonzero)throw new ArithmeticException("Encoder gradients are all zero.");
        maxError=Math.Max(maxError,maximum);rows.Add(new(type.ToString(),checkedCoordinates,maximum,observed.Loss,expectedLoss));Console.WriteLine($"gradient check {test+1}/{checks}: {type},coordinates={checkedCoordinates},max_absolute_error={maximum:R}");
    }
    if(checks==4)
    {
        Guard();var result=trainer.Backward(ids,markers,MarkerQuestionType.Choice,target,30,cancel.Token);
        var gradients=trainer.Parameters.ToDictionary(p=>p.Name,p=>trainer.ReadParameter(p.Name,true));trainer.ApplyAdamW(0.00001f,0.001f,0.01f,cancel.Token);
        foreach(var p in trainer.Parameters)
        {
            Guard();var updated=trainer.ReadParameter(p.Name);var old=source[p.Name];var g=gradients[p.Name];var rate=p.IsHead?0.001:0.00001;var decay=p.WeightDecay?0.01:0;
            for(var i=0;i<old.Length;i++){Guard();var expected=old[i]*(1-rate*decay)-rate*g[i]/(Math.Abs(g[i])+1e-8);if(Math.Abs(updated[i]-expected)>2e-6)throw new ArithmeticException("First AdamW update differs from independent numerical formula.");}
        }
        if(trainer.CompletedUpdates!=1)throw new ArithmeticException("Update count differs.");rows.Add(new("AdamW",source.Values.Sum(v=>v.Length),0,result.Loss,result.Loss));Console.WriteLine("gradient check 4/4: first AdamW update and all parameter families");
    }
}
Guard();var after=device.GetMemorySnapshot();if(after.OwnedBytes!=before.OwnedBytes||after.OwnedAllocationCount!=before.OwnedAllocationCount||after.LoadedModuleCount!=before.LoadedModuleCount||after.ReleaseFailureCount!=before.ReleaseFailureCount)throw new ArithmeticException("Tiny trainer did not restore caller-owned CUDA resources.");
var receipt=new GradientReceipt("PASS_tiny_synthetic_GPU_full_graph_only",checks,coordinates,maxError,device.Info.Uuid,rows.ToArray(),after.PeakOwnedBytes,watch.Elapsed.TotalSeconds,false,false);
using(var file=new FileStream(args[3],FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(file,receipt,CheckJson.Default.GradientReceipt);
return 0;

record GradientCheck(string Type,int Coordinates,double MaximumAbsoluteError,double GpuLoss,double CpuLoss);
record GradientReceipt(string Status,int PassedChecks,int Coordinates,double MaximumAbsoluteError,string GpuUuid,GradientCheck[] Results,ulong PeakOwnedBytes,double ElapsedSeconds,bool RealModelTraining,bool QualityAcceptance);
[JsonSerializable(typeof(GradientReceipt))]
[JsonSourceGenerationOptions(WriteIndented=true)]
partial class CheckJson:JsonSerializerContext;
