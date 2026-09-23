using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika;

public sealed record TokenizerSpec
{
    public required int VocabularySize { get; init; }
    public required int UnknownTokenId { get; init; }
    public int BeginningOfSequenceTokenId { get; init; } = 1;
    public int EndOfSequenceTokenId { get; init; } = 2;
    public Dictionary<string, int> Vocabulary { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Deterministic, allocation-bounded tokenizer for the portable Sezika model format.
/// The model package owns the vocabulary; no language guessing is performed.
/// </summary>
public sealed class Tokenizer
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly int _unknownTokenId;
    private readonly int _bosTokenId;
    private readonly int _eosTokenId;

    public Tokenizer(TokenizerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.VocabularySize <= 0 || spec.UnknownTokenId < 0 || spec.UnknownTokenId >= spec.VocabularySize)
        {
            throw new DecisionException("tokenizer_schema_invalid", "Tokenizer vocabulary bounds are invalid.");
        }
        if (spec.Vocabulary.Count == 0)
        {
            throw new DecisionException("tokenizer_schema_invalid", "Tokenizer vocabulary is empty.");
        }
        _vocabulary = new Dictionary<string, int>(spec.Vocabulary, StringComparer.Ordinal);
        foreach (var pair in _vocabulary)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value < 0 || pair.Value >= spec.VocabularySize)
            {
                throw new DecisionException("tokenizer_schema_invalid", "Tokenizer contains an invalid vocabulary entry.");
            }
        }
        _unknownTokenId = spec.UnknownTokenId;
        _bosTokenId = spec.BeginningOfSequenceTokenId;
        _eosTokenId = spec.EndOfSequenceTokenId;
    }

    public int[] Encode(string text, int maxTokens, bool addSpecialTokens = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxTokens < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens));
        }
        var result = new List<int>(Math.Min(maxTokens, text.Length + 2));
        if (addSpecialTokens)
        {
            result.Add(_bosTokenId);
        }
        foreach (var token in Split(text))
        {
            if (result.Count >= maxTokens - (addSpecialTokens ? 1 : 0))
            {
                throw new DecisionException("decision_token_limit_exceeded", $"Token count exceeds {maxTokens}.");
            }
            if (_vocabulary.TryGetValue(token, out var id))
            {
                result.Add(id);
                continue;
            }

            // Byte fallback is explicit and deterministic. A vocabulary without byte
            // entries falls back to its declared unknown token rather than guessing.
            var utf8 = Encoding.UTF8.GetBytes(token);
            var emitted = false;
            var byteTokens = new List<int>(utf8.Length);
            foreach (var value in utf8)
            {
                var byteToken = $"<0x{value:X2}>";
                if (_vocabulary.TryGetValue(byteToken, out id))
                {
                    byteTokens.Add(id);
                    emitted = true;
                }
                else
                {
                    emitted = false;
                    break;
                }
            }
            if (emitted)
            {
                if (result.Count + byteTokens.Count > maxTokens - (addSpecialTokens ? 1 : 0))
                    throw new DecisionException("decision_token_limit_exceeded", $"Token count exceeds {maxTokens}.");
                result.AddRange(byteTokens);
            }
            else
            {
                result.Add(_unknownTokenId);
            }
        }
        if (addSpecialTokens)
        {
            result.Add(_eosTokenId);
        }
        return result.ToArray();
    }

    private static IEnumerable<string> Split(string text)
    {
        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsPunctuation(rune) || Rune.IsSymbol(rune))
            {
                if (builder.Length != 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }
                if (!Rune.IsWhiteSpace(rune))
                {
                    yield return rune.ToString();
                }
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }
        if (builder.Length != 0)
        {
            yield return builder.ToString();
        }
    }
}
