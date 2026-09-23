using System.Diagnostics;
using Sezika;
using Sezika.Cuda;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(".artifacts", "models", "laya-mmbert"));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var loadWatch = Stopwatch.StartNew();
using var model = ModernBertModelLoader.Load(root, timeout.Token);
loadWatch.Stop();
var tokenIds = model.Tokenizer.Encode("Hello world", 64);
Console.WriteLine($"model={model.ModelId} revision={model.Revision} license={model.License} tokens=[{string.Join(',', tokenIds)}] load_seconds={loadWatch.Elapsed.TotalSeconds:F3}");
var maskTokens = new[] { 2, tokenIds.Length > 2 ? tokenIds[1] : 25957, 4, 4, 1 };

var cpuTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
model.Encoder.Trace = (name, values) => cpuTrace[name] = values;
var cpuWatch = Stopwatch.StartNew();
var hidden = model.Encoder.Encode(tokenIds, timeout.Token);
cpuWatch.Stop();
_ = model.Encoder.Encode(maskTokens, timeout.Token);
model.Encoder.Trace = null;
Console.WriteLine($"cpu_encoder tokens={tokenIds.Length} hidden={hidden.Length} seconds={cpuWatch.Elapsed.TotalSeconds:F3} finite={hidden.All(float.IsFinite)}");

var pipeline = new ModernBertDecisionPipeline(model.Encoder, model.Head);
var scoreWatch = Stopwatch.StartNew();
var logits = pipeline.Score(maskTokens, typeId: 0, markerPositions: new[] { 2, 3 }, timeout.Token);
scoreWatch.Stop();
Console.WriteLine($"cpu_head marker_logits=[{string.Join(',', logits.Select(x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}] seconds={scoreWatch.Elapsed.TotalSeconds:F3}");

if (CudaDevice.IsAvailable)
{
    using var device = CudaDevice.Open();
    var gpuWatch = Stopwatch.StartNew();
    using var gpuEncoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, timeout.Token);
    var gpuTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
    gpuEncoder.Trace = (name, values) => gpuTrace[name] = values;
    _ = gpuEncoder.Encode(maskTokens, timeout.Token);
    gpuEncoder.Trace = null;
    using var gpuPipeline = new CudaDecisionPipeline(device, gpuEncoder, model.Head, timeout.Token);
    var gpuLogits = gpuPipeline.Score(maskTokens, 0, new[] { 2, 3 }, timeout.Token);
    gpuWatch.Stop();
    var maxError = gpuLogits.Zip(logits, (a, b) => MathF.Abs(a - b)).DefaultIfEmpty().Max();
    var traceDiffs = cpuTrace.Keys.Intersect(gpuTrace.Keys, StringComparer.Ordinal).Select(name => (Name: name, Error: cpuTrace[name].Zip(gpuTrace[name], (a, b) => MathF.Abs(a - b)).DefaultIfEmpty().Max())).OrderByDescending(item => item.Error).ToArray();
    var traceError = traceDiffs.Length == 0 ? 0f : traceDiffs.Max(item => item.Error);
    var worstTrace = traceDiffs.Length > 0 ? traceDiffs[0] : (Name: "none", Error: 0f);
    Console.WriteLine($"cuda_encoder_head device={device.DeviceIndex} marker_logits=[{string.Join(',', gpuLogits.Select(x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}] seconds={gpuWatch.Elapsed.TotalSeconds:F3} max_abs_error={maxError:R} encoder_trace_ops={cpuTrace.Count} trace_max_abs_error={traceError:R} worst_trace={worstTrace.Name}");
}
else Console.WriteLine("cuda_encoder_head skipped: driver unavailable");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { _ = model.Encoder.Encode(tokenIds, cancelled.Token); throw new InvalidOperationException("cancel was not observed"); }
catch (OperationCanceledException) { Console.WriteLine("cancel smoke passed"); }
