using System.Runtime.ExceptionServices;
using K = Sezika.Cuda.CudaKernels.KernelArg;

namespace Sezika.Cuda;

internal enum EncoderBackwardKernel { Clear, Copy, LinearInput, LinearWeight, NormInput, NormWeight, GatedGelu, ContextProbability, ContextValue, Softmax, AttentionQuery, AttentionKey, Rope, Join, Embedding, MarkerLoss, MarkerInput, MarkerWeight, AdamW, Finite }

internal sealed class CudaEncoderBackwardKernels : IDisposable
{
    private readonly CudaDevice _device;
    private readonly List<CudaModuleHandle> _modules = new(20);
    private readonly IntPtr[] _functions = new IntPtr[20];
    private bool _disposed;
    internal const string SourceSha256 = GeneratedEncoderBackward.SourceSha256;
    internal const string ManifestSha256 = GeneratedEncoderBackward.ManifestSha256;

    internal CudaEncoderBackwardKernels(CudaDevice device, CancellationToken token)
    {
        _device=device;
        try
        {
            for(var i=0;i<20;i++)
            {
                token.ThrowIfCancellationRequested();
                var kernel=(EncoderBackwardKernel)i;
                var module=device.LoadModule(Image(kernel));_modules.Add(module);
                _functions[i]=device.GetFunction(module,"Kernel_"+Name(kernel));
            }
        }
        catch { Dispose(); throw; }
    }
    private static string Name(EncoderBackwardKernel kernel) => kernel switch
    {
        EncoderBackwardKernel.Clear=>"Clear",EncoderBackwardKernel.Copy=>"Copy",EncoderBackwardKernel.LinearInput=>"LinearInput",
        EncoderBackwardKernel.LinearWeight=>"LinearWeight",EncoderBackwardKernel.NormInput=>"NormInput",EncoderBackwardKernel.NormWeight=>"NormWeight",
        EncoderBackwardKernel.GatedGelu=>"GatedGelu",EncoderBackwardKernel.ContextProbability=>"ContextProbability",EncoderBackwardKernel.ContextValue=>"ContextValue",
        EncoderBackwardKernel.Softmax=>"Softmax",EncoderBackwardKernel.AttentionQuery=>"AttentionQuery",EncoderBackwardKernel.AttentionKey=>"AttentionKey",
        EncoderBackwardKernel.Rope=>"Rope",EncoderBackwardKernel.Join=>"Join",EncoderBackwardKernel.Embedding=>"Embedding",
        EncoderBackwardKernel.MarkerLoss=>"MarkerLoss",EncoderBackwardKernel.MarkerInput=>"MarkerInput",EncoderBackwardKernel.MarkerWeight=>"MarkerWeight",
        EncoderBackwardKernel.AdamW=>"AdamW",EncoderBackwardKernel.Finite=>"Finite",_=>throw new ArgumentOutOfRangeException(nameof(kernel))
    };
    private static ReadOnlySpan<byte> Image(EncoderBackwardKernel kernel) => kernel switch
    {
        EncoderBackwardKernel.Clear=>GeneratedEncoderBackward.Clear,EncoderBackwardKernel.Copy=>GeneratedEncoderBackward.Copy,
        EncoderBackwardKernel.LinearInput=>GeneratedEncoderBackward.LinearInput,EncoderBackwardKernel.LinearWeight=>GeneratedEncoderBackward.LinearWeight,
        EncoderBackwardKernel.NormInput=>GeneratedEncoderBackward.NormInput,EncoderBackwardKernel.NormWeight=>GeneratedEncoderBackward.NormWeight,
        EncoderBackwardKernel.GatedGelu=>GeneratedEncoderBackward.GatedGelu,EncoderBackwardKernel.ContextProbability=>GeneratedEncoderBackward.ContextProbability,
        EncoderBackwardKernel.ContextValue=>GeneratedEncoderBackward.ContextValue,EncoderBackwardKernel.Softmax=>GeneratedEncoderBackward.Softmax,
        EncoderBackwardKernel.AttentionQuery=>GeneratedEncoderBackward.AttentionQuery,EncoderBackwardKernel.AttentionKey=>GeneratedEncoderBackward.AttentionKey,
        EncoderBackwardKernel.Rope=>GeneratedEncoderBackward.Rope,EncoderBackwardKernel.Join=>GeneratedEncoderBackward.Join,
        EncoderBackwardKernel.Embedding=>GeneratedEncoderBackward.Embedding,EncoderBackwardKernel.MarkerLoss=>GeneratedEncoderBackward.MarkerLoss,
        EncoderBackwardKernel.MarkerInput=>GeneratedEncoderBackward.MarkerInput,EncoderBackwardKernel.MarkerWeight=>GeneratedEncoderBackward.MarkerWeight,
        EncoderBackwardKernel.AdamW=>GeneratedEncoderBackward.AdamW,EncoderBackwardKernel.Finite=>GeneratedEncoderBackward.Finite,_=>throw new ArgumentOutOfRangeException(nameof(kernel))
    };
    internal unsafe void Run(EncoderBackwardKernel kernel,int work,ReadOnlySpan<K> arguments)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(work<=0||arguments.Length is <1 or >16||(uint)kernel>=20)throw new ArgumentOutOfRangeException(nameof(work));
        Span<byte> storage=stackalloc byte[arguments.Length*16];nint* pointers=stackalloc nint[arguments.Length];
        fixed(byte* begin=storage)
        {
            for(var i=0;i<arguments.Length;i++){var address=begin+i*16;pointers[i]=(nint)address;arguments[i].Write(address);}
            _device.Launch(_functions[(int)kernel],checked((uint)((work+255L)/256L)),1,256,1,new ReadOnlySpan<nint>(pointers,arguments.Length));
        }
        _device.Synchronize();
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;Exception? first=null;
        for(var i=_modules.Count-1;i>=0;i--)try{_modules[i].Dispose();}catch(Exception error){first??=error;}
        if(first is not null)ExceptionDispatchInfo.Capture(first).Throw();
    }
}
