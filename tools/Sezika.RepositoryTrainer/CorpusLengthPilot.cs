using System.Diagnostics;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusLengthPilotSource(string Source,int FirstTrainShardIndex,long FirstTrainShardManifestRows,
    int RequestedPhysicalPrefix,int PhysicalRows,int OriginalExcluded,int SourceScopeExcluded,int TokenLimitExcluded,int Accepted,
    int RendererFailures,int CancelledRows,int SequenceCalls,int ChoiceRotationRows,int LongestAcceptedTokens,
    double ElapsedSeconds,double RenderingSeconds,double LongestRenderingAttemptSeconds,bool PrefixCompleted,bool ShardEofVerified,
    Dictionary<string,int> ScopeReasonCounts);
internal sealed record CorpusLengthPilotReceipt(string Status,bool ThisIsPilot,bool FullLengthAccepted,bool FullCorpusEofVerified,
    bool GpuFeaturesComputed,bool TrainingPerformanceMeasured,string CorpusManifestSha256,string SourcePolicySha256,
    int MaximumTokens,int PerSource,int MaximumSources,int MaximumPhysicalRows,int WorkDeadlineSeconds,int CloseoutDeadlineSeconds,
    int ManifestTrainSources,int SampledSources,int CompletedPrefixes,int SourcesWithScopeEligibleRows,
    int PhysicalRows,int OriginalExcluded,int SourceScopeExcluded,int TokenLimitExcluded,int Accepted,int RendererFailures,int CancelledRows,
    int SequenceCalls,int ChoiceRotationRows,int LongestAcceptedTokens,double WorkElapsedSeconds,double RenderingSeconds,
    double LongestSourceSeconds,double LongestRenderingAttemptSeconds,string? FailureCode,string TimingScope,string CoverageBoundary,
    CorpusLengthPilotSource[] Sources);

[JsonSerializable(typeof(CorpusLengthPilotReceipt))]
internal partial class CorpusLengthPilotJson:JsonSerializerContext;

