using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sezika.Tooling;

// Expected inputs/targets are hand specified from the frozen Python adapter,
// independent source-review and semantic-test contracts. No old script runs,
// model/teacher output or observed new CLI output is used as a golden value.
internal static class CorpusMigrationCoverageChecks
{
    internal const int CheckCount = 21;
    internal const int NativeCheckCount = 20;
    internal const int InternalCheckCount = 1;
    private const string Revision = "SYNTHETIC_frozen_revision";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void Run(string root, Action<string, Action> check, CancellationToken cancellation = default) => RunCore(root, check, 0, cancellation);
    internal static void RunNative(string root, Action<string, Action> check, CancellationToken cancellation = default) => RunCore(root, check, 1, cancellation);
    internal static void RunInternal(string root, Action<string, Action> check, CancellationToken cancellation = default) => RunCore(root, check, 2, cancellation);

    private static void RunCore(string root, Action<string, Action> check, int group, CancellationToken cancellation)
    {
        using var run = new Context(root, group, cancellation);
        void Case(string name, Action action, bool internalOnly = false)
        {
            if (group == 1 && internalOnly || group == 2 && !internalOnly) return;
            check((internalOnly ? "INTERNAL synthetic " : "CLI synthetic ") + name, () => { run.Gate(); action(); });
        }

        // Tiny first: two real CSV records, their independent gold identities,
        // provenance and one rejected carrier. Select maximum-checks1 first.
        Case("Banking77 class identity tiny", () =>
        {
            var rows = new[] { J("""{"text":"balance query","category":"balance"}"""), J("""{"text":"transfer money","category":"transfer"}""") };
            var input = run.Carrier("banking", "PolyAI/banking77", "source/train.csv", Utf8.GetBytes("text,category\nbalance query,balance\ntransfer money,transfer\n"), rows);
            run.Prepare(input,
                E(1, "Choice", "balance query", H("balance query"), "en", "source_annotation", "original_class_label", [("balance", 1), ("transfer", 0)]),
                E(2, "Choice", "transfer money", H("transfer money"), "en", "source_annotation", "original_class_label", [("balance", 0), ("transfer", 1)]));
            run.Quarantined(run.Carrier("banking-empty", input.Source, "source/train.csv", Utf8.GetBytes("text,category\n,balance\n,transfer\n"), []));
        });
        Case("CLINC explicit out-of-scope class", () =>
        {
            var data = """{"train":[["hello","greeting"],["pay","transfer"]],"oos_train":[["outside","oos"]]}""";
            var input = run.Carrier("clinc", "clinc/clinc_oos", "source/data_full.json", Utf8.GetBytes(data), [J("""["hello","greeting"]"""), J("""["pay","transfer"]"""), J("""["outside","oos"]""")]);
            run.Prepare(input,
                E(1, "Choice", "hello", H("hello"), "en", "source_annotation", "original_class_label", [("greeting", 1), ("transfer", 0), ("oos", 0)]),
                E(2, "Choice", "pay", H("pay"), "en", "source_annotation", "original_class_label", [("greeting", 0), ("transfer", 1), ("oos", 0)]),
                E(3, "Choice", "outside", H("outside"), "en", "source_annotation", "original_class_label", [("greeting", 0), ("transfer", 0), ("oos", 1)]));
            run.Quarantined(run.Carrier("clinc-empty", input.Source, "source/data_full.json", Utf8.GetBytes("""{"train":[["","greeting"]],"oos_train":[["","oos"]]}"""), []));
        });
        Case("CivilComments seven annotation fractions", () =>
        {
            const string row = """{"text":"comment","toxicity":0.25,"severe_toxicity":0,"obscene":0.5,"threat":0,"insult":1,"identity_attack":0,"sexual_explicit":0.75}""";
            var input = run.Lines("civil", "google/civil_comments", row);
            var attributes = new[] { "toxicity", "severe_toxicity", "obscene", "threat", "insult", "identity_attack", "sexual_explicit" };
            var fractions = new[] { .25, 0, .5, 0, 1, 0, .75 };
            run.Prepare(input, attributes.Select((attribute, i) => E(1, "Boolean", "comment", H("comment"), "en", "source_annotation", "annotation_fraction_not_calibrated_correctness",
                [("False", 1 - fractions[i]), ("True", fractions[i])], "Does this comment exhibit " + attribute.Replace('_', ' ') + "?")).ToArray());
            run.Quarantined(run.Lines("civil-range", input.Source, row.Replace("0.25", "1.25", StringComparison.Ordinal)));
        });
        Case("GoEmotions28 independent original indices", () =>
        {
            var input = run.Lines("emotions", "google-research-datasets/go_emotions", """{"id":"emotion-family","text":"emotion text","labels":[0,2]}""");
            var names = new[] { "admiration", "amusement", "anger", "annoyance", "approval", "caring", "confusion", "curiosity", "desire", "disappointment", "disapproval", "disgust", "embarrassment", "excitement", "fear", "gratitude", "grief", "joy", "love", "nervousness", "optimism", "pride", "realization", "relief", "remorse", "sadness", "surprise", "neutral" };
            run.Prepare(input, names.Select((name, i) => E(1, "Boolean", "emotion text", "emotion-family", "en", "source_annotation", "multi_label_annotation", [("False", i is 0 or 2 ? 0 : 1), ("True", i is 0 or 2 ? 1 : 0)], "Does this text express " + name + "?")).ToArray());
            run.Quarantined(run.Lines("emotions-range", input.Source, """{"id":"bad","text":"emotion text","labels":[28]}"""));
        });
        Case("Open-Jev parsed primitives without fictitious Parquet carrier", () =>
        {
            // Native Tooling intentionally has no Parquet injection. This is
            // an Adapt contract fixture, not a CLI/carrier/provenance test.
            foreach (var kind in new[] { "choice", "boolean", "score" })
            {
                run.Gate();
                var row = J("{\"metadata_json\":{\"language\":\"en\"},\"state_json\":\"\\\"open state\\\"\",\"question\":\"open question\",\"kind\":" + Q(kind) + ",\"options\":[\"left\",\"right\"],\"target\":[0.25,0.75],\"group_id\":\"open-family\"}");
                var result = CorpusAdapters.Adapt("ZefanCai/Open-Jev", row).Single();
                Assert(result.Type == (kind == "choice" ? "Choice" : kind == "score" ? "Score" : "Boolean") && result.State == "open state" && result.SourceFamily == "open-family" && result.Language == "en" && result.LabelKind == "synthetic_reference" && result.Target.SequenceEqual(new[] { .25, .75 }), "Original Open-Jev primitive/soft target/family changed.");
            }
            var dimensionRejected = false;
            try { CorpusAdapters.Adapt("ZefanCai/Open-Jev", J("""{"metadata_json":{},"state_json":"\"s\"","question":"q","kind":"choice","options":["a","b"],"target":[1,0,0]}""")); }
            catch (InvalidDataException error) when (error.Message == "Target dimension.") { dimensionRejected = true; }
            Assert(dimensionRejected, "Open-Jev dimension negative did not reach the intended target-dimension guard.");
            run.InternalEvidence("Open-Jev Adapt only; no CLI/Parquet or record provenance acceptance");
        }, true);
        Case("clean50k original action object identity", () =>
        {
            const string row = """{"question_type":"choice","state":{"trajectory_id":"traj-one","step":1},"question":"select action","answer_options":[{"type":"click","label":"wrong","description":"other","schema_json":null},{"type":"click","label":"gold","description":"target","schema_json":null}],"target_index":1}""";
            var input = run.Lines("clean50k", "samatv256/jev-decisions-v1", row);
            run.Prepare(input, E(1, "Choice", C(J("""{"trajectory_id":"traj-one","step":1}""")), "traj-one", "en", "source_action_annotation", "not_calibrated_probability",
                [(C(J("""{"type":"click","label":"wrong","description":"other","schema_json":null}""")), 0), (C(J("""{"type":"click","label":"gold","description":"target","schema_json":null}""")), 1)]));
            run.Quarantined(run.Lines("clean50k-kind", input.Source, row.Replace("\"choice\"", "\"score\"", StringComparison.Ordinal)));
        });
        Case("MASSIVE bilingual family and genuine TAR member", () =>
        {
            var raw = new[] { """{"id":"42","locale":"en-US","utt":"meeting","intent":"calendar_query","scenario":"calendar","partition":"train"}""", """{"id":"42","locale":"zh-CN","utt":"天气","intent":"weather_query","scenario":"weather","partition":"train"}""" };
            var expectedRows = raw.Select(text => J(text[..^1] + ",\"_member\":\"fixture.jsonl\"}")).ToArray();
            var input = run.Carrier("massive", "AmazonScience/massive", "source/amazon-massive-dataset-1.1.tar.gz", Tar(raw, false), expectedRows);
            var records = run.Prepare(input,
                E(1, "Choice", "meeting", "massive-original-id:42", "en-US", "source_annotation", "source_annotation", [("calendar", 1), ("weather", 0)], "Which scenario matches this utterance?", "fixture.jsonl"),
                E(1, "Choice", "meeting", "massive-original-id:42", "en-US", "source_annotation", "original_class_label", [("calendar query", 1), ("weather query", 0)], "Which intent matches this utterance?", "fixture.jsonl"),
                E(2, "Choice", "天气", "massive-original-id:42", "zh-CN", "source_annotation", "source_annotation", [("calendar", 0), ("weather", 1)], "Which scenario matches this utterance?", "fixture.jsonl"),
                E(2, "Choice", "天气", "massive-original-id:42", "zh-CN", "source_annotation", "original_class_label", [("calendar query", 0), ("weather query", 1)], "Which intent matches this utterance?", "fixture.jsonl"));
            Assert(records.Select(record => record.Family).Distinct(StringComparer.Ordinal).Count() == 1, "MASSIVE original ID was split by locale.");
            var bad = run.Carrier("massive-link", input.Source, input.RelativePath, Tar([], true), []);
            run.Fail("corpus", "prepare", bad.Request, CorpusJsonContext.Default.CorpusPrepareRequest);
            Assert(File.Exists(Path.Combine(bad.Request.Output, "manifest.json")), "Archive failure lost partial manifest.");
        });
        Case("QASC original facts and answer key", () =>
        {
            const string row = """{"id":"qasc-family","fact1":"first fact","fact2":"second fact","question":"science question","choices":{"label":["A","B"],"text":["wrong","gold"]},"answerKey":"B"}""";
            var input = run.Lines("qasc", "allenai/qasc", row);
            run.Prepare(input, E(1, "Choice", "Fact 1: first fact\nFact 2: second fact", "qasc-family", "en", "source_annotation", "provided_science_facts", [("wrong", 0), ("gold", 1)]));
            run.Quarantined(run.Lines("qasc-key", input.Source, row.Replace("\"answerKey\":\"B\"", "\"answerKey\":\"missing\"", StringComparison.Ordinal)));
        });
        Case("OpenBook original gold and duplicate-label refusal", () =>
        {
            const string row = """{"id":"book-family","question_stem":"book question","fact1":"provided fact","choices":{"label":["A","B"],"text":["wrong","gold"]},"answerKey":"B"}""";
            var input = run.Lines("openbook", "allenai/openbookqa", row);
            run.Prepare(input, E(1, "Choice", "Provided fact: provided fact\nQuestion: book question", "book-family", "en", "source_annotation", "original_training_answer_key", [("wrong", 0), ("gold", 1)]));
            run.Quarantined(run.Lines("openbook-labels", input.Source, row.Replace("[\"A\",\"B\"]", "[\"A\",\"A\"]", StringComparison.Ordinal)));
        });
        void Equivalence(string source, string name, string field)
        {
            var input = run.Lines(name, source, "{\"sentence1\":\"甲\",\"sentence2\":\"乙\",\"" + field + "\":0}", "{\"sentence1\":\"乙\",\"sentence2\":\"甲\",\"" + field + "\":1}");
            var records = run.Prepare(input,
                E(1, "Boolean", "句子一：甲\n句子二：乙", null, "zh", "source_annotation", "original_binary_equivalence_annotation", [("否", 1), ("是", 0)]),
                E(2, "Boolean", "句子一：乙\n句子二：甲", null, "zh", "source_annotation", "original_binary_equivalence_annotation", [("否", 0), ("是", 1)]));
            // The historical review's JSON-pair hash byte encoding is not the
            // new corpus's opaque family hash. Preserve symmetric identity,
            // not an unsupported claim that these two encodings are equal.
            Assert(records[0].Family == records[1].Family && records[0].Split == records[1].Split, "Symmetric original sentence pair leaked across families/splits.");
            run.Quarantined(run.Lines(name + "-label", source, "{\"sentence1\":\"甲\",\"sentence2\":\"乙\",\"" + field + "\":-1}"));
        }
        Case("AFQMC symmetric source pair", () => Equivalence("C-MTEB/AFQMC", "afqmc", "label"));
        Case("LCQMC original binary score", () => Equivalence("C-MTEB/LCQMC", "lcqmc", "score"));
        Case("kaivoss exact original Boolean and Score grammar", () =>
        {
            var boolean = Kaivoss("noul", "bool-family", "no - unavailable", "yes - available", [.25, .75]);
            var score = Kaivoss("score", "score-family", "1 - low", "2 - high", [.75, .25]);
            var input = run.Lines("kaivoss", "kaivoss/system-one-270m-data", boolean, score);
            run.Prepare(input,
                E(1, "Boolean", "fixed state", "bool-family", "en", "teacher_distribution", "not_measured_correctness", [("no - unavailable", .25), ("yes - available", .75)]),
                E(2, "Score", "fixed state", "score-family", "en", "teacher_distribution", "not_measured_correctness", [("1 - low", .75), ("2 - high", .25)]));
            run.Quarantined(run.Lines("kaivoss-grammar", input.Source, boolean.Replace("Question:", "Question?", StringComparison.Ordinal), Kaivoss("noul", "bad-order", "yes - available", "no - unavailable", [.25, .75])));
        });
        Case("ToolACE single reference tool excludes future call from state", () =>
        {
            var definitions = """[{"name":"lookup","description":"find fact"},{"name":"other","description":"other task"}]""";
            var system = "Here is a list of functions in JSON format that you can invoke:" + definitions;
            var row = "{\"system\":" + Q(system) + ",\"conversations\":[{\"from\":\"user\",\"value\":\"find a fact\"},{\"from\":\"assistant\",\"value\":\"[lookup()]\"}]}";
            var input = run.Carrier("toolace", "Team-ACE/ToolACE", "source/data.json", Utf8.GetBytes("[" + row + "]"), [J(row)]);
            var state = C(J("""[{"from":"user","value":"find a fact"}]"""));
            var records = run.Prepare(input, E(1, "Choice", state, H(state), "und", "synthetic_tool_reference", "arguments_and_generation_unsupported",
                [(C(J("""{"name":"lookup","description":"find fact"}""")), 1), (C(J("""{"name":"other","description":"other task"}""")), 0)]));
            Assert(!records[0].State.Contains("[lookup(", StringComparison.Ordinal), "Reference answer entered the input state.");
            var bad = row.Replace("[lookup()]", "[lookup(), other()]", StringComparison.Ordinal);
            run.Quarantined(run.Carrier("toolace-ambiguous", input.Source, input.RelativePath, Utf8.GetBytes("[" + bad + "]"), []));
        });
        Case("SNLI positive relation preserves invalid-label rejection", () =>
        {
            var input = run.Lines("snli", "stanfordnlp/snli", """{"premise":"a premise","hypothesis":"a hypothesis","label":2}""");
            run.Prepare(input, E(1, "Choice", "Premise: a premise\nHypothesis: a hypothesis", H("a premise"), "en", "source_annotation", "source_annotation", [("Entailed", 0), ("Neutral", 0), ("Contradicted", 1)]));
            run.Quarantined(run.Lines("snli-label", input.Source, """{"premise":"p","hypothesis":"h","label":-1}"""));
        });
        Case("HH valid paired preference is not objective correctness", () =>
        {
            var input = run.Lines("hh", "Anthropic/hh-rlhf", """{"chosen":"context\n\nAssistant: preferred","rejected":"context\n\nAssistant: other"}""");
            run.Prepare(input, E(1, "Choice", "context", H("context"), "en", "human_preference", "not_objective_correctness", [("preferred", 1), ("other", 0)]));
            run.Quarantined(run.Lines("hh-context", input.Source, """{"chosen":"p\n\nAssistant: good","rejected":"q\n\nAssistant: bad"}"""));
        });
        Case("HelpSteer alias retains five ordinal dimensions", () =>
        {
            const string row = """{"prompt":"rating prompt","response":"rating response","helpfulness":0,"correctness":1,"coherence":2,"complexity":3,"verbosity":4}""";
            var input = run.Lines("helpsteer-alias", "nvidia/HelpSteer", row);
            var dimensions = new[] { "helpfulness", "correctness", "coherence", "complexity", "verbosity" };
            var levels = new[] { new[] { "Not helpful", "Slightly helpful", "Moderately helpful", "Helpful", "Highly helpful" }, new[] { "Completely incorrect", "Mostly incorrect", "Partly correct", "Mostly correct", "Completely correct" }, new[] { "Incoherent", "Mostly incoherent", "Partly coherent", "Mostly coherent", "Highly coherent" }, new[] { "Basic", "Simple", "Intermediate", "Advanced", "Expert" }, new[] { "Very brief", "Brief", "Moderate", "Verbose", "Very verbose" } };
            var records = run.Prepare(input, dimensions.Select((dimension, index) => E(1, "Score", "Prompt:\nrating prompt\nResponse:\nrating response", H("rating prompt"), "en", "human_ordinal_annotation", "source_" + dimension + "_ordinal_0_to_4_not_correctness_probability", levels[index].Select((text, i) => (i + ": " + text, i == index ? 1d : 0d)).ToArray())).ToArray());
            Assert(records.Where(record => record.LabelSemantics.Contains("complexity", StringComparison.Ordinal) || record.LabelSemantics.Contains("verbosity", StringComparison.Ordinal)).All(record => record.Instruction.Contains("not answer quality", StringComparison.Ordinal)), "Complexity or verbosity became correctness.");
            run.Quarantined(run.Lines("helpsteer-fraction", input.Source, row.Replace("\"complexity\":3", "\"complexity\":2.5", StringComparison.Ordinal)));
        });
        Case("pilot frozen draft builds independently specified request", () =>
        {
            var source = run.Folder("pilot-source");
            Write(Path.Combine(source, "manifest.json"), """{"dataset_id":"synthetic-pilot","student_model_id":"synthetic-student","training_admission":false}""");
            Write(Path.Combine(source, "candidates.jsonl"), """{"record_id":"pilot-one","family_id":"pilot-family","split":"audit","state":"pilot state","question":{"id":"q","type":"boolean","instruction":"is available"}}""" + "\n");
            var output = Path.Combine(run.Root, "pilot-output");
            var request = new CorpusDatasetBuildRequest(source, output, "pilot", FH(Path.Combine(source, "manifest.json")), FH(Path.Combine(source, "candidates.jsonl")), 1);
            run.Command("corpus", "dataset", request, CorpusJsonContext.Default.CorpusDatasetBuildRequest);
            var expected = Utf8.GetBytes("""{"schema_version":1,"model_id":"synthetic-student","state":"pilot state","questions":[{"id":"q","type":"boolean","instruction":"is available"}]}""");
            Assert(File.ReadAllBytes(Path.Combine(output, "requests/pilot-one.json")).SequenceEqual(expected), "Pilot draft envelope or original question bytes differ from independent fixture.");
            var manifest = Read(Path.Combine(output, "manifest.json")); Assert(manifest.GetProperty("human_review").GetString() == "pending" && manifest.GetProperty("training_admission").GetString() == "pending" && !manifest.GetProperty("teacher_called").GetBoolean() && !manifest.GetProperty("quality_evidence").GetBoolean(), "Pilot became teacher/quality/training acceptance.");
            var before = FH(Path.Combine(output, "manifest.json")); run.Fail("corpus", "dataset", request, CorpusJsonContext.Default.CorpusDatasetBuildRequest); Assert(FH(Path.Combine(output, "manifest.json")) == before, "Repeated output modified the original pilot.");
        });
        Case("Inspect bounded private sample preserves failures and no admission", () =>
        {
            var input = run.Lines("inspect", "google/boolq", """{"passage":"one","question":"q","answer":false}""", """{"passage":"two","question":"q","answer":true}""");
            var output = Path.Combine(run.Root, "inspect-output"); var request = new CorpusInspectRequest(input.Directory, input.Request.AssetManifest, output, 1, 1);
            run.Command("corpus", "inspect", request, CorpusJsonContext.Default.CorpusInspectRequest);
            var report = Typed(Path.Combine(output, "report.json"), CorpusJsonContext.Default.CorpusInspectionReport);
            Assert(report.PrivateRawSamples && !report.TeacherCalled && !report.FormalTrainingAdmitted && !report.LabelQualityAssessed && report.Sources is [{ SampleRows: 1, SampleAdapterValidated: true }], "Inspection row bound or acceptance scope differs.");
            var item = report.Sources[0]; Assert(item.FileSha256 == input.Hash && item.SampleSha256 == FH(Path.Combine(output, item.SamplePath!)), "Inspection raw sample or carrier identity differs.");
            run.Fail("corpus", "inspect", request with { Output = Path.Combine(run.Root, "inspect-invalid"), RowsPerSource = 0 }, CorpusJsonContext.Default.CorpusInspectRequest);
            var malformed = run.Carrier("inspect-failure", input.Source, "source/train.jsonl", Utf8.GetBytes("{not-json}\n"), []); var failedOutput = Path.Combine(run.Root, "inspect-failed-output");
            run.Command("corpus", "inspect", new CorpusInspectRequest(malformed.Directory, malformed.Request.AssetManifest, failedOutput, 1, 1), CorpusJsonContext.Default.CorpusInspectRequest);
            var failed = Typed(Path.Combine(failedOutput, "report.json"), CorpusJsonContext.Default.CorpusInspectionReport); Assert(failed.Status.StartsWith("PARTIAL", StringComparison.Ordinal) && failed.Sources.Single().Failure is not null && !failed.Sources.Single().SampleAdapterValidated, "Inspection lost its decoder failure.");
        });
        Case("Bind independently pins four files and never grants admission", () =>
        {
            var input = run.Lines("bind", "google/boolq", """{"passage":"binding","question":"q","answer":false}""");
            run.Prepare(input, E(1, "Boolean", "binding", H("binding"), "en", "source_annotation", "source_annotation", [("False", 1), ("True", 0)]));
            var request = new CorpusBindRequest(input.Request.Output, input.Directory, Path.Combine(run.Root, "binding.json"), FH(Path.Combine(input.Request.Output, "manifest.json")));
            run.Command("corpus", "bind", request, CorpusJsonContext.Default.CorpusBindRequest); var bound = Typed(request.Output, CorpusJsonContext.Default.CorpusBinding);
            Assert(bound.ManifestSha256 == request.ManifestSha256 && bound.ExclusionsSha256 == FH(Path.Combine(request.Root, "exclusions.jsonl")) && bound.SourceRightsSha256 == FH(input.Request.RightsManifest) && bound.SourceAssetsSha256 == FH(input.Request.AssetManifest) && bound.SourceRoot == input.Directory && !bound.FormalTrainingAdmitted, "Binding identity or no-admission semantics differ.");
            run.Fail("corpus", "bind", request with { Output = Path.Combine(run.Root, "binding-bad.json"), ManifestSha256 = new string('0', 64) }, CorpusJsonContext.Default.CorpusBindRequest);
        });
        Case("native TeacherTool review layout archives exact references", () =>
        {
            var source = run.Folder("native-review-source"); Directory.CreateDirectory(Path.Combine(source, "items"));
            var requestBytes = Utf8.GetBytes("""{"schema_version":1,"model_id":"synthetic-student","state":"s","questions":[{"id":"q","type":"boolean","instruction":"q"}]}""");
            var responseBytes = Utf8.GetBytes("""{"synthetic":true,"choices":[{"message":{"content":"SYNTHETIC_NOT_TEACHER_OUTPUT"}}]}""");
            File.WriteAllBytes(Path.Combine(source, "items/00.decision.json"), requestBytes); File.WriteAllBytes(Path.Combine(source, "items/00.http-response.bin"), responseBytes);
            Write(Path.Combine(source, "manifest.json"), """{"schema_version":1,"status":"complete_pending_human_review","training_admission":false,"quality_evidence":false}""");
            var row = "{\"record_id\":\"general-en-boolean-00\",\"family_id\":\"native-review-family\",\"review_status\":\"pending\",\"training_admission\":false,\"request\":{\"path\":\"items/00.decision.json\",\"sha256\":" + Q(H(requestBytes)) + ",\"bytes\":" + requestBytes.Length + "},\"response\":{\"path\":\"items/00.http-response.bin\",\"sha256\":" + Q(H(responseBytes)) + ",\"bytes\":" + responseBytes.Length + "},\"request_sha256\":" + Q(H(requestBytes)) + ",\"response_sha256\":" + Q(H(responseBytes)) + "}";
            Write(Path.Combine(source, "records.jsonl"), row + "\n"); var output = Path.Combine(run.Root, "native-review-output"); var request = new CorpusDatasetBuildRequest(source, output, "review", FH(Path.Combine(source, "manifest.json")), FH(Path.Combine(source, "records.jsonl")), 1);
            run.Command("corpus", "dataset", request, CorpusJsonContext.Default.CorpusDatasetBuildRequest);
            Assert(File.ReadAllBytes(Path.Combine(output, "source-evidence/items/00.decision.json")).SequenceEqual(requestBytes) && File.ReadAllBytes(Path.Combine(output, "source-evidence/items/00.http-response.bin")).SequenceEqual(responseBytes), "Native TeacherTool raw evidence bytes changed.");
            var pending = ReadLine(Path.Combine(output, "review-template.jsonl")); Assert(pending.GetProperty("review_status").GetString() == "pending" && !pending.GetProperty("training_admission").GetBoolean(), "Review template approved synthetic data.");
            Write(Path.Combine(source, "bad-records.jsonl"), row.Replace(H(responseBytes), new string('0', 64), StringComparison.Ordinal) + "\n");
            run.Fail("corpus", "dataset", request with { Output = Path.Combine(run.Root, "native-review-bad"), CandidatesSha256 = FH(Path.Combine(source, "bad-records.jsonl")), RecordsFile = "bad-records.jsonl" }, CorpusJsonContext.Default.CorpusDatasetBuildRequest);
        });
        Case("old typed default Boolean and probability boundaries", () =>
        {
            const string row = """{"id":"typed-family","state":"typed state","questions":{"b":{"type":"noul","instructions":"boolean statement"},"s":{"type":"score","instructions":"ordinal statement","criteria":["low","high"]},"c":{"type":"choice","instructions":"choose item","criteria":{"b":"wrong","a":"gold"}}},"gold":{"b":{"probabilities":{"true":0.9,"false":0.1}},"s":{"probabilities":{"0":0.25,"1":0.75}},"c":{"probabilities":{"a":1,"b":0}}}}""";
            var input = run.Lines("typed-old", "LocalLLaMA/typed-decisions", row);
            run.Prepare(input,
                E(1, "Boolean", "typed state", "typed-family", "en", "synthetic_distribution", "not_measured_correctness", [("False", .1), ("True", .9)]),
                E(1, "Score", "typed state", "typed-family", "en", "synthetic_distribution", "not_measured_correctness", [("low", .25), ("high", .75)]),
                E(1, "Choice", "typed state", "typed-family", "en", "synthetic_distribution", "not_measured_correctness", [("wrong", 0), ("gold", 1)]));
            var invalid = new[] { "{\"true\":1}", "{\"false\":-0.1,\"true\":1.1}", "{\"false\":\"NaN\",\"true\":1}", "{\"false\":0.2,\"true\":0.3}" };
            run.Quarantined(run.Lines("typed-invalid", input.Source, invalid.Select((probability, index) => "{\"id\":\"bad-" + index + "\",\"state\":\"invalid\",\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":\"q\"}},\"gold\":{\"q\":{\"probabilities\":" + probability + "}}}").ToArray()));
        });
    }

