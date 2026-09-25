using System.Text.Json.Serialization;

namespace Sezika;

/// <summary>Controls whether input token loss is rejected or handled by compatibility truncation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PromptLengthPolicy>))]
public enum PromptLengthPolicy
{
    [JsonStringEnumMemberName("strict")]
    Strict,
    [JsonStringEnumMemberName("laya_compatible")]
    LayaCompatible,
}

/// <summary>Independent prefix and complete-sequence limits; these are not interchangeable.</summary>
public sealed record PromptSequenceOptions
{
    public int PrefixTokenBudget { get; init; } = 256;
    public int TotalTokenBudget { get; init; } = 1024;
    public PromptLengthPolicy LengthPolicy { get; init; } = PromptLengthPolicy.Strict;
    public int MaxInputCharacters { get; init; } = 262_144;
    public int MaxEncodedTokens { get; init; } = 1_048_576;
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>The complete, ordered input contract shared by inference and offline reference capture.</summary>
public sealed record PromptSequence
{
    public required int[] TokenIds { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required string[] CandidateLabels { get; init; }
    public required int TypeId { get; init; }
    public required PromptSequenceDiagnostics Diagnostics { get; init; }
}

/// <summary>Observed rendering and retained token ranges, never a correctness or calibration claim.</summary>
public sealed record PromptSequenceDiagnostics
{
    public required PromptLengthPolicy LengthPolicy { get; init; }
    public required string SerializedState { get; init; }
    public required string SanitizedState { get; init; }
    public required string RenderedInstruction { get; init; }
    public required string[] RenderedOptions { get; init; }
    public required int OriginalInstructionTokens { get; init; }
    public required int RetainedInstructionTokens { get; init; }
    public required int[] OriginalOptionTokens { get; init; }
    public required int[] RetainedOptionTokens { get; init; }
    /// <summary>Includes BOS, both prefix EOS tokens, and markers; excludes the final state EOS.</summary>
    public required int PrefixTokensIncludingSpecials { get; init; }
    /// <summary>Length after prefix clipping but before state clipping.</summary>
    public required int UntruncatedTotalTokens { get; init; }
    /// <summary>Length before any instruction, candidate, or state clipping.</summary>
    public required int OriginalTotalTokens { get; init; }
    public required int OriginalStateTokens { get; init; }
    public required int RetainedStateTokens { get; init; }
    public required int DroppedStateTokens { get; init; }
    public required int StateRetainedStart { get; init; }
    /// <summary>left for conversation arrays, right for all other state values.</summary>
    public required string StateTruncationDirection { get; init; }
    public required bool MaskTextRemoved { get; init; }
    public required bool InstructionTruncated { get; init; }
    public required bool OptionsTruncated { get; init; }
    public required bool StateTruncated { get; init; }
    public bool FinalSequenceTruncated { get; init; }
    public bool WasTruncated => InstructionTruncated || OptionsTruncated || StateTruncated || FinalSequenceTruncated;
}

/// <summary>A strict-mode rejection carrying the exact proposed token loss.</summary>
public sealed class PromptTruncationException : DecisionException
{
    public PromptTruncationException(string code, string message, PromptSequenceDiagnostics diagnostics)
        : base(code, message)
    {
        Diagnostics = diagnostics;
    }

    public PromptSequenceDiagnostics Diagnostics { get; }
}

/// <summary>Small inference-response projection; prompt text remains in offline capture diagnostics.</summary>
public sealed record PromptInputDiagnostics
{
    public required PromptLengthPolicy LengthPolicy { get; init; }
    public required bool WasTruncated { get; init; }
    public required bool InstructionTruncated { get; init; }
    public required bool OptionsTruncated { get; init; }
    public required bool StateTruncated { get; init; }
    public bool FinalSequenceTruncated { get; init; }
    public required int OriginalTotalTokens { get; init; }
    public required int RetainedTotalTokens { get; init; }
    public required int OriginalStateTokens { get; init; }
    public required int RetainedStateTokens { get; init; }
    public required int DroppedStateTokens { get; init; }
    public required int StateRetainedStart { get; init; }
    public required string StateTruncationDirection { get; init; }
    public required bool MaskTextRemoved { get; init; }

    internal static PromptInputDiagnostics From(PromptSequence sequence)
    {
        var value = sequence.Diagnostics;
        return new PromptInputDiagnostics
        {
            LengthPolicy = value.LengthPolicy, WasTruncated = value.WasTruncated,
            InstructionTruncated = value.InstructionTruncated, OptionsTruncated = value.OptionsTruncated,
            StateTruncated = value.StateTruncated, OriginalTotalTokens = value.OriginalTotalTokens,
            FinalSequenceTruncated = value.FinalSequenceTruncated,
            RetainedTotalTokens = sequence.TokenIds.Length, OriginalStateTokens = value.OriginalStateTokens,
            RetainedStateTokens = value.RetainedStateTokens, DroppedStateTokens = value.DroppedStateTokens,
            StateRetainedStart = value.StateRetainedStart, StateTruncationDirection = value.StateTruncationDirection,
            MaskTextRemoved = value.MaskTextRemoved,
        };
    }
}
