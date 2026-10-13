using System.Globalization;
using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static class CorpusSyntheticTasks
{
    internal static readonly string[] Templates = ["date_order", "relation_chain", "evidence_conflict", "tool_routing", "budget_arithmetic", "schedule_overlap", "document_evidence"];
    private static readonly string[] Variants = ["base", "counterfactual", "negated", "missing", "reordered"];
    internal sealed record Specification(string State, string[] Choices, int Choice, string BooleanInstruction, bool? Truth, string ScoreInstruction, int? Score);
    internal static Specification Specify(string template, int seed, string language, string variant)
    {
        Require(Templates.Contains(template, StringComparer.Ordinal) && seed is >= 0 and < 40 && language is "en" or "zh" && Variants.Contains(variant, StringComparer.Ordinal), "Synthetic template bounds.");
        string B(string en, string zh) => language == "zh" ? zh : en;
        var missing = variant == "missing"; var counter = variant == "counterfactual"; var negated = variant == "negated"; var reorder = variant == "reordered";
        string rule; string[] facts; string[] choices; int choice; bool? truth; int? score; string scoreRule; string positive; string negative;
        switch (template)
        {
            case "date_order":
                var first = new DateOnly(2026, 1, 15).AddDays(seed * 3); var gap = new[] { -4, 0, 1, 2, 4, 7, 14, 21 }[seed % 8]; var second = first.AddDays(gap);
                if (counter) { (first, second) = (second, first); gap = -gap; }
                facts = ["A.date=" + first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), missing ? "B.date=not provided" : "B.date=" + second.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)];
                rule = B("Gregorian calendar, UTC, same date format YYYY-MM-DD. Use only supplied dates.", "公历，UTC，日期格式 YYYY-MM-DD。仅使用所给日期。");
                choices = [B("A occurs earlier", "A 先发生"), B("B occurs earlier", "B 先发生"), B("Same date", "同一天"), B("Insufficient evidence", "证据不足")]; choice = missing ? 3 : gap > 0 ? 0 : gap < 0 ? 1 : 2; truth = missing ? null : gap > 0; score = missing ? null : gap <= 0 ? 0 : gap <= 2 ? 1 : gap <= 6 ? 2 : gap <= 13 ? 3 : 4;
                scoreRule = B("Rate A's lead: 0 when A is not earlier; 1 for1-2 days; 2 for3-6; 3 for7-13; 4 for at least14. Abstain if a date is missing.", "给 A 领先天数评分：不更早为0；1至2天为1；3至6天为2；7至13天为3；至少14天为4。日期缺失则拒答。"); positive = B("Does A occur earlier than B?", "A 是否比 B 更早发生？"); negative = B("Is it false that A occurs earlier than B?", "A 比 B 更早发生这一判断是否为假？"); break;
            case "relation_chain":
                var hops = 1 + seed % 4; var edges = Enumerable.Range(0, hops).Select(i => "N" + i + " precedes N" + (i + 1)).ToArray();
                if (counter) { var edge = seed % hops; edges[edge] = "N" + (edge + 1) + " precedes N" + edge; }
                facts = (missing ? edges.SkipLast(1) : edges).Append("start=N0;end=N" + hops).Append("edge_list_complete=" + (!missing).ToString().ToLowerInvariant()).ToArray();
                rule = B("Precedence is transitive. Only a complete edge list can establish absence of a directed path. Score the longest established start-to-end path, capped at4; zero when a complete graph has no path.", "先后关系可传递。只有完整边列表能确定不存在路径。按 start 到 end 最长已核实路径的边数评分，最多4；完整图没有路径为0。");
                choices = [B("A path is established", "已建立路径"), B("No path in the complete graph", "完整图中不存在路径"), B("Insufficient evidence", "证据不足")]; choice = missing ? 2 : counter ? 1 : 0; truth = missing ? null : !counter; score = missing ? null : counter ? 0 : hops; scoreRule = B("Rate longest start-to-end path using the supplied 0-4 edge-count rule.", "按所给0至4边数规则为最长路径评分。"); positive = B("Does a directed path from start to end exist?", "是否存在 start 到 end 的有向路径？"); negative = B("Is there no directed path from start to end?", "是否不存在 start 到 end 的有向路径？"); break;
            case "evidence_conflict":
                var active = Enumerable.Range(0, 4).Select(i => i < seed % 5).ToArray(); if (counter) active[seed % 4] = !active[seed % 4];
                var count = active.Count(v => v); facts = active.Select((v, i) => "S" + i + ".status=" + (v ? "active" : "inactive")).Take(missing ? 2 : 4).Append("observation_list_complete=" + (!missing).ToString().ToLowerInvariant()).ToArray();
                rule = B("Sources have equal authority and describe one object at one time. All active establishes active; all inactive establishes inactive; mixed reports conflict. Incomplete reports establish neither Boolean status. The ordinal score counts listed active sources only for a complete list; it is not a probability.", "各来源权威相等，描述同一时刻同一对象。全部 active 才确定 active；全部 inactive 才确定 inactive；混合为冲突。列表不完整时布尔状态不确定。完整列表的有序分数为 active 来源数量，不是概率。");
                choices = [B("Active established", "确定为 active"), B("Inactive established", "确定为 inactive"), B("Evidence is conflicting", "证据冲突"), B("Insufficient evidence", "证据不足")]; choice = missing ? 3 : count == 4 ? 0 : count == 0 ? 1 : 2; truth = missing || count is > 0 and < 4 ? null : count == 4; score = missing ? null : count;
                scoreRule = B("Count active sources using the supplied ordinal count0-4.", "按所给0至4有序计数规则统计 active 来源。"); positive = B("Is active established?", "证据是否确定 active？"); negative = B("Is inactive established?", "证据是否确定 inactive？"); break;
            case "tool_routing":
                var intents = new[] { "lookup", "reserve", "cancel", "other" }; var tools = new[] { "lookup_record", "reserve_slot", "cancel_slot", "manual_review" }; var intent = counter ? (seed + 1) % 4 : seed % 4; var present = seed % 5;
                facts = intents.Select((v, i) => v + " maps to " + tools[i]).Append(missing ? "request.intent=not provided;fields=not provided" : "request.intent=" + intents[intent] + ";present_fields=" + string.Join(',', new[] { "record_id", "requester_id", "date", "time" }.Take(present))).Append("request_complete=" + (!missing).ToString().ToLowerInvariant()).ToArray();
                rule = B("The intent-to-tool map is exhaustive. Select the mapped tool as a recommendation; this never authorizes execution. Score the number of present required fields record_id,requester_id,date,time only when the request is complete.", "意图到工具映射完整。选择对应工具仅作建议，不是执行授权。仅请求完整时按 record_id、requester_id、date、time 四字段中实际提供数量评分。");
                choices = tools.Append(B("Insufficient evidence", "证据不足")).ToArray(); choice = missing ? 4 : intent; truth = missing ? null : intent == 1; score = missing ? null : present; scoreRule = B("Rate required-field completeness by exact count0-4.", "按必填字段实际提供数量0至4评分。"); positive = B("Does the mapping select reserve_slot?", "映射是否选择 reserve_slot？"); negative = B("Does the mapping select a tool other than reserve_slot?", "映射是否选择 reserve_slot 以外的工具？"); break;
            case "budget_arithmetic":
                var money = 30 + seed; var margin = new[] { -8, 0, 1, 4, 7, 10, 11, 20 }[seed % 8]; var cost = money - margin; if (counter) { (money, cost) = (cost, money); margin = -margin; }
                facts = ["budget=" + money, missing ? "cost=not provided" : "cost=" + cost]; rule = B("Permitted means cost<=budget. Margin=budget-cost. Margin scale:0 negative,1 zero,2 for1-5,3 for6-10,4 for>=11. No payment authorization.", "cost不超过budget时规则允许。余量=budget-cost。余量等级：负为0；零为1；1至5为2；6至10为3；至少11为4。不授权支付。");
                choices = [B("Permitted by the rule", "规则允许"), B("Not permitted by the rule", "规则不允许"), B("Insufficient evidence", "证据不足")]; choice = missing ? 2 : margin >= 0 ? 0 : 1; truth = missing ? null : margin >= 0; score = missing ? null : margin < 0 ? 0 : margin == 0 ? 1 : margin <= 5 ? 2 : margin <= 10 ? 3 : 4;
                scoreRule = B("Compute margin and apply the supplied ordinal margin scale.", "计算余量并按所给有序余量等级评分。"); positive = B("Does cost satisfy the budget rule?", "cost是否满足预算规则？"); negative = B("Does cost violate the budget rule?", "cost是否违反预算规则？"); break;
            case "schedule_overlap":
                var length = new[] { 30, 45, 60, 90, 120 }[seed % 5]; var offset = new[] { length, length - 10, length - 20, length - 40, 0 }[seed % 5]; if (counter) offset = length + 5; var overlap = Math.Max(0, length - offset);
                facts = ["A.start=600;A.end=" + (600 + length), "B.start=" + (600 + offset) + ";B.end=" + (missing ? "not provided" : (600 + offset + length).ToString(CultureInfo.InvariantCulture)), "unit=minutes after midnight;timezone=UTC;same_date=2026-10-11"];
                rule = B("Half-open intervals[start,end). Touching endpoints is zero overlap. Overlap scale:0 zero,1 for1-14 minutes,2 for15-29,3 for30-59,4 for>=60.", "左闭右开区间[start,end)。仅端点相接为零重叠。重叠等级：零为0；1至14分钟为1；15至29为2；30至59为3；至少60为4。");
                choices = [B("Intervals overlap", "时间区间重叠"), B("Intervals do not overlap", "时间区间不重叠"), B("Insufficient evidence", "证据不足")]; choice = missing ? 2 : overlap > 0 ? 0 : 1; truth = missing ? null : overlap > 0; score = missing ? null : overlap == 0 ? 0 : overlap <= 14 ? 1 : overlap <= 29 ? 2 : overlap <= 59 ? 3 : 4; scoreRule = B("Compute overlap and apply the supplied ordinal scale.", "计算重叠时间并按所给有序等级评分。"); positive = B("Do intervals overlap?", "时间区间是否重叠？"); negative = B("Are intervals non-overlapping?", "时间区间是否不重叠？"); break;
            default:
                var keys = new[] { "owner", "phase", "team", "document_date" }; var expected = new[] { "Mira", "review", "Blue", "2026-10-01" }; var actual = new[] { "Nico", "draft", "Green", "2026-10-02" };
                for (var i = 0; i < seed % 5; i++) actual[i] = expected[i]; if (counter) actual[0] = actual[0] == "Mira" ? "Nico" : "Mira"; var supported = actual.Where((v, i) => v == expected[i]).Count();
                facts = keys.Select((v, i) => v + "=" + actual[i]).Take(missing ? 3 : 4).Concat(keys.Select((v, i) => "required." + v + "=" + expected[i])).Append("document_complete=" + (!missing).ToString().ToLowerInvariant()).ToArray();
                rule = B("All facts and names are fictional. A complete document gives one value per required field. Score the number of four required propositions supported. An incomplete document establishes neither support count nor owner.", "所有事实与名称均虚构。完整文档为各必需字段给出唯一值。按支持四项命题的数量评分。文档不完整时支持总数和所有者都不确定。");
                choices = ["Mira", "Nico", B("Insufficient evidence", "证据不足")]; choice = missing ? 2 : actual[0] == "Mira" ? 0 : 1; truth = missing ? null : actual[0] == "Mira"; score = missing ? null : supported; scoreRule = B("Rate support for the four required propositions by exact count0-4.", "按文档支持四项必需命题的精确数量0至4评分。"); positive = B("Does document establish Mira as owner?", "文档是否确定所有者为Mira？"); negative = B("Does document establish an owner other than Mira?", "文档是否确定所有者不是Mira？"); break;
        }
        if (reorder) Array.Reverse(facts); if (negated) truth = truth.HasValue ? !truth.Value : null;
        var state = new CorpusSyntheticState(template + "-" + seed.ToString("000", CultureInfo.InvariantCulture), template, B("Synthetic facts only; no external knowledge or action execution.", "仅使用合成事实；不使用外部知识、不执行动作。"), rule, facts);
        return new(JsonSerializer.Serialize(state, CorpusJsonContext.Default.CorpusSyntheticState), choices, choice, negated ? negative : positive, truth, scoreRule, score);
    }
    internal static IEnumerable<(CorpusQueueRecord Question, CorpusReference Reference)> Rows(int seeds, bool pilot, int maximum, Budget budget)
    {
        Require(seeds is >= 0 and <= 40 && maximum is >= 1 and <= 20_000, "Synthetic queue bounds."); var count = 0;
        (CorpusQueueRecord, CorpusReference) Make(string state, string instruction, string kind, string[] options, int? selected, string language, string family, string template, string variant)
        {
            budget.Check(); Require(++count <= maximum, "Synthetic question bound."); var question = CorpusAdapters.Question(state, instruction, kind, options, selected.HasValue ? OneHot(options.Length, selected.Value) : Enumerable.Repeat(1d / options.Length, options.Length).ToArray(), "owned_explicit_rule_reference", language, family,
                semantics: "synthetic_explicit_facts_and_rule; ordinal_Score_not_correctness_probability"); var id = "teacher-queue-" + CorpusWriter.ContentKey(state, instruction, kind, question.Candidates);
            return (new(id, family, Convert.ToUInt32(family[..8], 16) % 10 == 0 ? "development" : "train", state, instruction, kind, question.Candidates, language,
                new("owned_synthetic_explicit_rules_v2", "csharp-synthetic-v1", template + "/" + variant, Sha(state), "owned_synthetic", 1, "project-owned", null), Sha("csharp-synthetic-v1")), new(id, selected.HasValue, selected.HasValue ? OneHot(options.Length, selected.Value) : null));
        }
        if (pilot)
        {
            for (var templateIndex = 0; templateIndex < Templates.Length; templateIndex++)
                for (var index = 0; index < 3; index++)
                { var seed = new[] { 0, 2, 4 }[index]; var variant = new[] { "base", "counterfactual", "reordered" }[index]; var language = (templateIndex + index) % 2 == 0 ? "en" : "zh"; var spec = Specify(Templates[templateIndex], seed, language, variant); yield return Make(spec.State, spec.ScoreInstruction, "Score", ["0", "1", "2", "3", "4"], spec.Score, language, Sha("owned-synthetic-v2:" + Templates[templateIndex] + ":" + seed), Templates[templateIndex], variant); }
            for (var index = 0; index < 3; index++) { var language = index % 2 == 0 ? "en" : "zh"; var spec = Specify(Templates[index], 1, language, "missing"); yield return Make(spec.State, spec.ScoreInstruction, "Score", ["0", "1", "2", "3", "4"], null, language, Sha("owned-synthetic-v2:" + Templates[index] + ":1"), Templates[index], "missing"); }
            yield break;
        }
        foreach (var template in Templates)
            for (var seed = 0; seed < seeds; seed++)
                foreach (var language in new[] { "en", "zh" })
                    foreach (var variant in Variants)
                    {
                        var spec = Specify(template, seed, language, variant); var family = Sha("owned-synthetic-v2:" + template + ":" + seed);
                        yield return Make(spec.State, language == "zh" ? "根据所给事实和规则，可以得出哪个结论？" : "Which conclusion follows from supplied facts and rules?", "Choice", spec.Choices, spec.Choice, language, family, template, variant);
                        yield return Make(spec.State, spec.BooleanInstruction, "Boolean", language == "zh" ? ["否", "是"] : ["False", "True"], spec.Truth.HasValue ? spec.Truth.Value ? 1 : 0 : null, language, family, template, variant);
                        yield return Make(spec.State, spec.ScoreInstruction, "Score", ["0", "1", "2", "3", "4"], spec.Score, language, family, template, variant);
                        var offset = 1 + seed % (spec.Choices.Length - 1); var reordered = spec.Choices.Skip(offset).Concat(spec.Choices.Take(offset)).ToArray();
                        yield return Make(spec.State, language == "zh" ? "根据所给事实和规则，可以得出哪个结论？" : "Which conclusion follows from supplied facts and rules?", "Choice", reordered, (spec.Choice - offset + spec.Choices.Length) % spec.Choices.Length, language, family, template, variant + "_candidate_permutation");
                    }
        var domains = new[] { "arithmetic", "inventory", "logistics", "invoice", "scheduling", "quality", "authorization", "network", "data_validation", "maintenance", "document_review", "tool_routing" };
        var fields = new[] { "quantity", "stock", "available_capacity", "matching_fields", "available_minutes", "passing_checks", "verified_conditions", "successful_probes", "valid_fields", "completed_checks", "supporting_items", "required_parameters" };
        for (var domain = 0; domain < domains.Length; domain++)
            for (var seed = 0; seed < seeds; seed++)
                foreach (var language in new[] { "en", "zh" })
                    foreach (var variant in new[] { "base", "counterfactual", "negated", "missing" })
                    {
                        var threshold = 2 + seed % 13; var observed = threshold + new[] { -2, -1, 0, 1, 2 }[seed % 5]; if (variant == "counterfactual") observed = threshold - (observed - threshold) - 1;
                        var missing = variant == "missing"; bool? truth = missing ? null : observed >= threshold; var family = Sha("owned-synthetic-v1:" + domains[domain] + ":" + seed);
                        var state = JsonSerializer.Serialize(new CorpusSyntheticState(domains[domain] + "-" + seed, domains[domain], language == "zh" ? "仅判断规则，不执行动作。" : "Judge rules only; no action execution.", fields[domain] + ">=" + threshold, [fields[domain] + "=" + (missing ? "not provided" : observed.ToString(CultureInfo.InvariantCulture))]), CorpusJsonContext.Default.CorpusSyntheticState);
                        yield return Make(state, language == "zh" ? "根据所给观察，可以得出哪个结论？" : "Which conclusion follows from supplied observations?", "Choice", language == "zh" ? ["满足规则", "不满足规则", "证据不足"] : ["Rule satisfied", "Rule not satisfied", "Insufficient evidence"], missing ? 2 : truth!.Value ? 0 : 1, language, family, "threshold_rule", variant);
                        if (variant == "negated") truth = truth.HasValue ? !truth.Value : null;
                        yield return Make(state, language == "zh" ? variant == "negated" ? "是否不满足规则？" : "是否满足规则？" : variant == "negated" ? "Is the rule not satisfied?" : "Is the rule satisfied?", "Boolean", language == "zh" ? ["否", "是"] : ["False", "True"], truth.HasValue ? truth.Value ? 1 : 0 : null, language, family, "threshold_rule", variant);
                    }
    }
}
