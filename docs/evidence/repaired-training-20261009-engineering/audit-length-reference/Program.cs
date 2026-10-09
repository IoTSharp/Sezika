using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

if(args.Length != 1 || args[0] is not ("probe" or "pair")) return 2;
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var clock=Stopwatch.StartNew();
const string task="D:\\source\\Sezika\\.artifacts\\repaired-training-20261009";
const string target="massive-zh-cn-02996";
void Check(){cancellation.Token.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>=20 || File.Exists(Path.Combine(task,"audit-cancel.request")))throw new OperationCanceledException();}
try {
    var tokenizerPath="D:\\source\\Sezika\\.artifacts\\models\\mmbert-independent\\converted\\tokenizer\\tokenizer.json";
    var tokenizer=new TokenizerJson(tokenizerPath,cancellation.Token);Check();
    var paths=new[]{"D:\\source\\Sezika\\docs\\evidence\\teacher-expansion-20261009-final\\prepared-records.jsonl","D:\\source\\Sezika\\docs\\evidence\\teacher-repair-20261009-final\\data-v3\\experiment-records.jsonl"};
    var count=args[0]=="probe"?1:2;var rows=new LengthRow[count];
    for(var variant=0;variant<count && variant<2;variant++) {
        Check();using var reader=new StreamReader(paths[variant],new UTF8Encoding(false,true));
        string? requestText=null;int rowIndex=0;
        for(var index=0;index<256;index++) {
            Check();var line=reader.ReadLine();if(line is null)break;if(Encoding.UTF8.GetByteCount(line)>65536)throw new InvalidDataException("Row64KiB");
            using var json=JsonDocument.Parse(line);if(json.RootElement.GetProperty("record_id").GetString()!=target)continue;
            requestText=json.RootElement.GetProperty("request").GetRawText();rowIndex=index+1;break;
        }
        if(requestText is null)throw new InvalidDataException("Target absent in256row bound");
        var request=IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(requestText));
        var choice=request.Questions.Single() as IndependentChoiceQuestion ?? throw new InvalidDataException("Choice expected");
        var diagnostic=IndependentDecisionV1SequenceBuilder.Build(tokenizer,request,choice,new IndependentDecisionSequenceOptions{MaxTokens=8192,Deadline=TimeSpan.FromSeconds(5)},cancellation.Token);
        var q=tokenizer.Encode(diagnostic.SerializedQuestion,8192,false,cancellation.Token).Length;
        var state=tokenizer.Encode(diagnostic.SerializedState,8192,false,cancellation.Token).Length;
        var candidateLengths=new int[choice.Candidates.Count];
        for(var i=0;i<choice.Candidates.Count && i<8;i++) {
            Check();using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream)){writer.WriteStartObject();writer.WriteString("id",choice.Candidates[i].Id);writer.WriteString("text",choice.Candidates[i].Text);writer.WriteEndObject();writer.Flush();}
            candidateLengths[i]=tokenizer.Encode(Encoding.UTF8.GetString(stream.ToArray()),8192,false,cancellation.Token).Length;
        }
        if(4+candidateLengths.Length+q+state+candidateLengths.Sum()!=diagnostic.TokenIds.Length)throw new InvalidDataException("Formal segment/token sum differs");
        string strict="passed";
        try {var result=IndependentDecisionV1SequenceBuilder.Build(tokenizer,request,choice,new IndependentDecisionSequenceOptions{MaxTokens=1024,Deadline=TimeSpan.FromSeconds(5)},cancellation.Token);if(result.TokenIds.Length!=diagnostic.TokenIds.Length)throw new InvalidDataException("Strict/diagnostic mismatch");}
        catch(DecisionException error){if(error.Code!="decision_v1_token_budget_exceeded")throw;strict=error.Code;}
        rows[variant]=new(variant==0?"parent":"repaired",rowIndex,target,choice.Instruction.Length,choice.Candidates.Count,choice.Candidates.Select(x=>x.Id).ToArray(),choice.Candidates.Select(x=>x.Text.Length).ToArray(),q,state,candidateLengths,4+candidateLengths.Length,diagnostic.TokenIds.Length,strict,diagnostic.SerializedQuestion,diagnostic.SerializedState);
        Console.Error.WriteLine($"audited1row {rows[variant].Variant} tokens={rows[variant].Tokens} strict={strict}");
    }
    Check();using var process=Process.GetCurrentProcess();if(process.PeakWorkingSet64>4L*1024*1024*1024)throw new InvalidDataException("Host4GiB");
    var report=new Report("sezika/one-row-formal-renderer-tokenizer-diagnostic/v1",DateTimeOffset.UtcNow.ToString("o"),args[0],tokenizerPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tokenizerPath))),8192,1024,rows,false,false,false,clock.Elapsed.TotalSeconds,process.PeakWorkingSet64);
    Console.WriteLine(JsonSerializer.Serialize(report,AuditJson.Default.Report));return 0;
}catch(Exception error){Console.Error.WriteLine(error);return 1;}
internal sealed record LengthRow(string Variant,int RowOneBased,string RecordId,int InstructionCharacters,int CandidateCount,string[] CandidateIds,int[] CandidateTextCharacters,int QuestionTokens,int StateTokens,int[] CandidateTokens,int ControlTokens,int Tokens,string Strict1024Result,string SerializedQuestion,string SerializedState);
internal sealed record Report(string Contract,string Utc,string Mode,string TokenizerPath,string TokenizerSha256,int DiagnosticMaxTokens,int ProductionMaxTokens,LengthRow[] Rows,bool EncoderLoaded,bool GpuOpened,bool ApiCalled,double Seconds,long ProcessPeakWorkingSetBytes);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.SnakeCaseLower,WriteIndented=true)]
[JsonSerializable(typeof(Report))]
internal partial class AuditJson:JsonSerializerContext;
