using System.Text.Json;
using Sezika;

namespace Sezika.IndependentModelTool;

public sealed record EvaluationTrainingReport(WorkflowTrainingReport Training, string TrainingBackend);

/// <summary>Formal schema2/schema3 evaluation binding. Evaluation itself remains an explicit scalar encoder path.</summary>
public static class EvaluationTrainingReportAdapter
{
    public const string EvaluationBackend = "scalar-modernbert-fp32-linear-marker-head-fp64";
    public static EvaluationTrainingReport ParseBounded(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        BoundedTrainingJson.Check(bytes, 1_048_576, 16, 4096, cancellationToken, message => new DecisionException("training_report_invalid", message));
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        if (!document.RootElement.TryGetProperty("schema_version", out var schema) || !schema.TryGetInt32(out var version))
            throw new DecisionException("training_report_invalid", "Versioned training report schema is required.");
        if (version == 3)
        {
            var gpu = GpuTrainingReport.ParseBounded(bytes, cancellationToken);
            _ = gpu.BindApprovedIdentity(gpu.Training.Identity.ToIdentity(), cancellationToken);
            return new(gpu.Training, gpu.Execution.TrainingBackend);
        }
        if (version != 2) throw new DecisionException("training_report_invalid", "Evaluation adapter requires schema 2 or 3.");
        var cpu = WorkflowTrainingReport.ParseBounded(bytes, cancellationToken);
        if (!cpu.UsesRealIndependentEncoder) throw new DecisionException("training_resume_backend_mismatch", "Fixture training cannot evaluate original model data.");
        return new(cpu, "scalar-independent-linear-marker-head-sgd-fp64");
    }
}