    private sealed record Expected(int Row, string Type, string State, string? FamilySeed, string Language, string LabelKind,
        string Semantics, (string Text, double Mass)[] Masses, string? Instruction, string? Member);
    private static Expected E(int row, string type, string state, string? family, string language, string kind, string semantics,
        (string Text, double Mass)[] masses, string? instruction = null, string? member = null) => new(row, type, state, family, language, kind, semantics, masses, instruction, member);
    private sealed record Input(string Source, string Directory, string RelativePath, string Hash, JsonElement[] Rows, CorpusPrepareRequest Request);

    private sealed class Context : IDisposable
    {
        private readonly CancellationTokenSource cancellation;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string? cli;
        private readonly string? cliSha;
        private readonly FileStream journal;
        private int commands;
        internal string Root { get; }
        internal Context(string root, int group, CancellationToken token)
        {
            Root = Path.Combine(Path.GetFullPath(root), "corpus-migration-coverage");
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); cancellation.CancelAfter(TimeSpan.FromSeconds(900));
            FileStream? createdJournal = null;
            try
            {
                if (Path.Exists(Root)) throw new IOException("Coverage evidence exists; preserve it.");
                SafePaths.NoLinks(Root, Limit()); Directory.CreateDirectory(Root);
                if (group != 2)
                {
                    cli = Environment.GetEnvironmentVariable("SEZIKA_CORPUS_CLI") ?? throw new InvalidOperationException("Explicit already accepted Native AOT SEZIKA_CORPUS_CLI required.");
                    cliSha = Environment.GetEnvironmentVariable("SEZIKA_CORPUS_CLI_SHA256") ?? throw new InvalidOperationException("Exact accepted CLI SHA256 required.");
                    VerifyCli();
                }
                journal = createdJournal = new FileStream(Path.Combine(Root, "commands.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var file = new FileStream(Path.Combine(Root, "scope.json"), FileMode.CreateNew);
                using var writer = new Utf8JsonWriter(file); writer.WriteStartObject(); writer.WriteBoolean("SyntheticData", true); writer.WriteString("Mode", group == 2 ? "INTERNAL_ADAPT_ONLY" : group == 1 ? "PINNED_NATIVE_CLI_ONLY" : "PINNED_NATIVE_CLI_WITH_ONE_SEPARATE_INTERNAL_CASE"); writer.WriteNumber("DeclaredChecks", group == 2 ? InternalCheckCount : group == 1 ? NativeCheckCount : CheckCount); writer.WriteString("Cli", cli); writer.WriteString("CliSha256", cliSha); writer.WriteBoolean("ActualTeacherCalled", false); writer.WriteBoolean("GpuRun", false); writer.WriteBoolean("QualityAccepted", false); writer.WriteBoolean("TrainingAdmitted", false); writer.WriteEndObject();
            }
            catch { createdJournal?.Dispose(); cancellation.Dispose(); throw; }
        }
        internal void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 900) throw new TimeoutException("Coverage900s wall bound.");
            if (File.Exists(Path.Combine(Root, "CANCEL"))) throw new OperationCanceledException("Coverage CANCEL observed.");
        }
        private Budget Limit() => new(30, Path.Combine(Root, "CANCEL"), cancellation.Token);
        private void VerifyCli()
        {
            Gate(); ProcessExecutor.ValidateExecutable(cli!, Limit());
            TaskOperations.VerifyFile(cli!, new FileInfo(cli!).Length, cliSha!, Limit());
        }
        internal string Folder(string name)
        {
            Gate(); var path = SafePaths.Within(Root, name, Limit()); if (Path.Exists(path)) throw new IOException("Fixture exists."); Directory.CreateDirectory(path); return path;
        }
        internal Input Carrier(string name, string source, string relative, byte[] bytes, JsonElement[] rows)
        {
            Gate(); Assert(bytes.Length <= 1_048_576 && rows.Length <= 64, "Fixture input bound."); var directory = Folder(name + "-source");
            var path = SafePaths.Within(directory, relative, Limit()); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); var hash = H(bytes);
            var assets = Path.Combine(directory, "ASSET_MANIFEST.json"); var rights = Path.Combine(directory, "RIGHTS_MANIFEST.json");
            TaskOperations.WriteNew(assets, new[] { new CorpusAsset(source, relative, bytes.Length, hash, Revision, "train") }, CorpusJsonContext.Default.CorpusAssetArray);
            TaskOperations.WriteNew(rights, new[] { new CorpusRights(source, "eligible_under_declared_terms_with_attribution", "SYNTHETIC_fixture_only_no_license_grant") }, CorpusJsonContext.Default.CorpusRightsArray);
            return new(source, directory, relative, hash, rows, new(directory, assets, rights, Path.Combine(Root, name + "-output"), 256, 1, 64));
        }
        internal Input Lines(string name, string source, params string[] rows)
        {
            var relative = source == "google-research-datasets/go_emotions" ? "source/simplified/train.jsonl" : source == "samatv256/jev-decisions-v1" ? "source/clean50k/train.jsonl" : "source/train.jsonl";
            return Carrier(name, source, relative, Utf8.GetBytes(string.Join('\n', rows) + "\n"), rows.Select(J).ToArray());
        }
        internal void Command<T>(string first, string second, T request, JsonTypeInfo<T> type) => Execute(first, second, request, type, true);
        internal void Fail<T>(string first, string second, T request, JsonTypeInfo<T> type) => Execute(first, second, request, type, false);
        private void Execute<T>(string first, string second, T request, JsonTypeInfo<T> type, bool pass)
        {
            Gate(); Assert(cli is not null && ++commands <= 64 && clock.Elapsed.TotalSeconds < 870, "Native command count64/complete cleanup reserve."); VerifyCli();
            var directory = Folder("command-" + commands.ToString("D3", CultureInfo.InvariantCulture));
            var requestFile = Path.Combine(directory, "request.json"); TaskOperations.WriteNew(requestFile, request, type);
            var outer = Path.Combine(directory, "command-result.json");
            var execution = new ExecutionRequest { Executable = cli!, ArgumentList = [first, second, "--request", requestFile, "--output", outer, "--timeout-seconds", "15"], WorkingDirectory = Root, OutputDirectory = Path.Combine(directory, "native"), TimeoutSeconds = 20, CleanupSeconds = 5, MaximumProcesses = 8, MaximumInputBytes = 0, MaximumOutputBytes = 131072, BrokerExecutable = cli, CancelFile = Path.Combine(Root, "CANCEL") };
            TaskOperations.WriteNew(Path.Combine(directory, "execution-request.json"), execution, ExecutionJsonContext.Default.ExecutionRequest);
            var result = ProcessExecutor.ExecuteAsync(execution, Limit(), cancellation.Token).GetAwaiter().GetResult();
            var native = Path.Combine(execution.OutputDirectory, "result.json");
            using (var line = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(line)) { writer.WriteStartObject(); writer.WriteNumber("Index", commands); writer.WriteBoolean("SyntheticData", true); writer.WriteString("Command", first + " " + second); writer.WriteString("NativeReceipt", native); writer.WriteString("NativeReceiptSha256", File.Exists(native) ? FH(native) : null); writer.WriteString("OriginalStatus", result.Status); writer.WriteNumber("ExitCode", result.ExitCode ?? int.MinValue); writer.WriteString("Cleanup", result.CleanupStatus); writer.WriteEndObject(); }
                Assert(line.Length <= 8192 && journal.Length + line.Length + 1 <= 64 * 8193L, "Command ledger byte bound."); journal.Write(line.ToArray()); journal.WriteByte(10); journal.Flush(true);
            }
            Assert(result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 && result.InputComplete && result.OutputComplete && !result.OutputLimitExceeded && result.RootPid is > 0 && File.Exists(native), "Native CLI identity/EOF/input/cleanup failed; original receipt retained.");
            Assert(pass ? result.Status == "PASS" && result.ExitCode == 0 : result.Status == "FAIL" && result.ExitCode is not null and not 0, "CLI expected success/rejection differs; original failure retained.");
            VerifyCli(); Console.Error.WriteLine($"corpus coverage native={commands}/64 elapsed={clock.Elapsed.TotalSeconds:F1}s original={result.Status}");
        }
        internal CorpusRecord[] Prepare(Input input, params Expected[] expected)
        {
            Command("corpus", "prepare", input.Request, CorpusJsonContext.Default.CorpusPrepareRequest);
            var manifest = Typed(Path.Combine(input.Request.Output, "manifest.json"), CorpusJsonContext.Default.CorpusManifest);
            Assert(manifest.Status == "COMPLETE_PREPARATION_ONLY" && !manifest.FormalTrainingAdmitted && manifest.Quarantined == 0 && manifest.RecordsWritten == expected.Length, "Synthetic corpus completion/admission/record denominator differs.");
            Assert(manifest.AssetsSha256 == FH(input.Request.AssetManifest) && manifest.RightsSha256 == FH(input.Request.RightsManifest), "Independent asset/rights input hashes differ.");
            var records = Records(input.Request.Output, manifest); var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in expected)
            {
                Gate(); var found = records.Where(record => record.SourceRow == e.Row && record.Type == e.Type && record.State == e.State.Normalize(NormalizationForm.FormC) && record.LabelSemantics.Contains(e.Semantics, StringComparison.Ordinal) && (e.Instruction is null || record.Instruction == e.Instruction)).ToArray();
                Assert(found.Length == 1, "Independent expected question not uniquely found."); var row = found[0]; Assert(used.Add(row.RecordId), "One output matched multiple independent expected questions.");
                Assert(row.Source.Dataset == input.Source && row.Source.Revision == Revision && row.Source.Path == input.RelativePath && row.Source.FileSha256 == input.Hash && row.Source.Row == e.Row && row.Source.OriginalSplit == (input.Source == "Team-ACE/ToolACE" ? "unsplit" : "train") && row.Source.SourceMember == e.Member, "Original source/revision/row/member/SHA identity differs.");
                Assert(e.Row >= 1 && e.Row <= input.Rows.Length && row.SourceLineSha256 == H(C(input.Rows[e.Row - 1])), "Independent canonical decoded source-row SHA differs.");
                Assert(row.Language == e.Language && row.LabelKind == e.LabelKind && row.HumanReview == "pending" && row.TrainingAdmission.Contains("pending", StringComparison.Ordinal), "Source semantics or no-admission metadata differs.");
                Assert(row.Family.Length == 64 && row.OriginalStateId == row.Family && row.Split == (Convert.ToUInt32(row.Family[..8], 16) % 10 == 0 ? "development" : "train"), "Family identity/derived split differs from frozen writer rule.");
                if (e.FamilySeed is not null) Assert(row.Family == H(input.Source + "|" + e.FamilySeed), "Independent original source family differs.");
                Assert(row.Candidates.Length == e.Masses.Length && row.Target.Length == e.Masses.Length && row.Candidates.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == row.Candidates.Length, "Candidate identity/target dimensions differ.");
                for (var i = 0; i < row.Candidates.Length; i++) { Gate(); var mass = e.Masses.Where(value => value.Text == row.Candidates[i].Text).ToArray(); Assert(mass.Length == 1 && double.IsFinite(row.Target[i]) && Math.Abs(row.Target[i] - mass[0].Mass) <= 1e-12, "Gold candidate identity or soft mass changed after rotation."); }
                if (row.Type == "Boolean") Assert(row.Candidates[0].Id == "false" && row.Candidates[1].Id == "true", "False-first Boolean identity changed.");
            }
            Assert(used.Count == records.Length, "Unspecified extra derived records."); return records;
        }
        internal void Quarantined(Input input)
        {
            Command("corpus", "prepare", input.Request, CorpusJsonContext.Default.CorpusPrepareRequest);
            var manifest = Typed(Path.Combine(input.Request.Output, "manifest.json"), CorpusJsonContext.Default.CorpusManifest);
            Assert(manifest.RecordsWritten == 0 && manifest.Quarantined > 0 && !manifest.FormalTrainingAdmitted && File.Exists(Path.Combine(input.Request.Output, "quarantine.jsonl")), "Rejected source row became accepted data or lost its ledger.");
        }
        private CorpusRecord[] Records(string root, CorpusManifest manifest)
        {
            Assert(manifest.Shards.Length <= 64, "Shard bound64."); var records = new List<CorpusRecord>(); long bytes = 0;
            foreach (var shard in manifest.Shards)
            {
                Gate(); var path = SafePaths.Within(root, shard.Path, Limit()); Assert(new FileInfo(path).Length <= 1_048_576 && FH(path) == shard.Sha256, "Original closed shard SHA/byte bound differs.");
                using var file = File.OpenRead(path); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new StreamReader(gzip, Utf8);
                for (var index = 0; index <= 256; index++)
                {
                    Gate(); var line = BoundedLine(reader, Gate); if (line is null) break; bytes += Utf8.GetByteCount(line); Assert(index < 256 && records.Count < 256 && bytes <= 2_097_152, "Derived record/decoded byte bounds.");
                    records.Add(JsonSerializer.Deserialize(line, CorpusJsonContext.Default.CorpusRecord) ?? throw new InvalidDataException("Null record."));
                }
            }
            Assert(records.Count == manifest.RecordsWritten, "Physical record denominator differs."); return records.ToArray();
        }
        internal void InternalEvidence(string scope) => Write(Path.Combine(Root, "internal-scope.json"), "{\"SyntheticData\":true,\"NativeCliExecuted\":false,\"Scope\":" + Q(scope) + ",\"QualityAccepted\":false,\"TrainingAdmitted\":false}");
        public void Dispose() { cancellation.Cancel(); journal.Dispose(); cancellation.Dispose(); }
    }

    private static byte[] Tar(string[] rows, bool link)
    {
        Assert(rows.Length <= 8, "TAR fixture row bound8."); using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            if (link) tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "bad.jsonl") { LinkName = "../outside" });
            else { using var data = new MemoryStream(Utf8.GetBytes(string.Join('\n', rows) + "\n")); tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fixture.jsonl") { DataStream = data }); }
        }
        return memory.ToArray();
    }
    private static string Kaivoss(string kind, string family, string first, string second, double[] target)
    {
        var prompt = "<state>\nfixed state\n</state>\n\nQuestion: original question\nOptions:\nA. " + first + "\nB. " + second + "\n\nAnswer with one letter.";
        return "{\"prompt\":" + Q(prompt) + ",\"letters\":[\"A\",\"B\"],\"qtype\":" + Q(kind) + ",\"state_id\":" + Q(family) + ",\"target\":[" + string.Join(',', target.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]}";
    }
    // Independent compact JSON contract from original prepare_corpus.py:
    // sorted keys, compact separators, UTF8/unescaped Unicode. Fixture numbers
    // have explicit finite lexical forms, avoiding float-format ambiguities.
    private static string C(JsonElement element)
    {
        using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) Canonical(writer, element, 0);
        return Utf8.GetString(memory.ToArray());
    }
    private static void Canonical(Utf8JsonWriter writer, JsonElement element, int depth)
    {
        Assert(depth <= 16, "Fixture canonical depth16.");
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToArray(); Assert(properties.Length <= 64, "Fixture property bound64."); writer.WriteStartObject(); foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Canonical(writer, property.Value, depth + 1); } writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                Assert(element.GetArrayLength() <= 256, "Fixture array bound256."); writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Canonical(writer, item, depth + 1); writer.WriteEndArray(); break;
            default: element.WriteTo(writer); break;
        }
    }
    private static JsonElement J(string text) { using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false }); return document.RootElement.Clone(); }
    private static JsonElement Read(string path) { Assert(new FileInfo(path).Length <= 1_048_576, "Fixture report bound1MiB."); return J(File.ReadAllText(path, Utf8)); }
    private static T Typed<T>(string path, JsonTypeInfo<T> type) => Read(path).Deserialize(type) ?? throw new InvalidDataException("Missing typed fixture report.");
    private static JsonElement ReadLine(string path) { using var reader = new StreamReader(path, Utf8); return J(BoundedLine(reader, () => { }) ?? throw new InvalidDataException("Missing fixture row.")); }
    private static string? BoundedLine(TextReader reader, Action gate)
    {
        var text = new StringBuilder(); var clock = Stopwatch.StartNew();
        for (var index = 0; index <= 65536 && clock.Elapsed.TotalSeconds < 2; index++)
        {
            if (index % 256 == 0) gate(); var next = reader.Read(); if (next < 0) return text.Length == 0 ? null : text.ToString(); if (next == '\n') return text.ToString(); Assert(index < 65536, "Fixture line byte/char bound."); if (next != '\r') text.Append((char)next);
        }
        throw new TimeoutException("Fixture line65536chars/2s bound.");
    }
    private static void Write(string path, string text) { Assert(Utf8.GetByteCount(text) <= 1_048_576, "Fixture write bound1MiB."); using var file = new FileStream(path, FileMode.CreateNew); file.Write(Utf8.GetBytes(text)); file.Flush(true); }
    private static string Q(string value) => JsonSerializer.Serialize(value, CorpusJsonContext.Default.String);
    private static string H(string value) => H(Utf8.GetBytes(value));
    private static string H(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static string FH(string path) { using var input = File.OpenRead(path); Assert(input.Length <= 67_108_864, "Fixture/native image hash bound64MiB."); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or InvalidOperationException or JsonException) { return; } throw new InvalidOperationException("Independent negative fixture was not rejected."); }
}
