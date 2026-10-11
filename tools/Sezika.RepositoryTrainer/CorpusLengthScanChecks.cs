using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusLengthScanCheckReceipt(string Status,int Checks,int MaximumCases,int WallLimitSeconds,
    long PhysicalRows,long Accepted,long OriginalExcluded,long SourceScopeExcluded,long TokenLimitExcluded,
    bool GpuTrainingRun,bool RealCorpusReviewed,string[] PassedChecks);
[JsonSerializable(typeof(CorpusLengthScanCheckReceipt))]
internal partial class CorpusLengthScanCheckJson:JsonSerializerContext;

// Synthetic gzip/renderer evidence only. Failed attempts and all fixture bytes
// are retained under the requested receipt root for independent review.
internal static class CorpusLengthScanChecks
{
    internal static void Run(string output,RunBudget budget)
    {
        const int maximumCases=24,wallSeconds=100;var watch=Stopwatch.StartNew();var passed=new List<string>();
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("Fresh length-scan check receipt required.");
        var root=Path.GetFullPath(output+".fixture");if(File.Exists(root)||Directory.Exists(root))throw new IOException("Preserve previous length-scan fixtures.");Directory.CreateDirectory(root);
        void Check(){budget.Check();if(watch.Elapsed.TotalSeconds>=wallSeconds)throw new OperationCanceledException("Length-scan fixture deadline100s.");}
        void Passed(string name){Check();if(passed.Count>=maximumCases)throw new InvalidDataException("Length-scan fixture bound24.");passed.Add(name);Console.WriteLine($"length-scan checks={passed.Count}/{maximumCases}: {name}");}
        void Reject(string name,Action action)
        {
            Check();var rejected=false;try{action();}catch(Exception e)when(e is InvalidDataException or FileNotFoundException or JsonException or OperationCanceledException){rejected=true;}
            if(!rejected)throw new InvalidDataException("Length-scan negative fixture accepted: "+name);Passed(name);
        }
        TrainingRow Row(string id,string split="train")=>new(id,Data.Hash(id),id,split,"en",MarkerQuestionType.Boolean,"s","q",[new("false","n"),new("true","y")],[0f,1f],Data.Hash(id+"line"),1);
        (Corpus Corpus,string BindingsSha) Fixture(string name,TrainingRow[] rows,string[] excluded)
        {
            Check();var directory=Data.Within(root,name);Directory.CreateDirectory(directory);var shards=new List<CorpusShard>();
            foreach(var group in rows.GroupBy(r=>r.Split))
            {
                var path=group.Key+".jsonl.gz";
                using(var stream=new FileStream(Data.Within(directory,path),FileMode.CreateNew))
                using(var gzip=new GZipStream(stream,CompressionLevel.Fastest))
                using(var writer=new StreamWriter(gzip,new UTF8Encoding(false)))
                foreach(var row in group){Check();writer.WriteLine(JsonSerializer.Serialize(row,TrainerJson.Default.TrainingRow));}
                shards.Add(new(path,"fixture/source",group.Key,group.Count(),new FileInfo(Data.Within(directory,path)).Length,Data.FileHash(Data.Within(directory,path),budget)));
            }
            Data.WriteNew(Data.Within(directory,"manifest.json"),new CorpusManifest(1,"COMPLETE_PREPARATION_ONLY",false,false,rows.Length,shards.ToArray()),TrainerJson.Default.CorpusManifest);
            File.WriteAllText(Data.Within(directory,"exclusions.jsonl"),string.Concat(excluded.Select(id=>JsonSerializer.Serialize(new CorpusExclusion(id,"fixture_original_conflict"),TrainerJson.Default.CorpusExclusion)+"\n")),new UTF8Encoding(false));
            File.WriteAllText(Data.Within(directory,"source-rights.json"),"[]",new UTF8Encoding(false));File.WriteAllText(Data.Within(directory,"source-assets.json"),"[]",new UTF8Encoding(false));
            var manifestSha=Data.FileHash(Data.Within(directory,"manifest.json"),budget);
            Data.WriteNew(Data.Within(directory,"corpus-bindings.json"),new CorpusBindings(manifestSha,Data.FileHash(Data.Within(directory,"exclusions.jsonl"),budget),Data.FileHash(Data.Within(directory,"source-rights.json"),budget),Data.FileHash(Data.Within(directory,"source-assets.json"),budget),false,null,"source-assets.json"),TrainerJson.Default.CorpusBindings);
            var bindingsSha=Data.FileHash(Data.Within(directory,"corpus-bindings.json"),budget);return(new(directory,manifestSha,bindingsSha,budget),bindingsSha);
        }
        var tokenizerPath=Data.Within(root,"tokenizer.json");var json=new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<unk>\":0,\"<bos>\":1,\"<eos>\":2,\"<mask>\":3");
        for(var index=0;index<256;index++){Check();json.Append($",\"<0x{index:X2}>\":{1000+index}");}
        json.Append("},\"merges\":[]},\"added_tokens\":[{\"id\":3,\"content\":\"<mask>\",\"single_word\":false,\"normalized\":false,\"rstrip\":false,\"lstrip\":true}]}");File.WriteAllText(tokenizerPath,json.ToString(),new UTF8Encoding(false));
        var tokenizer=new TokenizerJson(tokenizerPath,32768,budget.Token);
        var policyPath=Data.Within(root,"private-policy.json");Data.WriteNew(policyPath,new CorpusSourcePolicyDescriptor(1,"PRIVATE_EXPERIMENT_SCOPE_FIXED_METADATA_ONLY",false,false,"deny",["fixture/source"],[],new(StringComparer.Ordinal){["fixture/source"]=[new("raw/fixture/train.jsonl",new('a',64),"fixture revision","train")]}),CorpusSourcePolicyJson.Default.CorpusSourcePolicyDescriptor);
        var policySha=Data.FileHash(policyPath,budget);var noticePath=Data.Within(root,"THIRD_PARTY_NOTICES.md");File.WriteAllText(noticePath,"Synthetic metadata fixture notice.\n",new UTF8Encoding(false));var noticeSha=Data.FileHash(noticePath,budget);
        string? Scope(TrainingRow row)=>row.RecordId=="scope"?"fixture_configuration_not_admitted":null;
        var fixture=Fixture("corpus",[Row("t0"),Row("t1"),Row("t2"),Row("scope"),Row("long") with{State=new('a',400)},Row("original"),Row("choice") with{Type=MarkerQuestionType.Choice},Row("d0","development")],["original"]);
        var scan=Data.Within(root,"scan");
        void Scan(bool resume,int chunks,Func<TrainingRow,string?>? scope=null,int max=256,int chunkRows=2)=>CorpusLengthScan.Run(fixture.Corpus,fixture.BindingsSha,tokenizerPath,tokenizer,max,8,scan,resume,chunkRows,chunks,policyPath,policySha,scope??Scope,budget);
        Scan(false,1);
        if(File.Exists(Data.Within(scan,"length-scan-complete.json"))||Directory.GetFiles(Data.Within(scan,"chunks"),"*.json",SearchOption.TopDirectoryOnly).Length!=1)throw new InvalidDataException("Partial scan committed incomplete completion.");Passed("one_chunk_partial_without_completion");
        var firstPath=Data.Within(scan,"chunks/chunk-00000.json");var firstSha=Data.FileHash(firstPath,budget);
        Reject("incomplete_scan_not_admitted",()=>CorpusLengthScan.Completion(scan,new('0',64),fixture.Corpus,fixture.BindingsSha,budget));
        Reject("changed_token_budget_resume_rejected",()=>Scan(true,8,max:255));
        Reject("changed_chunk_size_resume_rejected",()=>Scan(true,8,chunkRows:3));
        Reject("interrupted_chunk_is_not_committed",()=>Scan(true,8,r=>r.RecordId=="t2"?throw new OperationCanceledException("Synthetic interrupted row"):Scope(r)));
        if(Directory.GetFiles(Data.Within(scan,"chunks"),"*.json",SearchOption.TopDirectoryOnly).Length!=1||File.Exists(Data.Within(scan,"length-scan-complete.json")))throw new InvalidDataException("Interrupted chunk was committed.");Passed("interrupted_attempt_sidecars_retained");
        Scan(true,8,r=>r.RecordId is "t0" or "t1"?throw new InvalidDataException("Completed prefix must not be rendered again"):Scope(r));
        var completePath=Data.Within(scan,"length-scan-complete.json");var completeSha=Data.FileHash(completePath,budget);var complete=CorpusLengthScan.Completion(scan,completeSha,fixture.Corpus,fixture.BindingsSha,budget);
        if(complete.PhysicalRows!=8||complete.Accepted!=5||complete.Train!=4||complete.Development!=1||complete.OriginalExcluded!=1||complete.SourceScopeExcluded!=1||complete.TokenLimitExcluded!=1||complete.Chunks.Length!=5||Data.FileHash(firstPath,budget)!=firstSha)throw new InvalidDataException("Resume strict physical/admitted/exclusion denominator differs.");Passed("resume_skips_rendered_prefix_exact_denominators");
        var choice=fixture.Corpus.Rows(budget).Single(r=>r.Row.RecordId=="choice").Row;var original=Workflow.Sequence(tokenizer,choice,256,false,budget);var rotated=Workflow.Sequence(tokenizer,choice,256,true,budget);
        if(!original.Sequence.CandidateIds.SequenceEqual(choice.Candidates.Select(c=>c.Id))||!rotated.Sequence.CandidateIds.SequenceEqual(choice.Candidates.Skip(1).Concat(choice.Candidates.Take(1)).Select(c=>c.Id)))throw new InvalidDataException("Strict rotated choice did not retain candidate identities.");Passed("original_and_rotated_choice_sequences_checked");
        Scan(true,8,r=>throw new InvalidDataException("Already-complete scan must not render"));Passed("completed_scan_resume_revalidates_bytes_without_rendering");
        File.Move(completePath,Data.Within(scan,"length-scan-complete.retained"),false);Scan(true,8,r=>throw new InvalidDataException("Finalization retry must not render"));
        if(Data.FileHash(completePath,budget)!=completeSha||Data.FileHash(firstPath,budget)!=firstSha)throw new InvalidDataException("Recovered finalization changed pinned completion/chunk bytes.");Passed("ledger_only_finalization_recovery_preserves_chunks");
        var ledgerPath=Data.Within(scan,"scan-exclusions.jsonl");var ledgerBytes=File.ReadAllBytes(ledgerPath);
        try{var changed=ledgerBytes.ToArray();changed[0]^=1;File.WriteAllBytes(ledgerPath,changed);Reject("completed_resume_rejects_mutated_merged_ledger",()=>Scan(true,8));}finally{File.WriteAllBytes(ledgerPath,ledgerBytes);}
        var chunk=Assets.Read(firstPath,CorpusLengthScanJson.Default.CorpusScanChunk);var chunkBytes=File.ReadAllBytes(firstPath);
        try{File.WriteAllText(firstPath,JsonSerializer.Serialize(chunk with{Source="other/source"},CorpusLengthScanJson.Default.CorpusScanChunk),new UTF8Encoding(false));Reject("chunk_source_receipt_must_match_manifest",()=>Scan(true,8));}finally{File.WriteAllBytes(firstPath,chunkBytes);}
        var familyPath=Data.Within(scan,chunk.FamiliesPath);var familyBytes=File.ReadAllBytes(familyPath);
        try{var changed=familyBytes.ToArray();changed[0]^=1;File.WriteAllBytes(familyPath,changed);Reject("resume_rejects_changed_family_sidecar",()=>Scan(true,8));}finally{File.WriteAllBytes(familyPath,familyBytes);}
        var overlap=Fixture("overlap-corpus",[Row("overlap-train") with{Family=Data.Hash("same-family")},Row("overlap-dev","development") with{Family=Data.Hash("same-family")}],[]);var overlapScan=Data.Within(root,"overlap-scan");
        Reject("exact_family_overlap_rejects_full_completion",()=>CorpusLengthScan.Run(overlap.Corpus,overlap.BindingsSha,tokenizerPath,tokenizer,256,2,overlapScan,false,2,8,policyPath,policySha,_=>null,budget));
        if(File.Exists(Data.Within(overlapScan,"length-scan-complete.json")))throw new InvalidDataException("Family-overlap failure emitted completion.");
        var overLimitScan=Data.Within(root,"exclusion-bound-scan");Reject("total_exclusion_identity_limit_fails_without_truncation",()=>CorpusLengthScan.Run(fixture.Corpus,fixture.BindingsSha,tokenizerPath,tokenizer,256,8,overLimitScan,false,2,8,policyPath,policySha,Scope,budget,2));
        if(File.Exists(Data.Within(overLimitScan,"length-scan-complete.json")))throw new InvalidDataException("Exclusion identity cap failure emitted completion.");
        var control=Fixture("control-corpus",[Row("control") with{State=new('a',262144)},Row("control-dev","development")],[]);var controlScan=Data.Within(root,"control-scan");
        Reject("non_token_renderer_failure_is_fatal",()=>CorpusLengthScan.Run(control.Corpus,control.BindingsSha,tokenizerPath,tokenizer,256,2,controlScan,false,2,8,policyPath,policySha,_=>null,budget));
        if(File.Exists(Data.Within(controlScan,"length-scan-complete.json"))||Directory.GetFiles(Data.Within(controlScan,"chunks"),"*.json",SearchOption.TopDirectoryOnly).Length!=0)throw new InvalidDataException("Renderer character-budget failure masqueraded as length exclusion.");Passed("renderer_failure_has_no_complete_chunk_or_admission");
        var derivedRoot=Data.Within(root,"derived-corpus");CorpusLengthScan.Admit(fixture.Corpus,fixture.BindingsSha,scan,completeSha,derivedRoot,policyPath,policySha,noticePath,noticeSha,budget);
        var derivedManifestSha=Data.FileHash(Data.Within(derivedRoot,"manifest.json"),budget);var derivedBindingSha=Data.FileHash(Data.Within(derivedRoot,"corpus-bindings.json"),budget);var derived=new Corpus(derivedRoot,derivedManifestSha,derivedBindingSha,budget);
        var lengths=Assets.Read(Data.Within(derivedRoot,"lengths.json"),CorpusWorkflowJson.Default.CorpusLengthReceipt);CorpusLengthScan.VerifyDerived(derived,lengths,budget);var corpusCheck=derived.Check(8,budget);
        if(corpusCheck.Train!=4||corpusCheck.Development!=1||corpusCheck.ExcludedRows!=3||!derived.Manifest.Shards.SequenceEqual(fixture.Corpus.Manifest.Shards)||fixture.Corpus.Manifest.Shards.Any(s=>Data.FileHash(Data.Within(fixture.Corpus.Root,s.Path),budget)!=s.Sha256))throw new InvalidDataException("Derived admission altered original corpus or exclusion denominator.");Passed("derived_corpus_preserves_shards_and_explicit_exclusion_counts");
        Reject("unbound_or_incomplete_lengths_rejected",()=>CorpusLengthScan.VerifyDerived(derived,lengths with{CompleteScan=false},budget));
        var provenanceRoot=Data.Within(root,"derived-provenance");Directory.CreateDirectory(provenanceRoot);var provenanceSha=CorpusProvenancePackage.Export(derivedRoot,derivedManifestSha,derivedBindingSha,provenanceRoot,budget);CorpusProvenancePackage.Verify(provenanceRoot,provenanceSha,derivedManifestSha,derivedBindingSha,budget);Passed("portable_policy_notice_completion_metadata_package_verified");
        var parentPath=Data.Within(derivedRoot,"components/length-parent/manifest.json");var parentBytes=File.ReadAllBytes(parentPath);
        try{var changed=parentBytes.ToArray();changed[0]^=1;File.WriteAllBytes(parentPath,changed);Reject("derived_lengths_reject_changed_archived_parent",()=>CorpusLengthScan.VerifyDerived(derived,lengths,budget));}finally{File.WriteAllBytes(parentPath,parentBytes);}
        Check();Data.WriteNew(output,new CorpusLengthScanCheckReceipt("PASS_tiny_CPU_gzip_strict_renderer_resume_admission_negative_checks_only",passed.Count,maximumCases,wallSeconds,8,5,1,1,1,false,false,passed.ToArray()),CorpusLengthScanCheckJson.Default.CorpusLengthScanCheckReceipt);
    }
}
