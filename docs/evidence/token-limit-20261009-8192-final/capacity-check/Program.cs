using System.Diagnostics;
using Sezika;
using Sezika.Cuda;

if (args.Length != 2 || args[0] is not ("cpu" or "gpu") || !Directory.Exists(args[1])) return 2;
try
{
using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(75));
var watch = Stopwatch.StartNew();
void Check() { cancel.Token.ThrowIfCancellationRequested(); if (watch.Elapsed.TotalSeconds >= 75) throw new TimeoutException(); }
void Assert(bool condition, string name) { Check(); if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
var identity = new MarkerFeatureIdentity("sezika/fixture-expanded-capacity", new string('A',64),new string('B',64),new string('C',64),4);
MarkerFeatureSet Set(int count)
{
    var rows = new MarkerFeatureExample[count];
    for (var i=0;i<count && i<257;i++) { Check(); rows[i]=new($"fixture-{i}",MarkerQuestionType.Boolean,"en",[1,2],[0,1],[[1,0,0,0],[0,1,0,0]],i%2); }
    return new(identity,rows);
}
var tiny=Set(1);
var directory=Path.GetFullPath(args[1]);
if (args[0]=="cpu")
{
    // One H4 synthetic question only: serialization boundary reference, no dataset/model training.
    var state=IndependentMarkerHeadTrainer.Train(tiny,new(17,1,0.001f,TimeSpan.FromSeconds(5)),cancellationToken:cancel.Token);
    Assert(state.CompletedSteps==1,"CPU one-question H4 synthetic reference");
    var checkpoint=Path.Combine(directory,"cpu-boundary.checkpoint"); var asset=Path.Combine(directory,"cpu-boundary.asset");
    var ch=MarkerTrainingCheckpoint.SaveNew(checkpoint,state,cancel.Token);var ah=IndependentMarkerHeadAssetStore.SaveNew(asset,state,cancel.Token);
    var loaded=MarkerTrainingCheckpoint.Load(checkpoint,tiny,ch,cancel.Token);var head=IndependentMarkerHeadAssetStore.Load(asset,identity,state.FeatureSha256,ah,cancel.Token);
    Assert(loaded.CompletedSteps==1 && loaded.Head.CopyWeights().SequenceEqual(state.Head.CopyWeights()) && head.Head.CopyWeights().SequenceEqual(state.Head.CopyWeights()),"tiny1 checkpoint and head disk roundtrip");
    try { IndependentMarkerHeadTrainer.Train(Set(257),new(17,1,0.001f,TimeSpan.FromSeconds(5)),cancellationToken:cancel.Token); throw new InvalidOperationException("257 accepted"); } catch(DecisionException e) { Assert(e.Code=="training_input_invalid","257 records rejected"); }
    try { IndependentMarkerHeadTrainer.Train(tiny,new(17,24577,0.001f,TimeSpan.FromSeconds(5)),cancellationToken:cancel.Token); throw new InvalidOperationException("24577 accepted"); } catch(DecisionException e) { Assert(e.Code=="training_budget_invalid","24577 updates rejected"); }
    return 0;
}
CudaDevice? device=null;
try
{
    device=CudaDevice.Open(0); var baseline=device.GetMemorySnapshot();
    var tinyState=IndependentCudaMarkerHeadTrainer.Train(device,tiny,new(17,1,0.001f,TimeSpan.FromSeconds(10)),cancellationToken:cancel.Token);
    Assert(tinyState.CompletedSteps==1,"GPU tiny1 update");
    var train=Set(162); var all=Set(215);
    var state=IndependentCudaMarkerHeadTrainer.Train(device,train,new(17,1,0.001f,TimeSpan.FromSeconds(60)),cancellationToken:cancel.Token);
    Assert(state.CompletedSteps==1,"GPU actual162 synthetic examples and1 update");
    var evaluation=IndependentCudaMarkerHeadEvaluator.Evaluate(device,all,state.Head,TimeSpan.FromSeconds(10),cancellationToken:cancel.Token);
    Assert(evaluation.ExampleCount==215 && evaluation.CompletedExamples==215 && evaluation.Rows.Length==215 && evaluation.Rows[^1].RecordId=="fixture-214","GPU actual215 synthetic denominator including last row");
    Assert(Math.Abs(evaluation.MeanLoss-evaluation.Rows.Average(x=>x.Loss))<1e-12,"GPU CE includes complete denominator");
    var checkpoint=Path.Combine(directory,"gpu-capacity.checkpoint");var asset=Path.Combine(directory,"gpu-capacity.asset");
    var ch=MarkerTrainingCheckpoint.SaveNew(checkpoint,state,cancel.Token);var ah=IndependentMarkerHeadAssetStore.SaveNew(asset,state,cancel.Token);
    var loaded=MarkerTrainingCheckpoint.Load(checkpoint,train,ch,cancel.Token);var head=IndependentMarkerHeadAssetStore.Load(asset,identity,state.FeatureSha256,ah,cancel.Token);
    Assert(loaded.CompletedSteps==1 && loaded.Head.CopyWeights().SequenceEqual(state.Head.CopyWeights()) && head.Head.CopyWeights().SequenceEqual(state.Head.CopyWeights()),"GPU expanded saved weights roundtrip");
    var after=device.GetMemorySnapshot();
    using var process=Process.GetCurrentProcess();
    Assert(after.PeakOwnedBytes<=3UL*1024*1024*1024 && process.PeakWorkingSet64<=4L*1024*1024*1024,"GPU3GiB/host4GiB synthetic capacity stage limits");
    Assert(after.OwnedBytes==baseline.OwnedBytes && after.OwnedAllocationCount==baseline.OwnedAllocationCount && after.LoadedModuleCount==baseline.LoadedModuleCount && after.ReleaseFailureCount==baseline.ReleaseFailureCount,"162-buffer training cleanup restores caller baseline");
    Console.WriteLine($"owned_peak_bytes={after.PeakOwnedBytes} elapsed_seconds={watch.Elapsed.TotalSeconds}");
    return 0;
}
finally { if(device is not null) {device.Dispose();Console.WriteLine("device_dispose_returned=true");} }
}
catch(Exception error) { Console.Error.WriteLine(error); return 1; }
