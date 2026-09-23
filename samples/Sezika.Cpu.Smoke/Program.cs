using System.Text.Json;
using Sezika;

using var state = JsonDocument.Parse("{\"message\":\"cpu aot smoke\"}");
using var instruction = JsonDocument.Parse("\"choose a category\"");
using var first = JsonDocument.Parse("\"billing\"");
using var second = JsonDocument.Parse("\"technical\"");
using var engine = new DecisionEngine(DemoModelFactory.CreateTiny());
var response = engine.Evaluate(new DecisionRequest
{
    Model = "sezika-demo-tiny",
    State = state.RootElement.Clone(),
    Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
    {
        ["intent"] = new ChoiceQuestion
        {
            Instructions = instruction.RootElement.Clone(),
            Criteria = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["billing"] = first.RootElement.Clone(),
                ["technical"] = second.RootElement.Clone(),
            },
        },
    },
});
if (response.Answers["intent"] is not ChoiceAnswer choice || choice.Probabilities.Count != 2)
    throw new InvalidOperationException("CPU Native AOT typed smoke failed.");
Console.WriteLine($"cpu_aot_smoke passed: choice={choice.Choice}, backend={response.Backend}");
