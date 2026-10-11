using System.Diagnostics;
using System.Runtime.ExceptionServices;
using K = Sezika.Cuda.CudaKernels.KernelArg;

namespace Sezika.Cuda;

public sealed record FullEncoderParameter(string Name, int[] Shape, int Elements, bool WeightDecay, bool IsHead);
public sealed record FullEncoderGradientResult(double Loss, int TokenCount, int CandidateCount, long CompletedUpdates, string BackwardSourceSha256, string BackwardManifestSha256);
public sealed record FullEncoderPrediction(double Loss,float[] Probabilities);
public enum FullEncoderStateKind { Weight, FirstMoment, SecondMoment }

/// <summary>FP32 full-encoder reverse pass with per-layer recomputation and static PTX/Driver kernels.
/// Engineering path: callers must separately validate numerical gradients, real GPU execution and model quality.</summary>
/// <remarks>The caller must serialize all operations and disposal on this trainer and its CUDA device.</remarks>
public sealed class CudaFullEncoderTrainer : IDisposable
{
    public static string BackwardManifestSha256=>CudaEncoderBackwardKernels.ManifestSha256;
    private readonly CudaDevice _device;
    private readonly CudaKernels _forward;
    private readonly CudaEncoderBackwardKernels _backward;
    private readonly List<Parameter> _parameters=new(135);
    private readonly List<CudaDeviceBuffer> _owned=new(550);
    private readonly Layer[] _layers;
    private readonly Parameter _embedding,_embeddingNorm,_finalNorm,_head;
    private readonly CudaDeviceBuffer _finiteStatus;
    private readonly ulong _maximumBytes;
    private bool _disposed,_busy,_poisoned,_hasGradient;
    private long _updates;
    public ModernBertConfig Config { get; }
    public IReadOnlyList<FullEncoderParameter> Parameters => _parameters.Select(p=>p.Info).ToArray();
    public long CompletedUpdates=>_updates;

