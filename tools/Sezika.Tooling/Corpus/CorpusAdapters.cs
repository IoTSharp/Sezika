using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static class CorpusAdapters
{
    private static readonly string[] Emotions = ["admiration", "amusement", "anger", "annoyance", "approval", "caring", "confusion", "curiosity", "desire", "disappointment", "disapproval", "disgust", "embarrassment", "excitement", "fear", "gratitude", "grief", "joy", "love", "nervousness", "optimism", "pride", "realization", "relief", "remorse", "sadness", "surprise", "neutral"];
    private static readonly string[] Attributes = ["helpfulness", "correctness", "coherence", "complexity", "verbosity"];
    private static readonly string[][] Ratings = [
        ["Not helpful", "Slightly helpful", "Moderately helpful", "Helpful", "Highly helpful"],
        ["Completely incorrect", "Mostly incorrect", "Partly correct", "Mostly correct", "Completely correct"],
        ["Incoherent", "Mostly incoherent", "Partly coherent", "Mostly coherent", "Highly coherent"],
        ["Basic", "Simple", "Intermediate", "Advanced", "Expert"], ["Very brief", "Brief", "Moderate", "Verbose", "Very verbose"]];
    internal static CorpusQuestion Question(string state, string instruction, string kind, string[] options, double[] target,
        string label = "source_annotation", string language = "en", string? family = null, string? suffix = null, string? semantics = null)
    {
        Require(kind is "Choice" or "Boolean" or "Score" && options.Length >= 2 && options.Length <= (kind == "Score" ? 10 : 32), "Candidate count or type.");
        Require(target.Length == options.Length && (kind != "Boolean" || options.Length == 2), "Target dimension.");
        Require(options.All(o => !string.IsNullOrWhiteSpace(o) && o.Length <= 65_536), "Candidate text bounds.");
        return new(state, instruction, kind, options.Select((text, i) => new CorpusCandidate(kind == "Boolean" ? i == 0 ? "false" : "true" : i.ToString(CultureInfo.InvariantCulture), text)).ToArray(),
            Probability(target), language, label, semantics ?? label, family, suffix);
    }
    private static CorpusQuestion Sampled(string state, string label, string[] names, string instruction, string family)
    {
        Require(names.Length is >= 2 and <= 4096 && names.Contains(label, StringComparer.Ordinal), "Class label missing.");
        var options = names.Where(v => v != label).OrderBy(v => Sha(family + "|negative|" + v), StringComparer.Ordinal).Take(31).Append(label)
            .OrderBy(v => Sha(family + "|position|" + v), StringComparer.Ordinal).ToArray();
        return Question(state, instruction, "Choice", options.Select(v => v.Replace('_', ' ')).ToArray(), OneHot(options.Length, Array.IndexOf(options, label)), family: family,
            semantics: "original_class_label; training_only_gold_plus_up_to_31_negative_candidates");
    }
    internal static CorpusQuestion[] Adapt(string source, JsonElement row, string[]? names = null, string[]? scenarios = null)
    {
        switch (source)
        {
            case "google/boolq":
                var answer = row.GetProperty("answer");
                var boolean = answer.ValueKind == JsonValueKind.String ? bool.Parse(answer.GetString()!) : answer.GetBoolean();
                return [Question(Str(row, "passage"), Str(row, "question"), "Boolean", ["False", "True"], OneHot(2, boolean ? 1 : 0), family: Sha(Str(row, "passage")))];
            case "stanfordnlp/snli":
                return [Question("Premise: " + Str(row, "premise") + "\nHypothesis: " + Str(row, "hypothesis"), "What relation does the hypothesis have to the premise?", "Choice",
                    ["Entailed", "Neutral", "Contradicted"], OneHot(3, (int)Number(row.GetProperty("label"))), family: Sha(Str(row, "premise")))];
            case "PolyAI/banking77":
                return [Sampled(Str(row, "text"), Str(row, "category"), names ?? throw new InvalidDataException("Banking names required."), "Which banking intent matches this utterance?", Sha(Str(row, "text")))];
            case "clinc/clinc_oos":
                return [Sampled(Text(row[0]), Text(row[1]), names ?? throw new InvalidDataException("CLINC names required."), "Which intent matches this utterance? Use out of scope if no supported intent applies.", Sha(Text(row[0])))];
            case "google/civil_comments":
                return new[] { "toxicity", "severe_toxicity", "obscene", "threat", "insult", "identity_attack", "sexual_explicit" }.Select(attr =>
                {
                    var value = Number(row.GetProperty(attr)); Require(value is >= 0 and <= 1, "Annotation fraction range.");
                    return Question(Str(row, "text"), "Does this comment exhibit " + attr.Replace('_', ' ') + "?", "Boolean", ["False", "True"], [1 - value, value], family: Sha(Str(row, "text")), suffix: attr,
                        semantics: "reported_human_annotation_fraction_not_calibrated_correctness");
                }).ToArray();
            case "google-research-datasets/go_emotions":
                var labels = Parsed(row.GetProperty("labels")); Require(labels.GetArrayLength() <= 28, "Emotion label count.");
                var indices = labels.EnumerateArray().Select(v => (int)Number(v)).ToHashSet(); Require(indices.All(i => i is >= 0 and < 28), "Emotion label range.");
                return Emotions.Select((name, index) => Question(Str(row, "text"), "Does this text express " + name + "?", "Boolean", ["False", "True"], OneHot(2, indices.Contains(index) ? 1 : 0),
                    family: Str(row, "id"), suffix: name, semantics: "multi_label_annotation_one_boolean_per_emotion")).ToArray();
            case "nvidia/HelpSteer": case "nvidia/HelpSteer2":
                return Attributes.Select((attr, index) =>
                {
                    var rating = Number(row.GetProperty(attr)); Require(rating is >= 0 and <= 4 && rating == Math.Truncate(rating), "Integral ordinal scale 0..4 required.");
                    var instruction = attr switch
                    {
                        "complexity" => "Rate complexity from 0 (basic language) to 4 (expert language), not answer quality.",
                        "verbosity" => "Rate verbosity from 0 (very brief) to 4 (very verbose), not answer quality.",
                        "helpfulness" => "Rate helpfulness from 0 (not helpful) to 4 (highly helpful), using the original HelpSteer rubric.",
                        "correctness" => "Rate correctness from 0 (completely incorrect) to 4 (completely correct), using the original HelpSteer rubric.",
                        _ => "Rate coherence from 0 (incoherent) to 4 (highly coherent), using the original HelpSteer rubric."
                    };
                    return Question("Prompt:\n" + Str(row, "prompt") + "\nResponse:\n" + Str(row, "response"), instruction, "Score", Ratings[index].Select((v, i) => i + ": " + v).ToArray(),
                        OneHot(5, (int)rating), "human_ordinal_annotation", family: Sha(Str(row, "prompt")), suffix: attr, semantics: "source_" + attr + "_ordinal_0_to_4_not_correctness_probability");
                }).ToArray();
            case "Anthropic/hh-rlhf":
                var chosen = Str(row, "chosen"); var rejected = Str(row, "rejected"); const string separator = "\n\nAssistant:";
                var left = chosen.LastIndexOf(separator, StringComparison.Ordinal); var right = rejected.LastIndexOf(separator, StringComparison.Ordinal);
                Require(left >= 0 && right >= 0 && chosen[..left] == rejected[..right], "Preference context differs.");
                var context = chosen[..left]; var texts = new[] { chosen[(left + separator.Length)..].Trim(), rejected[(right + separator.Length)..].Trim() };
                var selected = 0; if (Convert.ToInt32(Sha(context)[..2], 16) % 2 != 0) { Array.Reverse(texts); selected = 1; }
                return [Question(context, "Which response better satisfies the conversation under the original human preference annotation?", "Choice", texts, OneHot(2, selected), "human_preference", family: Sha(context), semantics: "pairwise_preference_not_objective_correctness")];
            case "dwidlee/systemone-lite-phase2":
                var meta = Parsed(row.GetProperty("meta")); var keys = Strings(row.GetProperty("criteria_keys")); var alias = Str(row, "label_alias");
                Require((Optional(meta, "schema") == "choice" || Optional(meta, "schema") is null && Optional(meta, "task_schema") == "staged_v1") && keys.Contains(alias, StringComparer.Ordinal), "phase2_non_choice_or_label");
                return [Question(Str(row, "state"), Str(row, "instructions"), "Choice", Strings(row.GetProperty("criteria_values")), OneHot(keys.Length, Array.IndexOf(keys, alias)),
                    Optional(meta, "label_source") == "stockfish" ? "source_engine_reference" : "synthetic_reference", family: Sha(Str(row, "state")), semantics: "upstream_reference_choice; stockfish_reference_when_declared; not_teacher_accuracy")];
            case "ZefanCai/Open-Jev":
                var metadata = Parsed(row.GetProperty("metadata_json")); var state = Parsed(row.GetProperty("state_json"));
                return [Question(Text(state), Str(row, "question"), Kind(Str(row, "kind")), Strings(row.GetProperty("options")), Probabilities(row.GetProperty("target")), "synthetic_reference",
                    Optional(metadata, "language") ?? Optional(metadata, "locale") ?? "und", Optional(row, "group_id") ?? Optional(metadata, "document_family_id") ?? Sha(Text(state)), semantics: "source_synthetic_target_preserved_not_real_accuracy")];
            case "samatv256/jev-decisions-v1":
                Require(Str(row, "question_type") == "choice", "clean50k_choice_only");
                var optionArray = row.GetProperty("answer_options"); Require(optionArray.GetArrayLength() <= 32, "Answer options count.");
                var options = optionArray.EnumerateArray().Select(option =>
                {
                    using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory))
                    { writer.WriteStartObject(); foreach (var key in new[] { "type", "label", "description", "schema_json" }) { writer.WritePropertyName(key); if (option.TryGetProperty(key, out var v)) v.WriteTo(writer); else writer.WriteNullValue(); } writer.WriteEndObject(); }
                    using var json = JsonDocument.Parse(memory.ToArray()); return Canonical(json.RootElement);
                }).ToArray();
                var objectState = row.GetProperty("state"); var objectFamily = objectState.ValueKind == JsonValueKind.Object ? Optional(objectState, "trajectory_id") ?? Optional(objectState, "task_id") : null;
                return [Question(Text(objectState), Str(row, "question"), "Choice", options, OneHot(options.Length, (int)Number(row.GetProperty("target_index"))), "source_action_annotation", family: objectFamily ?? Sha(Text(objectState)), semantics: "public_clean50k_reference_choice_not_calibrated_probability")];
            case "LocalLLaMA/typed-decisions": return Typed(row);
            case "AmazonScience/massive":
                Require(names is not null && scenarios is not null, "MASSIVE class inventories required.");
                var family = "massive-original-id:" + Str(row, "id"); var language = Str(row, "locale");
                var first = Question(Str(row, "utt"), "Which scenario matches this utterance?", "Choice", scenarios!, OneHot(scenarios!.Length, Array.IndexOf(scenarios, Str(row, "scenario"))), language: language, family: family);
                var second = Sampled(Str(row, "utt"), Str(row, "intent"), names!, "Which intent matches this utterance?", family) with { Language = language };
                return [first with { SourceMember = Optional(row, "_member") }, second with { SourceMember = Optional(row, "_member") }];
            case "allenai/quartz": case "allenai/qasc":
                var choices = row.GetProperty("choices"); var answerKeys = Strings(choices.GetProperty("label")); var choiceTexts = Strings(choices.GetProperty("text"));
                Require(answerKeys.Length == choiceTexts.Length, "Science choice dimension.");
                return [Question(source == "allenai/quartz" ? Str(row, "para") : "Fact 1: " + Str(row, "fact1") + "\nFact 2: " + Str(row, "fact2"), Str(row, "question"), "Choice", choiceTexts,
                    OneHot(answerKeys.Length, Array.IndexOf(answerKeys, Str(row, "answerKey"))), family: Optional(row, "para_id") ?? Str(row, "id"), semantics: "original_training_answer_key;provided_science_facts;not_teacher_gold")];
            case "allenai/ai2_arc":
                var arcChoices = row.GetProperty("choices"); var arcLabels = Strings(arcChoices.GetProperty("label")); var arcText = Strings(arcChoices.GetProperty("text")); var arcAnswer = Str(row, "answerKey");
                string Normalize(string value) => string.Join(' ', value.Normalize(System.Text.NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                Require(arcLabels.Length == arcText.Length && arcLabels.Distinct(StringComparer.Ordinal).Count() == arcLabels.Length && arcLabels.Contains(arcAnswer, StringComparer.Ordinal), "ARC choice identity or answer key.");
                var normalizedOptions = arcText.Select(Normalize).ToArray(); Require(normalizedOptions.All(v => v.Length > 0) && normalizedOptions.Distinct(StringComparer.Ordinal).Count() == normalizedOptions.Length, "Empty or duplicate ARC option text.");
                var order = Enumerable.Range(0, normalizedOptions.Length).OrderBy(i => normalizedOptions[i], StringComparer.Ordinal).ToArray(); var normalizedState = Normalize(Str(row, "question"));
                return [Question(normalizedState, "Choose the correct answer to the science question.", "Choice", order.Select(i => normalizedOptions[i]).ToArray(), OneHot(order.Length, Array.IndexOf(order, Array.IndexOf(arcLabels, arcAnswer))), family: Sha(normalizedState),
                    semantics: "original_ARC_training_answer_key_not_teacher_probability_or_measured_accuracy; candidates_sorted_by_NFC_collapsed_text; source_row_keeps_original_label_mapping")];
            case "allenai/openbookqa":
                var bookChoices = row.GetProperty("choices"); var bookLabels = Strings(bookChoices.GetProperty("label")); var bookOptions = Strings(bookChoices.GetProperty("text"));
                Require(bookLabels.Length == bookOptions.Length && bookLabels.Distinct(StringComparer.Ordinal).Count() == bookLabels.Length, "OpenBook choice identity.");
                var bookQuestion = Str(row, "question_stem"); var fact = Optional(row, "fact1");
                return [Question(fact is null ? bookQuestion : "Provided fact: " + fact + "\nQuestion: " + bookQuestion, "Choose the correct answer to the question.", "Choice", bookOptions, OneHot(bookLabels.Length, Array.IndexOf(bookLabels, Str(row, "answerKey"))), family: Str(row, "id"), semantics: "original_training_answer_key_not_teacher_probability")];
            case "C-MTEB/AFQMC": case "C-MTEB/LCQMC":
                var equivalence = row.TryGetProperty("label", out var labelValue) ? Number(labelValue) : Number(row.GetProperty("score")); Require(equivalence is 0 or 1, "Binary equivalence label required.");
                var sentence1 = Str(row, "sentence1"); var sentence2 = Str(row, "sentence2");
                return [Question("句子一：" + sentence1 + "\n句子二：" + sentence2, "这两句话是否表达相同的意图或语义？", "Boolean", ["否", "是"], OneHot(2, (int)equivalence), language: "zh", family: Sha(string.Join('\n', new[] { sentence1, sentence2 }.Order(StringComparer.Ordinal))), semantics: "original_binary_equivalence_annotation")];
            case "kaivoss/system-one-270m-data": return Kaivoss(row);
            case "Team-ACE/ToolACE": return Tools(row);
            default: throw new InvalidDataException("adapter_not_implemented: " + source);
        }
    }
    private static string Kind(string value) => value switch { "choice" => "Choice", "noul" or "boolean" => "Boolean", "score" => "Score", _ => throw new InvalidDataException("Unknown original question type.") };
    private static CorpusQuestion[] Typed(JsonElement row)
    {
        var questions = Parsed(row.GetProperty("questions")); var gold = Parsed(row.GetProperty("gold"));
        var fields = questions.EnumerateObject().Take(257).ToArray(); Require(fields.Length <= 256, "Per-row question count.");
        return fields.Select(field =>
        {
            var kind = Kind(Str(field.Value, "type")); var probabilities = gold.GetProperty(field.Name).GetProperty("probabilities");
            var criteria = field.Value.TryGetProperty("criteria", out var found) ? found : default;
            string[] ids; string[] texts;
            if (kind == "Boolean")
            {
                ids = ["false", "true"];
                if (criteria.ValueKind == JsonValueKind.Undefined) texts = ["False", "True"];
                else { Require(criteria.ValueKind == JsonValueKind.Object && criteria.EnumerateObject().Count() == 2, "Typed Boolean IDs."); texts = ids.Select(id => Str(criteria, id)).ToArray(); }
            }
            else if (criteria.ValueKind == JsonValueKind.Object)
            { var items = criteria.EnumerateObject().Take(33).ToArray(); Require(items.Length <= 32, "Criteria count."); ids = items.Select(v => v.Name).ToArray(); texts = items.Select(v => Text(v.Value)).ToArray(); }
            else { texts = Strings(criteria); ids = Enumerable.Range(0, texts.Length).Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray(); }
            return Question(Str(row, "state"), Str(field.Value, "instructions"), kind, texts, ids.Select(id => Number(probabilities.GetProperty(id))).ToArray(), "synthetic_distribution", family: Str(row, "id"), suffix: field.Name,
                semantics: "synthetic_distribution_preserved_not_measured_correctness");
        }).ToArray();
    }
    private static CorpusQuestion[] Kaivoss(JsonElement row)
    {
        var prompt = Str(row, "prompt"); Require(prompt.Length <= 262_144, "Prompt bounds.");
        var match = Regex.Match(prompt, @"^<state>\n(.*?)\n</state>\n\nQuestion: (.*?)\nOptions:\n(.*?)\n\nAnswer with one letter\.(?:.*)$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Require(match.Success, "unsupported_original_prompt_layout"); var letters = Strings(row.GetProperty("letters")); var lines = match.Groups[3].Value.Split('\n');
        Require(lines.Length == letters.Length, "Option boundary.");
        var options = lines.Select((line, index) => { Require(line.StartsWith(letters[index] + ". ", StringComparison.Ordinal), "Option identity."); return line[(letters[index].Length + 2)..]; }).ToArray();
        var kind = Kind(Str(row, "qtype"));
        if (kind == "Boolean") Require(options.Length == 2 && options[0].StartsWith("no - ", StringComparison.OrdinalIgnoreCase) && options[1].StartsWith("yes - ", StringComparison.OrdinalIgnoreCase), "Boolean mapping.");
        if (kind == "Score") Require(options.Select((text, i) => text.StartsWith((i + 1) + " - ", StringComparison.Ordinal)).All(v => v), "Ordinal mapping unreviewed.");
        return [Question(match.Groups[1].Value, match.Groups[2].Value, kind, options, Probabilities(row.GetProperty("target")), "teacher_distribution", family: Optional(row, "state_id"), semantics: "original_teacher_soft_targets_not_measured_correctness")];
    }
    private static CorpusQuestion[] Tools(JsonElement row)
    {
        const string marker = "Here is a list of functions in JSON format that you can invoke:";
        var system = Str(row, "system"); var at = system.IndexOf(marker, StringComparison.Ordinal); Require(at >= 0, "Tool definitions boundary.");
        var raw = Utf8.GetBytes(system[(at + marker.Length)..].TrimStart()); var reader = new Utf8JsonReader(raw); using var definitions = JsonDocument.ParseValue(ref reader);
        var tools = definitions.RootElement; Require(tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() is >= 2 and <= 32, "Tool candidates count.");
        var toolNames = tools.EnumerateArray().Select(v => Str(v, "name")).ToArray(); Require(toolNames.Distinct(StringComparer.Ordinal).Count() == toolNames.Length, "Duplicate tool name.");
        var turns = row.GetProperty("conversations"); Require(turns.GetArrayLength() <= 256, "Tool turn bound."); var context = new List<JsonElement>(); var result = new List<CorpusQuestion>();
        foreach (var turn in turns.EnumerateArray())
        {
            if (Str(turn, "from") == "assistant" && context.Count > 0 && Str(context[^1], "from") == "user")
            {
                var value = Str(turn, "value").Trim();
                if (value.StartsWith('[') && value.EndsWith(']'))
                {
                    var matches = toolNames.Select((name, index) => (name, index)).Where(item => Regex.IsMatch(value, @"(?:^\[|,\s*)" + Regex.Escape(item.name) + @"\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).ToArray();
                    if (matches.Length == 1)
                    {
                        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartArray(); foreach (var item in context) item.WriteTo(writer); writer.WriteEndArray(); }
                        using var json = JsonDocument.Parse(stream.ToArray());
                        result.Add(Question(Canonical(json.RootElement), "Select the single tool used in the next reference call. Arguments are outside this task.", "Choice", tools.EnumerateArray().Select(Canonical).ToArray(), OneHot(toolNames.Length, matches[0].index),
                            "synthetic_tool_reference", "und", Sha("[" + Canonical(turns[0]) + "]"), context.Count.ToString(CultureInfo.InvariantCulture), "single_reference_tool_selection_only;arguments_and_generation_unsupported"));
                    }
                }
            }
            context.Add(turn);
        }
        Require(result.Count > 0, "no_unambiguous_single_tool_choice_generation_not_converted"); return result.ToArray();
    }
}
