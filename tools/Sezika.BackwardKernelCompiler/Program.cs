using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using Sezika.Kernels;

if(args.Length is < 1 or > 2) throw new ArgumentException("output-directory [maximum-kernels 1..20]");
string[] names=["Clear","Copy","LinearInput","LinearWeight","NormInput","NormWeight","GatedGelu","ContextProbability","ContextValue","Softmax","AttentionQuery","AttentionKey","Rope","Join","Embedding","MarkerLoss","MarkerInput","MarkerWeight","AdamW","Finite"];
var maximum=args.Length==2?int.Parse(args[1]):names.Length;
if(maximum<1||maximum>names.Length)throw new ArgumentException("Kernel count out of range.");
var directory=Path.GetFullPath(args[0]);Directory.CreateDirectory(directory);
using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(180));
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;lifetime.Cancel();};
var clock=Stopwatch.StartNew();void Check(){lifetime.Token.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>=180)throw new TimeoutException("Backward PTX compilation deadline.");}
using var context=Context.Create(builder=>builder.EnableAlgorithms().Math(MathMode.Default));
using var backend=new PTXBackend(context,new CudaArchitecture(7,0),new CudaInstructionSet(7,0),null!);
var sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("src/Sezika.Kernels/EncoderBackwardKernels.cs")));
var generated=new StringBuilder("// Build-time ILGPU output; no runtime IL/Emit.\nnamespace Sezika.Cuda;\ninternal static class GeneratedEncoderBackward\n{\n");
generated.Append("internal const string SourceSha256=\"").Append(sourceHash).Append("\";\n");
var records=new List<object>();
for(var index=0;index<maximum;index++)
{
    Check();var name=names[index];var method=typeof(EncoderBackwardKernels).GetMethod(name,BindingFlags.Public|BindingFlags.Static)!;
    var compiled=(PTXCompiledKernel)backend.Compile(EntryPointDescription.FromExplicitlyGroupedKernel(method),KernelSpecialization.Empty);
    Check();var ptx=compiled.PTXAssembly.Replace("\r\n","\n").TrimEnd()+"\0";
    if(ptx.Length>1_048_576)throw new InvalidDataException("PTX byte limit.");
    var entry=Regex.Match(ptx,@"\.visible \.entry\s+([A-Za-z0-9_]+)\s*\(",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)).Groups[1].Value;
    if(entry!="Kernel_"+name)throw new InvalidDataException("Unexpected entry.");
    // ArrayView<T> ABI is the same driver-bound 16-byte {pointer,int64 length}
    // used by existing forward kernels; scalar parameters are all 32 bit.
    var signature=ptx[ptx.IndexOf(".visible .entry",StringComparison.Ordinal)..];signature=signature[..signature.IndexOf(')')];
    var abi=Regex.Matches(signature,@"\.param\s+(?:\.align\s+(\d+)\s+)?\.(\w+)\s+\w+(?:\[(\d+)\])?",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    var parameters=method.GetParameters();if(abi.Count!=parameters.Length)throw new InvalidDataException("Parameter count mismatch.");
    for(var p=0;p<parameters.Length;p++){Check();var view=parameters[p].ParameterType.IsGenericType&&parameters[p].ParameterType.GetGenericTypeDefinition()==typeof(ArrayView<>);if(view?(abi[p].Groups[1].Value!="8"||abi[p].Groups[2].Value!="b8"||abi[p].Groups[3].Value!="16"):(abi[p].Groups[2].Value is not ("b32" or "f32")))throw new InvalidDataException("Parameter ABI mismatch.");}
    var bytes=Encoding.ASCII.GetBytes(ptx);File.WriteAllBytes(Path.Combine(directory,name+".ptx"),bytes);
    generated.Append("internal static ReadOnlySpan<byte> ").Append(name).Append(" => new byte[]{");
    for(var b=0;b<bytes.Length;b++){if((b&1023)==0)Check();generated.Append("0x").Append(bytes[b].ToString("X2")).Append(',');}
    generated.Append("};\n");records.Add(new{name,entry,parameters=parameters.Length,sha256=Convert.ToHexString(SHA256.HashData(bytes))});
    Console.WriteLine($"backward PTX {index+1}/{maximum}: {name}");
}
var manifest=JsonSerializer.Serialize(new{schema_version=1,compiler="ILGPU 1.5.3",target="sm_70",ptx_version="7.0",source_sha256=sourceHash,kernels=records,actual_gpu_backward="NOT_RUN"},new JsonSerializerOptions{WriteIndented=true});
var encoding=new UTF8Encoding(false);File.WriteAllText(Path.Combine(directory,"backward-kernel-manifest.json"),manifest,encoding);
generated.Append("internal const string ManifestSha256=\"").Append(Convert.ToHexString(SHA256.HashData(encoding.GetBytes(manifest)))).Append("\";\n}\n");
File.WriteAllText(Path.Combine(directory,"GeneratedEncoderBackward.g.cs"),generated.ToString(),encoding);
