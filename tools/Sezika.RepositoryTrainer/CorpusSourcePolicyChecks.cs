using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sezika;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusSourcePolicyCheckReceipt(string Status,int Checks,int FixtureRecords,int MaximumFixtureCases,
    int MaximumFixtureFiles,int WallLimitSeconds,bool GpuTrainingRun,bool RealCorpusReviewed,string[] PassedChecks);

[JsonSerializable(typeof(CorpusSourcePolicyCheckReceipt))]
internal partial class CorpusSourcePolicyCheckJson:JsonSerializerContext;

// These fixtures contain synthetic metadata only. They exercise the real reader
// and policy validator without admitting or scanning a production corpus.
internal static class CorpusSourcePolicyChecks
{
    internal static void Run(string output,RunBudget budget)
    {
        const int maximumCases=24,maximumFiles=24,wallSeconds=90;
        const string dataset="fixture/allowed",deniedDataset="fixture/denied",sourcePath="raw/fixture/train.parquet";
        const string revision="frozen fixture upstream release",deniedReason="fixture_component_rights_unverified";
        var outputPath=Path.GetFullPath(output);
        if(File.Exists(outputPath)||Directory.Exists(outputPath))throw new IOException("Fresh source-policy check receipt required.");
        var parent=Path.GetDirectoryName(outputPath)??throw new IOException("Source-policy check output parent required.");
        Directory.CreateDirectory(parent);
        var token=Guid.NewGuid().ToString("N");
        var fixture=Data.Within(parent,".corpus-source-policy-check-"+token);
        if(File.Exists(fixture)||Directory.Exists(fixture))throw new IOException("Preserve existing source-policy fixture.");
        var ownedFiles=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        var passed=new List<string>();var watch=Stopwatch.StartNew();
        var markerText="CorpusSourcePolicyChecks:"+token;
        Directory.CreateDirectory(fixture);
        var marker=Data.Within(fixture,"OWNED_BY_CORPUS_SOURCE_POLICY_CHECKS");
        using(var markerStream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {var bytes=Encoding.UTF8.GetBytes(markerText);markerStream.Write(bytes);}

        void Check()
        {
            budget.Check();
            if(watch.Elapsed.TotalSeconds>=wallSeconds-10)throw new OperationCanceledException("Source-policy tiny work deadline80s;10s reserved for cleanup.");
        }
        void Passed(string name)
        {
            Check();if(passed.Count>=maximumCases)throw new InvalidDataException("Source-policy fixture case bound24.");
            passed.Add(name);Console.WriteLine($"source-policy checks={passed.Count}/{maximumCases}: {name}");
        }
        FileStream NewFile(string name)
        {
            Check();if(ownedFiles.Count>=maximumFiles-1)throw new InvalidDataException("Source-policy fixture file bound24.");
            var path=Data.Within(fixture,name);
            var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            ownedFiles.Add(path);return stream;
        }
        string WriteJson<T>(string name,T value,JsonTypeInfo<T> info)
        {
            using(var stream=NewFile(name))JsonSerializer.Serialize(stream,value,info);
            return Data.Within(fixture,name);
        }
        string WriteText(string name,string value)
        {
            using(var stream=NewFile(name)){var bytes=Encoding.UTF8.GetBytes(value);stream.Write(bytes);}
            return Data.Within(fixture,name);
        }
        void ExpectReason(string name,CorpusSourcePolicy policy,TrainingRow row,string? reason)
        {
            Check();var actual=policy.Failure(row);
            if(actual!=reason)throw new InvalidDataException($"Source-policy {name} returned '{actual}' instead of '{reason}'.");
            Passed(name);
        }
        void ExpectRejected(string name,Action action)
        {
            Check();var rejected=false;
            try{action();}catch(Exception error)when(error is InvalidDataException or JsonException){rejected=true;}
            if(!rejected)throw new InvalidDataException("Source-policy negative fixture was accepted: "+name);
            Passed(name);
        }

        try
        {
            var assetSha=new string('a',64);
            var asset=new CorpusAllowedAsset(sourcePath,assetSha,revision,"train");
            var descriptor=new CorpusSourcePolicyDescriptor(1,"PRIVATE_EXPERIMENT_SCOPE_FIXED_METADATA_ONLY",false,false,"deny",
                [dataset],[new(deniedDataset,deniedReason)],new(StringComparer.Ordinal){[dataset]=[asset]});
            var policyPath=WriteJson("source-scope-policy.json",descriptor,CorpusSourcePolicyJson.Default.CorpusSourcePolicyDescriptor);
            var policySha=Data.FileHash(policyPath,budget);
            var policy=new CorpusSourcePolicy(policyPath,policySha,budget);
            var row=new TrainingRow("fixture-record",new('1',64),"fixture-state","train","en",MarkerQuestionType.Boolean,
                "synthetic fixture state","synthetic fixture decision",[new("false","No"),new("true","Yes")],[0f,1f],new('2',64),1)
                {Source=new(dataset,revision,sourcePath,assetSha,"train",1)};
            var provenance=row.Source!;
            // The first two cases are the tiny smoke before the negative batch.
            ExpectReason("exact_asset_accepted",policy,row,null);
            ExpectReason("path_separator_normalization_accepted",policy,row with{Source=provenance with{Path=sourcePath.Replace('/','\\')}},null);
            ExpectReason("missing_provenance_rejected",policy,row with{Source=null},"source_scope_provenance_missing");
            ExpectReason("missing_provenance_dataset_rejected",policy,row with{Source=provenance with{Dataset=" "}},"source_scope_provenance_missing");
            ExpectReason("missing_provenance_path_rejected",policy,row with{Source=provenance with{Path=null!}},"source_scope_provenance_missing");
            ExpectReason("unknown_dataset_rejected",policy,row with{Source=provenance with{Dataset="fixture/unknown"}},"source_scope_dataset_not_reviewed");
            ExpectReason("unknown_configuration_rejected",policy,row with{Source=provenance with{Path="raw/fixture/customer-control/train.parquet"}},"source_scope_configuration_not_reviewed");
            const string identityFailure="source_scope_asset_identity_or_original_split_differs";
            ExpectReason("original_split_mismatch_rejected",policy,row with{Source=provenance with{OriginalSplit="test"}},identityFailure);
            ExpectReason("revision_mismatch_rejected",policy,row with{Source=provenance with{Revision="another release"}},identityFailure);
            ExpectReason("file_sha_mismatch_rejected",policy,row with{Source=provenance with{FileSha256=new('b',64)}},identityFailure);
            ExpectReason("source_row_mismatch_rejected",policy,row with{Source=provenance with{Row=2}},identityFailure);
            ExpectReason("explicit_source_deny_preserves_reason",policy,row with{Source=provenance with{Dataset=deniedDataset}},deniedReason);

            var negativeNumber=0;
            void RejectDescriptor(string name,CorpusSourcePolicyDescriptor changed)
            {
                Check();if(negativeNumber>=8)throw new InvalidDataException("Source-policy descriptor fixture bound8.");
                var path=WriteJson($"negative-policy-{negativeNumber++:00}.json",changed,CorpusSourcePolicyJson.Default.CorpusSourcePolicyDescriptor);
                var sha=Data.FileHash(path,budget);ExpectRejected(name,()=>_ =new CorpusSourcePolicy(path,sha,budget));
            }
            RejectDescriptor("duplicate_allowed_dataset_rejected",descriptor with{SourceAllowlist=[dataset,dataset]});
            RejectDescriptor("duplicate_normalized_asset_rejected",descriptor with{SourceAssetAllowlist=new(StringComparer.Ordinal)
                {[dataset]=[asset,asset with{Path=sourcePath.Replace('/','\\')}]}});
            RejectDescriptor("duplicate_denied_dataset_rejected",descriptor with{SourceDenylist=[new(deniedDataset,deniedReason),new(deniedDataset,deniedReason)]});
            RejectDescriptor("allow_deny_overlap_rejected",descriptor with{SourceDenylist=[new(dataset,deniedReason)]});
            RejectDescriptor("malformed_asset_sha_rejected",descriptor with{SourceAssetAllowlist=new(StringComparer.Ordinal){[dataset]=[asset with{FileSha256=new('z',64)}]}});
            RejectDescriptor("default_allow_rejected",descriptor with{DefaultAction="allow"});
            var malformed=WriteText("malformed-policy.json","{\"SchemaVersion\":1,");
            var malformedSha=Data.FileHash(malformed,budget);
            ExpectRejected("malformed_json_rejected",()=>_ =new CorpusSourcePolicy(malformed,malformedSha,budget));
            // Deliberate null-forgiving input models malformed external JSON.
            // NullReferenceException is a validator defect, not an expected gate.
            RejectDescriptor("null_required_policy_array_rejected",descriptor with{SourceAllowlist=null!});
            ExpectRejected("pinned_policy_sha_mismatch_rejected",()=>_ =new CorpusSourcePolicy(policyPath,new('0',64),budget));

            var shardPath=Data.Within(fixture,"tiny.jsonl.gz");
            using(var compressed=NewFile("tiny.jsonl.gz"))
            using(var gzip=new GZipStream(compressed,CompressionLevel.Fastest))
            using(var writer=new StreamWriter(gzip,new UTF8Encoding(false)))
            {Check();writer.WriteLine(JsonSerializer.Serialize(row,TrainerJson.Default.TrainingRow));}
            var ledgerPath=WriteText("exclusions.jsonl","");
            var rightsPath=WriteText("source-rights.json","[]\n");
            var assetsPath=WriteText("source-assets.json","[]\n");
            var lengthPath=WriteText("length-scan-complete.json","{\"Status\":\"fixture_complete\",\"Rows\":1}\n");
            var manifest=new CorpusManifest(1,"COMPLETE_PREPARATION_ONLY",false,false,1,
                [new("tiny.jsonl.gz",dataset,"train",1,new FileInfo(shardPath).Length,Data.FileHash(shardPath,budget))]);
            var manifestPath=WriteJson("manifest.json",manifest,TrainerJson.Default.CorpusManifest);
            var manifestSha=Data.FileHash(manifestPath,budget);
            var bindings=new CorpusBindings(manifestSha,Data.FileHash(ledgerPath,budget),Data.FileHash(rightsPath,budget),
                Data.FileHash(assetsPath,budget),false,SourceAssetsPath:"source-assets.json")
                {SourcePolicySha256=policySha,LengthScanSha256=Data.FileHash(lengthPath,budget)};
            var bindingsPath=WriteJson("corpus-bindings.json",bindings,TrainerJson.Default.CorpusBindings);
            var bindingsSha=Data.FileHash(bindingsPath,budget);
            var corpus=new Corpus(fixture,manifestSha,bindingsSha,budget);
            var observed=0;
            foreach(var item in corpus.Rows(budget))
            {
                Check();if(++observed>1||item.Excluded||item.Row.RecordId!=row.RecordId||item.Source!=dataset||policy.Failure(item.Row) is not null)
                    throw new InvalidDataException("Tiny gzip source/binding fixture differs.");
            }
            if(observed!=1)throw new InvalidDataException("Tiny gzip physical denominator differs.");
            Passed("tiny_gzip_bound_source_policy_and_length_scan_accepted");

            void RejectBoundTamper(string name,string path)
            {
                Check();if(!ownedFiles.Contains(path)||Data.Within(fixture,Path.GetFileName(path))!=path)
                    throw new IOException("Tamper fixture ownership differs.");
                var original=File.ReadAllBytes(path);if(original.Length>16384)throw new IOException("Tiny tamper byte bound16KiB.");
                try
                {
                    var changed=new byte[original.Length+1];original.CopyTo(changed,0);changed[^1]=(byte)' ';
                    File.WriteAllBytes(path,changed);
                    ExpectRejected(name,()=>_ =new Corpus(fixture,manifestSha,bindingsSha,budget));
                }
                finally{File.WriteAllBytes(path,original);}
            }
            RejectBoundTamper("bound_source_policy_tamper_rejected",policyPath);
            RejectBoundTamper("bound_length_scan_tamper_rejected",lengthPath);
        }
        finally
        {
            // Cleanup ignores task cancellation but has its own small deadline.
            // Only our registered files and exact ownership marker may be removed.
            var cleanup=Stopwatch.StartNew();
            var checkedFixture=Data.Within(parent,Path.GetFileName(fixture));
            if(checkedFixture!=fixture||File.ReadAllText(marker,Encoding.UTF8)!=markerText)
                throw new IOException("Source-policy fixture cleanup ownership differs; preserve it.");
            var entries=Directory.EnumerateFileSystemEntries(fixture).Take(maximumFiles+1).ToArray();
            if(entries.Length!=ownedFiles.Count+1||entries.Length>maximumFiles||
                entries.Any(path=>path!=marker&&!ownedFiles.Contains(path)))
                throw new IOException("Unexpected fixture entries; preserve source-policy fixture.");
            foreach(var path in ownedFiles)
            {
                if(cleanup.Elapsed.TotalSeconds>=10)throw new IOException("Source-policy fixture cleanup deadline10s.");
                if(Data.Within(fixture,Path.GetFileName(path))!=path)throw new IOException("Fixture file cleanup path differs.");
                File.Delete(path);
            }
            File.Delete(marker);Directory.Delete(fixture,false);
        }
        budget.Check();
        if(watch.Elapsed.TotalSeconds>=wallSeconds)throw new OperationCanceledException("Source-policy tiny total deadline90s.");
        if(passed.Count!=24)throw new InvalidDataException("Source-policy tiny check denominator24 differs.");
        Data.WriteNew(outputPath,new CorpusSourcePolicyCheckReceipt("PASS_tiny_offline_source_policy_and_binding_contracts_only",passed.Count,1,
            maximumCases,maximumFiles,wallSeconds,false,false,passed.ToArray()),CorpusSourcePolicyCheckJson.Default.CorpusSourcePolicyCheckReceipt);
        Console.WriteLine($"source-policy checks={passed.Count};fixtures-cleaned=true;GPU training=NOT_RUN;real corpus=NOT_SCANNED");
    }
}
