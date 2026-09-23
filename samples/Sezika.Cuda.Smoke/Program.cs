using Sezika.Cuda;
using Sezika;
using System.Text.Json;

if (!CudaDevice.IsAvailable)
{
    Console.Error.WriteLine("CUDA Driver is unavailable.");
    return 2;
}

using CudaDevice device = CudaDevice.Open();
using CudaVectorAdd vectorAdd = device.CreateVectorAdd();
using CudaGemm gemm = device.CreateGemm();

float[] left = [1f, 2f, 3f, 4f, 5f];
float[] right = [10f, 20f, 30f, 40f, 50f];
float[] vectorResult = new float[left.Length];
vectorAdd.Execute(left, right, vectorResult);
for (var i = 0; i < vectorResult.Length; i++)
{
    if (MathF.Abs(vectorResult[i] - (left[i] + right[i])) > 1e-5f)
    {
        throw new InvalidOperationException($"Vector add mismatch at {i}: {vectorResult[i]}.");
    }
}

float[] matrixLeft = [1f, 2f, 3f, 4f, 5f, 6f];
float[] matrixRight = [7f, 8f, 9f, 10f, 11f, 12f];
float[] matrixResult = new float[4];
gemm.Execute(matrixLeft, 2, 3, matrixRight, 2, matrixResult);
float[] expected = [58f, 64f, 139f, 154f];
for (var i = 0; i < matrixResult.Length; i++)
{
    if (MathF.Abs(matrixResult[i] - expected[i]) > 1e-4f)
    {
        throw new InvalidOperationException($"GEMM mismatch at {i}: {matrixResult[i]}.");
    }
}

Console.WriteLine($"CUDA smoke passed on device index {device.DeviceIndex}: vector-add and GEMM.");

using var gpuScorer = new CudaDecisionScorer(device);
using var engine = new DecisionEngine(DemoModelFactory.CreateTiny("cuda-driver-gemm-head"), scorer: gpuScorer);
using var stateDocument = JsonDocument.Parse("{\"message\":\"duplicate invoice charge\"}");
var decision = engine.Evaluate(new DecisionRequest
{
    Model = "sezika-demo-tiny",
    State = stateDocument.RootElement.Clone(),
    Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
    {
        ["intent"] = new ChoiceQuestion
        {
            Instructions = JsonDocument.Parse("\"choose a category\"").RootElement.Clone(),
            Criteria = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["billing"] = JsonDocument.Parse("\"billing or refund\"").RootElement.Clone(),
                ["technical"] = JsonDocument.Parse("\"software problem\"").RootElement.Clone(),
            },
        },
    },
});
Console.WriteLine($"CUDA decision passed: status={decision.Answers["intent"].Status}, backend={decision.Backend}, usage_tokens={decision.Usage?.TokenCount}.");
return 0;
