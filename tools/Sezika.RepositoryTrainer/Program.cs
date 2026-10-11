using System.Diagnostics;
using System.Text.Json;
using Sezika;
using Sezika.RepositoryTrainer;

if(args.Length==0||args[0]=="help")
{
    Console.WriteLine("RepositoryTrainer prepare --dataset <checkout> --output <fresh-derived> --dataset-commit <40hex> [--max-records 100000] [--pilot true]");
    Console.WriteLine("RepositoryTrainer lengths --data <derived> --tokenizer <json> --output <fresh-json> [--max-tokens 8192]");
    Console.WriteLine("RepositoryTrainer admit-lengths --data <derived> --lengths <report> --output <fresh-derived>");
    Console.WriteLine("RepositoryTrainer attach-notices --dataset <checkout> --data <existing-derived> (adds source notices; preserves data bytes)");
    Console.WriteLine("RepositoryTrainer profile|train --plan <pinned-json> --output <fresh-directory> [--profile <real-base-profile>] [--resume <complete-checkpoint>]");
    Console.WriteLine("RepositoryTrainer validate --plan <same-json> --package <saved-package> --package-sha <64hex> --output <fresh-json>");
    Console.WriteLine("RepositoryTrainer predict-package --package <new-run-directory> --package-sha <64hex> --cases <json> --cases-sha <64hex> --gpu-uuid <32hex> --output <fresh-json> [--max-cases 512] [--max-tokens 1024] [--owned-mib 4096]");
    Console.WriteLine("RepositoryTrainer evaluate-baseline --base-model <directory> --head <asset> --head-sha <64hex> --baseline-report <json> --baseline-report-sha <64hex> --cases <json> --cases-sha <64hex> --gpu-uuid <32hex> --output <fresh-json>");
    Console.WriteLine("RepositoryTrainer storage-check --output <fresh-directory> (tiny synthetic GPU save/reload/resume)");return 0;
}
var options=new Dictionary<string,string>(StringComparer.Ordinal);
if(args.Length>35||args.Length%2==0)throw new ArgumentException("At most17 named pairs.");
for(var i=1;i<args.Length;i+=2)if(!args[i].StartsWith("--",StringComparison.Ordinal)||!options.TryAdd(args[i],args[i+1]))throw new ArgumentException("Duplicate or invalid option.");
string Need(string name)=>options.TryGetValue(name,out var value)?value:throw new ArgumentException("Missing "+name);
int Number(string name,int fallback,int min,int max){var value=options.TryGetValue(name,out var text)?int.Parse(text):fallback;if(value<min||value>max)throw new ArgumentOutOfRangeException(name);return value;}
using var budget=new RunBudget(Number("--timeout-seconds",900,1,86400),options.GetValueOrDefault("--cancel-file"));
try
{
    if(args[0]=="evaluate-baseline")
    {
        Acceptance.Baseline(Need("--base-model"),Need("--head"),Need("--baseline-report"),Need("--head-sha"),Need("--baseline-report-sha"),
            Need("--cases"),Need("--cases-sha"),Need("--gpu-uuid"),Number("--max-cases",512,1,4096),Number("--max-tokens",1024,2,8192),
            (ulong)Number("--owned-mib",4096,1536,6144)*1024*1024,Need("--output"),budget);return 0;
    }
    if(args[0]=="predict-package")
    {
        Acceptance.Run(Need("--package"),Need("--package-sha"),Need("--cases"),Need("--cases-sha"),Need("--gpu-uuid"),
            Number("--max-cases",512,1,4096),Number("--max-tokens",1024,2,8192),(ulong)Number("--owned-mib",4096,1536,6144)*1024*1024,Need("--output"),budget);return 0;
    }
    if(args[0]=="storage-check"){StorageChecks.Run(Need("--output"),budget);return 0;}
    if(args[0]=="attach-notices"){Data.AttachNotices(Need("--dataset"),Need("--data"),budget);return 0;}
    if(args[0] is "profile" or "train"){Workflow.Run(args[0],Need("--plan"),Need("--output"),options.GetValueOrDefault("--profile"),options.GetValueOrDefault("--resume"),budget);return 0;}
    if(args[0]=="validate"){Workflow.Validate(Need("--package"),Need("--package-sha"),Need("--plan"),Need("--output"),budget);return 0;}
    if(args[0]=="admit-lengths"){var manifest=Data.AdmitLengths(Need("--data"),Need("--lengths"),Need("--output"),budget);Console.WriteLine(JsonSerializer.Serialize(manifest,TrainerJson.Default.DataManifest));return 0;}
    if(args[0]=="prepare")
    {
        var manifest=Data.Prepare(Need("--dataset"),Need("--output"),Need("--dataset-commit"),Number("--max-records",100000,1,1000000),options.GetValueOrDefault("--pilot")=="true",budget);
        Console.WriteLine(JsonSerializer.Serialize(manifest,TrainerJson.Default.DataManifest));return 0;
    }
    if(args[0]=="lengths")
    {
        var input=Data.Open(Need("--data"),budget);var tokenizer=new TokenizerJson(Need("--tokenizer"),budget.Token);var max=Number("--max-tokens",8192,2,8192);var rows=new List<LengthRow>();
        foreach(var row in input.Rows)
        {
            budget.Check();try{var request=Data.Request(row,out var question);var sequence=IndependentDecisionV1SequenceBuilder.Build(tokenizer,request,question,new IndependentDecisionSequenceOptions{MaxTokens=max},budget.Token);rows.Add(new(row.RecordId,sequence.TokenIds.Length,true,null));}
            catch(DecisionException error){rows.Add(new(row.RecordId,null,false,error.Code));}
            if(rows.Count%1000==0)Console.WriteLine($"strict lengths {rows.Count}/{input.Rows.Length}");
        }
        var rejected=rows.Count(r=>!r.Accepted);var report=new LengthReport(rejected==0?"PASS_strict_lengths_only":"FAIL_preserve_overlength_records",max,rows.Count,rows.Count-rejected,rejected,input.ManifestSha256,rows.ToArray());
        Data.WriteNew(Need("--output"),report,TrainerJson.Default.LengthReport);Console.WriteLine($"lengths accepted={report.Accepted}/{report.Total};training=NOT_RUN");return rejected==0?0:3;
    }
    throw new ArgumentException("Unknown command.");
}
catch(OperationCanceledException){Console.Error.WriteLine("Cancelled or bounded deadline exceeded.");return 124;}
catch(Exception error){Console.Error.WriteLine(error.ToString());return 1;}

namespace Sezika.RepositoryTrainer
{
    internal sealed class RunBudget:IDisposable
    {
        private readonly CancellationTokenSource _source;private readonly Stopwatch _watch=Stopwatch.StartNew();private readonly int _seconds;private readonly string? _cancel;
        private readonly ConsoleCancelEventHandler _handler;
        internal CancellationToken Token=>_source.Token;
        internal double RemainingSeconds=>Math.Max(0,_seconds-_watch.Elapsed.TotalSeconds);
        internal RunBudget(int seconds,string? cancel){_seconds=seconds;_cancel=cancel;_source=new(TimeSpan.FromSeconds(seconds));_handler=(_,e)=>{e.Cancel=true;_source.Cancel();};Console.CancelKeyPress+=_handler;}
        internal void Check(){Token.ThrowIfCancellationRequested();if(_watch.Elapsed.TotalSeconds>=_seconds||(_cancel is not null&&File.Exists(_cancel)))throw new OperationCanceledException("Task deadline or cancel file.");}
        public void Dispose(){Console.CancelKeyPress-=_handler;_source.Dispose();}
    }
}