internal static class CorpusLengthPilot
{
    internal static void Run(Corpus corpus,TokenizerJson tokenizer,CorpusSourcePolicy policy,int maximumTokens,int perSource,
        string output,RunBudget budget)
    {
        const int maximumSources=128,maximumRows=8192,maximumWorkSeconds=120,closeoutSeconds=10;
        ArgumentNullException.ThrowIfNull(corpus);ArgumentNullException.ThrowIfNull(tokenizer);ArgumentNullException.ThrowIfNull(policy);
        if(maximumTokens is <2 or >8192||perSource is <1 or >128)throw new ArgumentOutOfRangeException(nameof(perSource),"Pilot token/prefix bounds differ.");
        var outputPath=Path.GetFullPath(output);
        if(File.Exists(outputPath)||Directory.Exists(outputPath))throw new IOException("Fresh length-pilot receipt required.");
        var parent=Path.GetDirectoryName(outputPath)??throw new IOException("Length-pilot output parent absent.");
        Directory.CreateDirectory(parent);outputPath=Data.Within(parent,Path.GetFileName(outputPath));
        budget.Check();var available=(int)Math.Floor(Math.Min(maximumWorkSeconds+closeoutSeconds,budget.RemainingSeconds));
        if(available<=closeoutSeconds)throw new OperationCanceledException("Length-pilot closeout time unavailable.");
        var workSeconds=Math.Min(maximumWorkSeconds,available-closeoutSeconds);
        using var pilotBudget=new RunBudget(workSeconds,budget.CancelFile);
        var watch=Stopwatch.StartNew();var firstShards=new Dictionary<string,int>(StringComparer.Ordinal);
        var results=new List<CorpusLengthPilotSource>();var status="FAIL_pilot_not_completed";string? failureCode=null;
        void Check()
        {
            budget.Check();pilotBudget.Check();
            if(watch.Elapsed.TotalSeconds>=workSeconds)throw new OperationCanceledException("Length-pilot work deadline.");
        }
        static string SafeCode(string? value)=>value is not null&&value.Length is >=1 and <=128&&
            value.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_')?value:"unrecognized_diagnostic_code";

        try
        {
            if(corpus.Manifest.Shards.Length is <1 or >4096)throw new InvalidDataException("Length-pilot manifest shard bound4096.");
            for(var index=0;index<corpus.Manifest.Shards.Length;index++)
            {
                Check();var shard=corpus.Manifest.Shards[index];if(shard.Split!="train")continue;
                if(string.IsNullOrWhiteSpace(shard.Source))throw new InvalidDataException("Length-pilot source absent.");
                if(firstShards.TryAdd(shard.Source,index)&&firstShards.Count>maximumSources)
                    throw new InvalidDataException("Length-pilot source bound128.");
            }
            if(firstShards.Count==0||checked(firstShards.Count*perSource)>maximumRows)
                throw new InvalidDataException("Length-pilot worst-case physical prefix exceeds8192; reduce per-source rows.");

            // Manifest encounter order is retained. The same tokenizer/cache is
            // shared across sources; this is not a cold-cache timing comparison.
            foreach(var entry in firstShards)
            {
                Check();var shard=corpus.Manifest.Shards[entry.Value];
                if(shard.Records is <1 or >50000)throw new InvalidDataException("Length-pilot shard denominator bound50000.");
                var requested=(int)Math.Min(perSource,shard.Records);var timer=Stopwatch.StartNew();
                int physical=0,originalExcluded=0,scopeExcluded=0,tokenExcluded=0,accepted=0,rendererFailures=0,cancelledRows=0;
                int calls=0,rotatedRows=0,longest=0;double renderSeconds=0,longestRendering=0;var complete=false;
                var reasons=new Dictionary<string,int>(StringComparer.Ordinal);
                try
                {
                    // This reader hashes the selected compressed file before
                    // yielding its prefix. Hashing, gzip/JSON and policy costs
                    // belong to source elapsed time, not rendering time.
                    using var rows=corpus.RowsForShard(entry.Value,pilotBudget).GetEnumerator();
                    for(var number=0;number<requested;number++)
                    {
                        Check();if(!rows.MoveNext())throw new InvalidDataException("Length-pilot selected shard prefix truncated.");
                        var item=rows.Current;physical++;
                        if(physical>perSource||results.Sum(s=>s.PhysicalRows)+physical>maximumRows)
                            throw new InvalidDataException("Length-pilot physical row bound exceeded.");
                        try
                        {
                            Check();if(item.Source!=entry.Key||item.Row.Split!="train")throw new InvalidDataException("Length-pilot source/train identity differs.");
                            if(item.Excluded){originalExcluded++;continue;}
                            var scopeFailure=policy.Failure(item.Row);
                            if(scopeFailure is not null)
                            {
                                scopeExcluded++;var reason=SafeCode(scopeFailure);
                                reasons[reason]=reasons.GetValueOrDefault(reason)+1;continue;
                            }
                            var rendering=Stopwatch.StartNew();
                            try
                            {
                                calls++;var length=Workflow.Sequence(tokenizer,item.Row,maximumTokens,false,pilotBudget).Sequence.TokenIds.Length;
                                Check();
                                if(item.Row.Type==MarkerQuestionType.Choice)
                                {
                                    calls++;rotatedRows++;
                                    length=Math.Max(length,Workflow.Sequence(tokenizer,item.Row,maximumTokens,true,pilotBudget).Sequence.TokenIds.Length);
                                    Check();
                                }
                                accepted++;longest=Math.Max(longest,length);
                            }
                            catch(DecisionException error)when(error.Code is "decision_v1_token_budget_exceeded" or "decision_token_limit_exceeded")
                            {tokenExcluded++;}
                            catch(OperationCanceledException){throw;}
                            catch(Exception error)
                            {
                                rendererFailures++;failureCode=error is DecisionException decision?SafeCode(decision.Code):SafeCode(error.GetType().Name);
                                // Do not propagate renderer messages or nested
                                // exceptions that could contain source text.
                                throw new InvalidDataException($"Length-pilot renderer failed;source={entry.Key};code={failureCode};no row admitted.");
                            }
                            finally
                            {
                                renderSeconds+=rendering.Elapsed.TotalSeconds;
                                longestRendering=Math.Max(longestRendering,rendering.Elapsed.TotalSeconds);
                            }
                        }
                        catch(OperationCanceledException){cancelledRows++;throw;}
                        if((number+1)%16==0)Console.WriteLine($"length pilot source={entry.Key};physical={physical}/{requested};accepted={accepted};scope={scopeExcluded};tokens={tokenExcluded};elapsed={timer.Elapsed.TotalSeconds:F3}s");
                    }
                    complete=true;
                    // No additional MoveNext: even a prefix matching the
                    // manifest count does not claim verified gzip EOF here.
                }
                finally
                {
                    results.Add(new(entry.Key,entry.Value,shard.Records,requested,physical,originalExcluded,scopeExcluded,tokenExcluded,accepted,
                        rendererFailures,cancelledRows,calls,rotatedRows,longest,timer.Elapsed.TotalSeconds,renderSeconds,longestRendering,complete,false,reasons));
                    Console.WriteLine($"length pilot prefix source={entry.Key};physical={physical};accepted={accepted};original-excluded={originalExcluded};scope-excluded={scopeExcluded};token-excluded={tokenExcluded};seconds={timer.Elapsed.TotalSeconds:F3};EOF-verified=false");
                }
            }
            Check();
            if(results.Sum(s=>s.PhysicalRows)!=results.Sum(s=>s.OriginalExcluded+s.SourceScopeExcluded+s.TokenLimitExcluded+s.Accepted))
                throw new InvalidDataException("Length-pilot completed physical denominator differs.");
            status=results.Any(s=>s.TokenLimitExcluded>0)?"COMPLETE_pilot_prefix_with_token_rejections_only":"PASS_pilot_prefix_lengths_only";
        }
        catch(OperationCanceledException){failureCode??="pilot_cancelled_or_deadline";status="CANCELLED_pilot_partial_preserved";throw;}
        catch(Exception error)
        {
            failureCode??=SafeCode(error.GetType().Name);
            status=results.Any(s=>s.RendererFailures>0)?"FAIL_pilot_renderer_contract":"FAIL_pilot_input_or_io_contract";throw;
        }
        finally
        {
            // A bounded diagnostic receipt survives work cancellation. It is
            // the sole deliverable; no temporary corpus or per-row data exists.
            var closeout=Stopwatch.StartNew();
            if(results.Count>maximumSources)throw new InvalidDataException("Length-pilot receipt source bound128.");
            var receipt=new CorpusLengthPilotReceipt(status,true,false,false,false,false,corpus.ManifestSha256,policy.Sha256,
                maximumTokens,perSource,maximumSources,maximumRows,workSeconds,closeoutSeconds,firstShards.Count,results.Count,
                results.Count(s=>s.PrefixCompleted),results.Count(s=>s.Accepted+s.TokenLimitExcluded+s.RendererFailures>0),
                results.Sum(s=>s.PhysicalRows),results.Sum(s=>s.OriginalExcluded),results.Sum(s=>s.SourceScopeExcluded),results.Sum(s=>s.TokenLimitExcluded),
                results.Sum(s=>s.Accepted),results.Sum(s=>s.RendererFailures),results.Sum(s=>s.CancelledRows),results.Sum(s=>s.SequenceCalls),
                results.Sum(s=>s.ChoiceRotationRows),results.Count==0?0:results.Max(s=>s.LongestAcceptedTokens),watch.Elapsed.TotalSeconds,
                results.Sum(s=>s.RenderingSeconds),results.Count==0?0:results.Max(s=>s.ElapsedSeconds),
                results.Count==0?0:results.Max(s=>s.LongestRenderingAttemptSeconds),failureCode,
                "Per-source elapsed includes selected-shard SHA hashing, gzip/JSON, exclusions, source policy and strict rendering. RenderingSeconds isolates encoding; tokenizer/cache is shared in manifest encounter order. CPU pilot timing does not measure GPU features or training throughput.",
                "Only the first train shard physical prefix per source is sampled; original exclusions take precedence. Choice checks original and one cyclic rotation. No extra EOF read, full-corpus length acceptance, representative quality claim or full-training time estimate.",results.ToArray());
            Data.WriteNew(outputPath,receipt,CorpusLengthPilotJson.Default.CorpusLengthPilotReceipt);
            if(closeout.Elapsed.TotalSeconds>=closeoutSeconds)throw new IOException("Length-pilot diagnostic closeout exceeded10s.");
        }
    }
}