    public CudaFullEncoderTrainer(CudaDevice device,ModernBertConfig config,ModernBertWeights weights,
        float[]? initialHead=null,ulong maximumOwnedBytes=24UL*1024*1024*1024,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(device);ArgumentNullException.ThrowIfNull(config);ArgumentNullException.ThrowIfNull(weights);
        config.Validate();weights.Validate(config);
        if(maximumOwnedBytes is <1048576 or >25769803776)throw new ArgumentOutOfRangeException(nameof(maximumOwnedBytes));
        if(initialHead is not null&&(initialHead.Length!=3*config.HiddenSize||initialHead.Any(v=>!float.IsFinite(v))))throw new ArgumentException("Finite three-row head required.");
        Config=config;_device=device;_maximumBytes=maximumOwnedBytes;_layers=new Layer[config.LayerCount];
        _forward=new CudaKernels(device,cancellationToken);
        try
        {
            _backward=new CudaEncoderBackwardKernels(device,cancellationToken);
            _embedding=Add("model.embeddings.tok_embeddings.weight",[config.VocabularySize,config.HiddenSize],weights.TokenEmbeddings,true,false,cancellationToken);
            _embeddingNorm=Add("model.embeddings.norm.weight",[config.HiddenSize],weights.EmbeddingNorm,false,false,cancellationToken);
            _finalNorm=Add("model.final_norm.weight",[config.HiddenSize],weights.FinalNorm,false,false,cancellationToken);
            for(var i=0;i<_layers.Length;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();var l=weights.Layers[i];var prefix=$"model.layers.{i}.";
                var norm=l.AttentionNorm is null?null:Add(prefix+"attn_norm.weight",[config.HiddenSize],l.AttentionNorm,false,false,cancellationToken);
                _layers[i]=new(norm,Add(prefix+"attn.Wqkv.weight",[3*config.HiddenSize,config.HiddenSize],l.Qkv,true,false,cancellationToken),
                    Add(prefix+"attn.Wo.weight",[config.HiddenSize,config.HiddenSize],l.AttentionOutput,true,false,cancellationToken),
                    Add(prefix+"mlp_norm.weight",[config.HiddenSize],l.MlpNorm,false,false,cancellationToken),
                    Add(prefix+"mlp.Wi.weight",[2*config.IntermediateSize,config.HiddenSize],l.MlpUp,true,false,cancellationToken),
                    Add(prefix+"mlp.Wo.weight",[config.HiddenSize,config.IntermediateSize],l.MlpDown,true,false,cancellationToken));
            }
            _head=Add("head.linear.weight",[3,config.HiddenSize],initialHead??new float[3*config.HiddenSize],true,true,cancellationToken);
            _finiteStatus=Allocate((_parameters.Max(p=>p.Info.Elements)+4095)/4096);
        }
        catch { Dispose(); throw; }
    }
    private CudaDeviceBuffer Allocate(int count)
    {
        var bytes=checked((ulong)count*4);var snapshot=_device.GetMemorySnapshot();
        if(count<=0||snapshot.OwnedBytes>_maximumBytes||bytes>_maximumBytes-snapshot.OwnedBytes)throw new DecisionException("full_training_memory_budget","Full training owned-memory budget exceeded.");
        var result=_device.Allocate(checked((nuint)bytes));_owned.Add(result);return result;
    }
    private Parameter Add(string name,int[] shape,float[] source,bool decay,bool head,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(_parameters.Count>=387)throw new InvalidDataException("Parameter count bound.");
        var p=new Parameter(new(name,shape,source.Length,decay,head),Allocate(source.Length),Allocate(source.Length),Allocate(source.Length),Allocate(source.Length));
        _parameters.Add(p);_device.CopyToDevice(p.Weight,source);
        foreach(var b in new[]{p.Gradient,p.First,p.Second})_backward.Run(EncoderBackwardKernel.Clear,source.Length,[K.View(b),K.Int(source.Length)]);
        return p;
    }
    private void Check(CancellationToken token,Stopwatch watch,int seconds)
    {
        token.ThrowIfCancellationRequested();if(watch.Elapsed.TotalSeconds>=seconds)throw new TimeoutException("Full encoder step deadline.");
        var snapshot=_device.GetMemorySnapshot();if(snapshot.OwnedBytes>_maximumBytes||snapshot.ReleaseFailureCount!=0)throw new DecisionException("full_training_resources_invalid","Owned bytes or CUDA release failures invalid.");
    }
    private void Copy(CudaDeviceBuffer x,CudaDeviceBuffer y,int count)=>_backward.Run(EncoderBackwardKernel.Copy,count,[K.View(x),K.View(y),K.Int(count)]);
    private void DenseGradient(CudaDeviceBuffer x,Parameter p,CudaDeviceBuffer dy,CudaDeviceBuffer dx,int rows,int input,int output)
    {
        _backward.Run(EncoderBackwardKernel.LinearInput,rows*input,[K.View(dy),K.View(p.Weight),K.View(dx),K.Int(rows),K.Int(input),K.Int(output)]);
        _backward.Run(EncoderBackwardKernel.LinearWeight,input*output,[K.View(x),K.View(dy),K.View(p.Gradient),K.Int(rows),K.Int(input),K.Int(output)]);
    }
    private void NormGradient(CudaDeviceBuffer x,Parameter p,CudaDeviceBuffer dy,CudaDeviceBuffer dx,Frame f)
    {
        _backward.Run(EncoderBackwardKernel.NormInput,f.Rows,[K.View(x),K.View(p.Weight),K.View(dy),K.View(dx),K.View(f.Xhat),K.Int(f.Rows),K.Int(Config.HiddenSize),K.Float(Config.NormEpsilon)]);
        _backward.Run(EncoderBackwardKernel.NormWeight,Config.HiddenSize,[K.View(f.Xhat),K.View(dy),K.View(p.Gradient),K.Int(f.Rows),K.Int(Config.HiddenSize)]);
    }
    private void LayerForward(int index,CudaDeviceBuffer x,Frame f)
    {
        var h=Config.HiddenSize;var n=f.Rows;var l=_layers[index];var global=index%Config.GlobalAttentionEvery==0;
        if(l.Norm is null)Copy(x,f.Norm,n*h);else _forward.Norm(x,l.Norm.Weight,null,f.Norm,n,h,Config.NormEpsilon);
        _forward.Linear(f.Norm,l.Qkv.Weight,f.Qkv,n,h,3*h);_forward.SplitQkv(f.Qkv,f.Q,f.K,f.V,n,h);
        _forward.Rope(f.Q,f.K,n,Config.HeadCount,h/Config.HeadCount,global?Config.GlobalRopeTheta:Config.LocalRopeTheta);
        _forward.AttentionScores(f.Q,f.K,f.P,n,Config.HeadCount,h/Config.HeadCount,global?0:Config.LocalAttention/2);
        _forward.Softmax(f.P,n*Config.HeadCount,n);_forward.AttentionContext(f.P,f.V,f.Context,n,Config.HeadCount,h/Config.HeadCount);
        _forward.Linear(f.Context,l.Output.Weight,f.Update,n,h,h);Copy(x,f.Post,n*h);_forward.Add(f.Post,f.Update,n*h);
        _forward.Norm(f.Post,l.MlpNorm.Weight,null,f.Norm2,n,h,Config.NormEpsilon);
        _forward.Linear(f.Norm2,l.Up.Weight,f.Up,n,h,2*Config.IntermediateSize);_forward.GatedGelu(f.Up,f.Activated,n,Config.IntermediateSize);
        _forward.Linear(f.Activated,l.Down.Weight,f.Update,n,Config.IntermediateSize,h);Copy(f.Post,f.Result,n*h);_forward.Add(f.Result,f.Update,n*h);
    }
    public FullEncoderGradientResult Backward(int[] ids,int[] markers,MarkerQuestionType type,float[] target,int maximumSeconds=300,CancellationToken cancellationToken=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_busy||_poisoned)throw new InvalidOperationException("Trainer busy or poisoned after failed step.");
        if(maximumSeconds is <1 or >600||ids.Length is <2 or >8192||ids.Length>Config.MaxTokens||ids.Any(i=>i<0||i>=Config.VocabularySize)||markers.Length is <2 or >32||markers.Distinct().Count()!=markers.Length||markers.Any(i=>i<0||i>=ids.Length)||
            (uint)type>2||(type==MarkerQuestionType.Boolean&&markers.Length!=2)||(type==MarkerQuestionType.Score&&markers.Length>10)||target.Length!=markers.Length||target.Any(v=>!float.IsFinite(v)||v<0)||Math.Abs(target.Sum()-1)>1e-5)throw new ArgumentException("Bounded full sequence, unique markers and normalized target required.");
        _busy=true;_hasGradient=false;var watch=Stopwatch.StartNew();using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);lifetime.CancelAfter(TimeSpan.FromSeconds(maximumSeconds));var token=lifetime.Token;
        var baseline=_owned.Count;
        try
        {
            Check(token,watch,maximumSeconds);
            foreach(var p in _parameters){Check(token,watch,maximumSeconds);_backward.Run(EncoderBackwardKernel.Clear,p.Info.Elements,[K.View(p.Gradient),K.Int(p.Info.Elements)]);}
            var f=new Frame(this,ids.Length,markers.Length);_device.CopyToDevice(f.Ids,ids);_device.CopyToDevice(f.Markers,markers);_device.CopyToDevice(f.Target,target);
            var unique=ids.Distinct().Order().ToArray();_device.CopyToDevice(f.Unique,unique);
            _forward.Embedding(f.Ids,_embedding.Weight,f.Raw,f.Rows,Config.HiddenSize);_forward.Norm(f.Raw,_embeddingNorm.Weight,null,f.Boundaries[0],f.Rows,Config.HiddenSize,Config.NormEpsilon);
            for(var i=0;i<_layers.Length;i++){Check(token,watch,maximumSeconds);LayerForward(i,f.Boundaries[i],f);Copy(f.Result,f.Boundaries[i+1],f.Rows*Config.HiddenSize);}
            _forward.Norm(f.Boundaries[^1],_finalNorm.Weight,null,f.Final,f.Rows,Config.HiddenSize,Config.NormEpsilon);
            _backward.Run(EncoderBackwardKernel.MarkerLoss,1,[K.View(f.Final),K.View(f.Markers),K.View(_head.Weight),K.View(f.Target),K.View(f.Delta),K.View(f.Loss),K.Int(markers.Length),K.Int(Config.HiddenSize),K.Int((int)type)]);
            var loss=new float[1];_device.CopyFromDevice(loss,f.Loss);if(!float.IsFinite(loss[0])||loss[0]<-1e-5f)throw new ArithmeticException("Invalid GPU soft-label loss.");
            _backward.Run(EncoderBackwardKernel.MarkerInput,f.Rows*Config.HiddenSize,[K.View(f.Markers),K.View(_head.Weight),K.View(f.Delta),K.View(f.DY),K.Int(f.Rows),K.Int(markers.Length),K.Int(Config.HiddenSize),K.Int((int)type)]);
            _backward.Run(EncoderBackwardKernel.MarkerWeight,Config.HiddenSize,[K.View(f.Final),K.View(f.Markers),K.View(f.Delta),K.View(_head.Gradient),K.Int(markers.Length),K.Int(Config.HiddenSize),K.Int((int)type)]);
            NormGradient(f.Boundaries[^1],_finalNorm,f.DY,f.DX,f);Copy(f.DX,f.DY,f.Rows*Config.HiddenSize);
            for(var i=_layers.Length-1;i>=0;i--)
            {
                Check(token,watch,maximumSeconds);LayerForward(i,f.Boundaries[i],f);var l=_layers[i];var h=Config.HiddenSize;var n=f.Rows;
                DenseGradient(f.Activated,l.Down,f.DY,f.DActivated,n,Config.IntermediateSize,h);
                _backward.Run(EncoderBackwardKernel.GatedGelu,n*Config.IntermediateSize,[K.View(f.Up),K.View(f.DActivated),K.View(f.DUp),K.Int(n),K.Int(Config.IntermediateSize)]);
                DenseGradient(f.Norm2,l.Up,f.DUp,f.DNorm,n,h,2*Config.IntermediateSize);
                NormGradient(f.Post,l.MlpNorm,f.DNorm,f.DX,f);_forward.Add(f.DY,f.DX,n*h); // residual through MLP
                DenseGradient(f.Context,l.Output,f.DY,f.DContext,n,h,h);
                _backward.Run(EncoderBackwardKernel.ContextProbability,Config.HeadCount*n*n,[K.View(f.DContext),K.View(f.V),K.View(f.DP),K.Int(n),K.Int(Config.HeadCount),K.Int(h/Config.HeadCount)]);
                _backward.Run(EncoderBackwardKernel.ContextValue,n*h,[K.View(f.P),K.View(f.DContext),K.View(f.DV),K.Int(n),K.Int(Config.HeadCount),K.Int(h/Config.HeadCount)]);
                _backward.Run(EncoderBackwardKernel.Softmax,Config.HeadCount*n,[K.View(f.P),K.View(f.DP),K.Int(Config.HeadCount*n),K.Int(n)]);
                _backward.Run(EncoderBackwardKernel.AttentionQuery,n*h,[K.View(f.DP),K.View(f.K),K.View(f.DQ),K.Int(n),K.Int(Config.HeadCount),K.Int(h/Config.HeadCount)]);
                _backward.Run(EncoderBackwardKernel.AttentionKey,n*h,[K.View(f.DP),K.View(f.Q),K.View(f.DK),K.Int(n),K.Int(Config.HeadCount),K.Int(h/Config.HeadCount)]);
                _backward.Run(EncoderBackwardKernel.Rope,n*h/2,[K.View(f.DQ),K.View(f.DK),K.Int(n),K.Int(Config.HeadCount),K.Int(h/Config.HeadCount),K.Float(i%Config.GlobalAttentionEvery==0?Config.GlobalRopeTheta:Config.LocalRopeTheta)]);
                _backward.Run(EncoderBackwardKernel.Join,n*h,[K.View(f.DQ),K.View(f.DK),K.View(f.DV),K.View(f.DQkv),K.Int(n),K.Int(h)]);
                DenseGradient(f.Norm,l.Qkv,f.DQkv,f.DNorm,n,h,3*h);
                if(l.Norm is null)Copy(f.DNorm,f.DX,n*h);else NormGradient(f.Boundaries[i],l.Norm,f.DNorm,f.DX,f);
                _forward.Add(f.DY,f.DX,n*h); // residual through attention
            }
            NormGradient(f.Raw,_embeddingNorm,f.DY,f.DX,f);
            _backward.Run(EncoderBackwardKernel.Embedding,unique.Length*Config.HiddenSize,[K.View(f.Ids),K.View(f.Unique),K.View(f.DX),K.View(_embedding.Gradient),K.Int(f.Rows),K.Int(unique.Length),K.Int(Config.HiddenSize)]);
            Check(token,watch,maximumSeconds);_hasGradient=true;
            return new(Math.Max(0,loss[0]),ids.Length,markers.Length,_updates,CudaEncoderBackwardKernels.SourceSha256,CudaEncoderBackwardKernels.ManifestSha256);
        }
        catch{_poisoned=true;throw;}
        finally
        {
            Exception? first=null;
            for(var i=_owned.Count-1;i>=baseline;i--)try{_owned[i].Dispose();}catch(Exception error){first??=error;}
            _owned.RemoveRange(baseline,_owned.Count-baseline);_busy=false;
            if(first is not null){_poisoned=true;ExceptionDispatchInfo.Capture(first).Throw();}
        }
    }
    public void ApplyAdamW(float encoderRate,float headRate,float weightDecay=0.01f,CancellationToken cancellationToken=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_busy||_poisoned||!_hasGradient)throw new InvalidOperationException("A successful fresh gradient is required.");
        if(!float.IsFinite(encoderRate)||encoderRate<=0||encoderRate>0.01f||!float.IsFinite(headRate)||headRate<=0||headRate>0.1f||!float.IsFinite(weightDecay)||weightDecay<0||weightDecay>1||_updates>=1000000)throw new ArgumentOutOfRangeException(nameof(encoderRate));
        var next=_updates+1;_busy=true;using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(300));var watch=Stopwatch.StartNew();
        try
        {
            foreach(var p in _parameters)
            {
                Check(deadline.Token,watch,300);RequireFinite(p.Gradient,p.Info.Elements);
                _backward.Run(EncoderBackwardKernel.AdamW,p.Info.Elements,[K.View(p.Weight),K.View(p.Gradient),K.View(p.First),K.View(p.Second),K.Int(p.Info.Elements),K.Float(p.Info.IsHead?headRate:encoderRate),K.Float(0.9f),K.Float(0.999f),K.Float((float)(1-Math.Pow(0.9,next))),K.Float((float)(1-Math.Pow(0.999,next))),K.Float(1e-8f),K.Float(p.Info.WeightDecay?weightDecay:0)]);
                RequireFinite(p.Weight,p.Info.Elements);RequireFinite(p.First,p.Info.Elements);RequireFinite(p.Second,p.Info.Elements);
            }
            _updates=next;_hasGradient=false;
        }
        catch{_poisoned=true;throw;}
        finally{_busy=false;}
    }
    private void RequireFinite(CudaDeviceBuffer values,int count)
    {
        var blocks=(count+4095)/4096;_backward.Run(EncoderBackwardKernel.Finite,blocks,[K.View(values),K.View(_finiteStatus),K.Int(count)]);
        var status=new float[blocks];_device.CopyFromDevice(status,_finiteStatus);
        if(status.Any(v=>BitConverter.SingleToInt32Bits(v)!=1))throw new ArithmeticException("Non-finite gradient, parameter or optimizer state; abort without publishing partial update.");
    }
    public float[] ReadParameter(string name,bool gradient=false)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_busy||_poisoned)throw new InvalidOperationException("Unavailable trainer state.");
        var p=_parameters.Single(p=>p.Info.Name==name);var result=new float[p.Info.Elements];_device.CopyFromDevice(result,gradient?p.Gradient:p.Weight);return result;
    }
    public float[] ReadState(string name,FullEncoderStateKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_busy||_poisoned)throw new InvalidOperationException("Unavailable trainer state.");
        var p=_parameters.Single(p=>p.Info.Name==name);var result=new float[p.Info.Elements];
        _device.CopyFromDevice(result,kind switch{FullEncoderStateKind.Weight=>p.Weight,FullEncoderStateKind.FirstMoment=>p.First,FullEncoderStateKind.SecondMoment=>p.Second,_=>throw new ArgumentOutOfRangeException(nameof(kind))});return result;
    }
    public void RestoreState(long completedUpdates,Func<FullEncoderParameter,FullEncoderStateKind,float[]> reader,CancellationToken cancellationToken=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);ArgumentNullException.ThrowIfNull(reader);
        if(_busy||_poisoned||_updates!=0||_hasGradient||completedUpdates is <0 or >1000000)throw new InvalidOperationException("Restore requires a pristine trainer and a bounded update count.");
        _busy=true;using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(600));var watch=Stopwatch.StartNew();
        try
        {
            foreach(var p in _parameters)
            {
                foreach(var kind in new[]{FullEncoderStateKind.Weight,FullEncoderStateKind.FirstMoment,FullEncoderStateKind.SecondMoment})
                {
                    Check(deadline.Token,watch,600);var values=reader(p.Info,kind);
                    if(values.Length!=p.Info.Elements||values.Any(v=>!float.IsFinite(v))||(kind==FullEncoderStateKind.SecondMoment&&values.Any(v=>v<0)))throw new InvalidDataException("Optimizer tensor shape or finite-value contract differs.");
                    try{_device.CopyToDevice(kind switch{FullEncoderStateKind.Weight=>p.Weight,FullEncoderStateKind.FirstMoment=>p.First,_=>p.Second},values);}finally{Array.Clear(values);}
                }
            }
            _updates=completedUpdates;
        }
        catch{_poisoned=true;throw;}
        finally{_busy=false;}
    }
    public FullEncoderPrediction Evaluate(int[] ids,int[] markers,MarkerQuestionType type,float[] target,CancellationToken cancellationToken=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_busy||_poisoned||_hasGradient)throw new InvalidOperationException("Evaluate requires a clean trainer with no pending update.");
        if(ids.Length is <2 or >8192||ids.Length>Config.MaxTokens||ids.Any(i=>i<0||i>=Config.VocabularySize)||markers.Length is <2 or >32||markers.Distinct().Count()!=markers.Length||markers.Any(i=>i<0||i>=ids.Length)||(uint)type>2||(type==MarkerQuestionType.Boolean&&markers.Length!=2)||(type==MarkerQuestionType.Score&&markers.Length>10)||target.Length!=markers.Length||target.Any(v=>!float.IsFinite(v)||v<0)||Math.Abs(target.Sum()-1)>1e-5)throw new ArgumentException("Invalid evaluation sequence or target.");
        _busy=true;var baseline=_owned.Count;using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(300));var watch=Stopwatch.StartNew();
        try
        {
            var f=new Frame(this,ids.Length,markers.Length);_device.CopyToDevice(f.Ids,ids);_device.CopyToDevice(f.Markers,markers);_device.CopyToDevice(f.Target,target);
            _forward.Embedding(f.Ids,_embedding.Weight,f.Raw,f.Rows,Config.HiddenSize);_forward.Norm(f.Raw,_embeddingNorm.Weight,null,f.Boundaries[0],f.Rows,Config.HiddenSize,Config.NormEpsilon);
            for(var i=0;i<_layers.Length;i++){Check(deadline.Token,watch,300);LayerForward(i,f.Boundaries[i],f);Copy(f.Result,f.Boundaries[i+1],f.Rows*Config.HiddenSize);}
            _forward.Norm(f.Boundaries[^1],_finalNorm.Weight,null,f.Final,f.Rows,Config.HiddenSize,Config.NormEpsilon);
            _backward.Run(EncoderBackwardKernel.MarkerLoss,1,[K.View(f.Final),K.View(f.Markers),K.View(_head.Weight),K.View(f.Target),K.View(f.Delta),K.View(f.Loss),K.Int(markers.Length),K.Int(Config.HiddenSize),K.Int((int)type)]);
            var loss=new float[1];var probabilities=new float[markers.Length];_device.CopyFromDevice(loss,f.Loss);_device.CopyFromDevice(probabilities,f.Delta);
            for(var i=0;i<probabilities.Length;i++)probabilities[i]+=target[i];
            if(!float.IsFinite(loss[0])||loss[0]<-1e-5||probabilities.Any(v=>!float.IsFinite(v)||v<0||v>1)||Math.Abs(probabilities.Sum()-1)>1e-5)throw new ArithmeticException("Non-finite evaluation or invalid distribution.");
            return new(Math.Max(0,loss[0]),probabilities);
        }
        catch{_poisoned=true;throw;}
        finally
        {
            Exception? first=null;for(var i=_owned.Count-1;i>=baseline;i--)try{_owned[i].Dispose();}catch(Exception error){first??=error;}
            _owned.RemoveRange(baseline,_owned.Count-baseline);_busy=false;if(first is not null){_poisoned=true;ExceptionDispatchInfo.Capture(first).Throw();}
        }
    }
    public void Dispose()
    {
        if(_disposed)return;if(_busy)throw new InvalidOperationException("Cannot dispose active trainer.");_disposed=true;Exception? first=null;
        for(var i=_owned.Count-1;i>=0;i--)try{_owned[i].Dispose();}catch(Exception error){first??=error;}
        try{_backward?.Dispose();}catch(Exception error){first??=error;}
        try{_forward?.Dispose();}catch(Exception error){first??=error;}
        if(first is not null)ExceptionDispatchInfo.Capture(first).Throw();
    }
    private sealed record Parameter(FullEncoderParameter Info,CudaDeviceBuffer Weight,CudaDeviceBuffer Gradient,CudaDeviceBuffer First,CudaDeviceBuffer Second);
    private sealed record Layer(Parameter? Norm,Parameter Qkv,Parameter Output,Parameter MlpNorm,Parameter Up,Parameter Down);
    private sealed class Frame
    {
        internal readonly int Rows;
        internal readonly CudaDeviceBuffer Ids,Unique,Markers,Target,Raw,Norm,Qkv,Q,K,V,P,Context,Update,Post,Norm2,Up,Activated,Result,Final,Xhat,DY,DX,DNorm,DActivated,DUp,DContext,DP,DV,DQ,DK,DQkv,Delta,Loss;
        internal readonly CudaDeviceBuffer[] Boundaries;
        internal Frame(CudaFullEncoderTrainer owner,int rows,int candidates)
        {
            Rows=rows;var h=owner.Config.HiddenSize;var m=owner.Config.IntermediateSize;
            // Complete frame admission before its first allocation.
            var required=checked((ulong)(rows*3+candidates*3+1+(owner.Config.LayerCount+1L)*rows*h+26L*rows*h+6L*rows*m+2L*owner.Config.HeadCount*rows*rows)*4);
            var occupied=owner._device.GetMemorySnapshot().OwnedBytes;if(occupied>owner._maximumBytes||required>owner._maximumBytes-occupied)throw new DecisionException("full_training_frame_budget","Full reverse frame and retained layer boundaries exceed memory budget.");
            CudaDeviceBuffer A(int count)=>owner.Allocate(count);
            Ids=A(rows);Unique=A(rows);Markers=A(candidates);Target=A(candidates);Raw=A(rows*h);Norm=A(rows*h);Qkv=A(rows*3*h);Q=A(rows*h);K=A(rows*h);V=A(rows*h);P=A(checked(owner.Config.HeadCount*rows*rows));Context=A(rows*h);Update=A(rows*h);Post=A(rows*h);Norm2=A(rows*h);Up=A(rows*2*m);Activated=A(rows*m);Result=A(rows*h);Final=A(rows*h);Xhat=A(rows*h);
            DY=A(rows*h);DX=A(rows*h);DNorm=A(rows*h);DActivated=A(rows*m);DUp=A(rows*2*m);DContext=A(rows*h);DP=A(checked(owner.Config.HeadCount*rows*rows));DV=A(rows*h);DQ=A(rows*h);DK=A(rows*h);DQkv=A(rows*3*h);Delta=A(candidates);Loss=A(1);
            Boundaries=new CudaDeviceBuffer[owner.Config.LayerCount+1];for(var i=0;i<Boundaries.Length;i++)Boundaries[i]=A(rows*h);
        }
    }
}
