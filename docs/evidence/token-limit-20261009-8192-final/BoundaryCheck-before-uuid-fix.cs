using System.Diagnostics;
using Sezika;
using Sezika.Cuda;

if (args.Length != 2 || args[0] is not ("cpu" or "tiny" or "full" or "real")) return 2;
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(args[0] == "real" ? 600 : 120));
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    var watch = Stopwatch.StartNew(); var ct = cancellation.Token;
    void Check() { ct.ThrowIfCancellationRequested(); }
    void Assert(bool ok, string name) { Check(); if (!ok) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
    var config = new ModernBertConfig { VocabularySize = 16, HiddenSize = 4, IntermediateSize = 8, LayerCount = 1, HeadCount = 1, MaxTokens = 8192 };
    float[] Matrix(int n) => Enumerable.Range(0, n).Select(i => (i % 7 - 3) * 0.03f).ToArray();
    var weights = new ModernBertWeights { TokenEmbeddings = Matrix(64), EmbeddingNorm = [1,1,1,1], FinalNorm = [1,1,1,1],
        Layers = [new() { Qkv = Matrix(48), AttentionOutput = Matrix(16), MlpNorm = [1,1,1,1], MlpUp = Matrix(64), MlpDown = Matrix(32) }] };
    config.Validate();
    try { (config with { MaxTokens = 8193 }).Validate(); throw new InvalidOperationException("8193 accepted"); }
    catch (DecisionException e) { Assert(e.Code == "model_architecture_unsupported", "8193 config rejected"); }
    Assert(CudaModernBertEncoder.EstimateWorkspaceBytes(config,8192) == 270565376, "complete H4 8192 frame byte calculation");
    if (args[0] == "cpu")
    {
        // Serialization boundary only, one H4 record and one reference update, no real encoder.
        var identity = new MarkerFeatureIdentity("sezika/fixture-8192-boundary",new string('A',64),new string('B',64),new string('C',64),4);
        var ids = Enumerable.Range(0,8192).Select(i=>i%16).ToArray();
        var example = new MarkerFeatureExample("boundary8192",MarkerQuestionType.Boolean,"en",ids,[0,8191],[[1,0,0,0],[0,1,0,0]],0);
        var set = new MarkerFeatureSet(identity,[example]);
        var state = IndependentMarkerHeadTrainer.Train(set,new(17,1,0.001f,TimeSpan.FromSeconds(5)),cancellationToken:ct);
        Directory.CreateDirectory(args[1]); var path=Path.Combine(args[1],"boundary8192.checkpoint");
        var sha=MarkerTrainingCheckpoint.SaveNew(path,state,ct); var loaded=MarkerTrainingCheckpoint.Load(path,set,sha,ct);
        Assert(loaded.CompletedSteps==1 && loaded.FeatureSha256==state.FeatureSha256, "8192 feature last marker checkpoint roundtrip");
        return 0;
    }
    var device=CudaDevice.Open(0);
    try
    {
        Assert(device.Info.Uuid=="GPU-e786e313-bc92-a63d-a833-f4e48cbafb66","pinned GPU UUID");
        if(args[0]=="real")
        {
            using var model=IndependentModelLoader.Load(args[1],new EncoderExecutionOptions {Kernel=EncoderKernelMode.Scalar,Deadline=TimeSpan.FromSeconds(600)},ct);
            Assert(model.Encoder.Config.MaxTokens==8192,"real encoder explicit8192 profile");
            var ids=Enumerable.Range(0,8192).Select(i=>i==0?2:i==8191?1:100+i%1000).ToArray();
            using(var encoder=new CudaModernBertEncoder(device,model.Encoder.Config,model.Encoder.Weights,ct,6UL*1024*1024*1024))
            {
                var before=device.GetMemorySnapshot();
                Assert(before.OwnedBytes+CudaModernBertEncoder.EstimateWorkspaceBytes(model.Encoder.Config,8192)>3UL*1024*1024*1024,"real8192 exceeds formal3GiB budget");
                Console.WriteLine("real8192_forward_started=true");
                var output=encoder.Encode(ids,ct);
                Assert(output.Length==8192*768,"real8192 complete output shape");
                for(var i=0;i<output.Length && i<8192*768;i++){if((i%65536)==0)Check(); if(!float.IsFinite(output[i]))throw new InvalidOperationException("nonfinite real output");}
                Assert(output.AsSpan(8191*768,768).ToArray().Any(x=>x!=0),"real8192 final row nonzero");
                Console.WriteLine("real8192_forward_completed=true"); Array.Clear(output);
            }
        }
        else
        {
            var count=args[0]=="tiny"?16:8192;
            var ids=Enumerable.Range(0,count).Select(i=>i%16).ToArray();
            using var cpu=new ModernBertEncoder(config,weights,new EncoderExecutionOptions {Deadline=TimeSpan.FromSeconds(120),MaxWorkspaceBytes=512L*1024*1024},ct);
            Console.WriteLine($"H4 CPU reference rows={count}"); var reference=cpu.Encode(ids,ct);
            using(var encoder=new CudaModernBertEncoder(device,config,weights,ct,3UL*1024*1024*1024))
            {
                var before=device.GetMemorySnapshot();
                try { encoder.Encode(new int[8193],ct); throw new InvalidOperationException("oversize accepted"); }
                catch(DecisionException e){Assert(e.Code=="decision_token_limit_exceeded","8193 rejects before allocation");}
                using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
                try {encoder.Encode(ids,cancelled.Token); throw new InvalidOperationException("cancelled accepted");}
                catch(OperationCanceledException){Assert(device.GetMemorySnapshot().OwnedBytes==before.OwnedBytes,"pre-cancelled forward no allocation");}
                var output=encoder.Encode(ids,ct); Assert(output.Length==reference.Length,"H4 GPU full output shape");
                double error=0; for(var i=0;i<output.Length && i<8192*4;i++){Check();error=Math.Max(error,Math.Abs(output[i]-reference[i]));}
                Assert(error<=0.0002,"H4 CPU/GPU all rows absolute error<=2e-4"); Console.WriteLine($"rows={count} maximum_absolute_error={error:R}");
                Assert(device.GetMemorySnapshot().OwnedBytes==before.OwnedBytes,"transient frame restored resident baseline");
            }
            using(var low=new CudaModernBertEncoder(device,config,weights,ct,1024*1024))
            {
                var before=device.GetMemorySnapshot();
                try{low.Encode(new int[8192],ct);throw new InvalidOperationException("overbudget accepted");}
                catch(DecisionException e){Assert(e.Code=="decision_gpu_memory_budget_exceeded" && device.GetMemorySnapshot().OwnedBytes==before.OwnedBytes,"8192 complete frame budget rejects before allocation");}
            }
        }
        var after=device.GetMemorySnapshot(); using var process=Process.GetCurrentProcess();
        Assert(after.OwnedBytes==0 && after.OwnedAllocationCount==0 && after.LoadedModuleCount==0 && after.ReleaseFailureCount==0,"encoder resources zero after disposal");
        Assert(after.PeakOwnedBytes<=6UL*1024*1024*1024 && process.PeakWorkingSet64<=4L*1024*1024*1024,"boundary6GiB/host4GiB limits");
        Console.WriteLine($"owned_peak_bytes={after.PeakOwnedBytes} host_peak_bytes={process.PeakWorkingSet64} elapsed_seconds={watch.Elapsed.TotalSeconds:R}");
    }
    finally {device.Dispose();Console.WriteLine("device_dispose_returned=true");}
    return 0;
}
catch(Exception e){Console.Error.WriteLine(e);return e is OperationCanceledException?124:1;}
finally {Console.CancelKeyPress-=cancel;}
