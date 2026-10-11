using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.RepositoryTrainer;

internal sealed record CorpusProvenanceFile(string PackagePath,string OriginalRoot,string OriginalRelativePath,string Sha256,long Bytes,string Role);
internal sealed record CorpusProvenanceReceipt(int SchemaVersion,string Status,string CorpusManifestSha256,string OriginalBindingsSha256,
    string? OriginalSourceAssetsPath,string? OriginalSourceRoot,string SourceAssetsPath,string? SourceRoot,
    CorpusProvenanceFile[] Files,Dictionary<string,string> SourceNoticeBindings,bool PrivateSourceMetadata,bool PublicPublicationAccepted,
    bool ContainsRawExamplesOrGoldLabels,bool ContainsPerRecordExclusionIdentities);

[JsonSerializable(typeof(CorpusProvenanceReceipt))]
[JsonSourceGenerationOptions(WriteIndented=false)]
internal partial class CorpusProvenanceJson:JsonSerializerContext;

// Copies declared private provenance only. No model/data execution, model
// features, raw shards or teacher target records are read by this helper.
internal static class CorpusProvenancePackage
{
    private const int MaximumItems=512;
    private const long MaximumFileBytes=16*1024*1024;
    private const long MaximumTotalBytes=128*1024*1024;
    private static string PortableRelative(string path)=>path.Replace('\\','/');
    private static bool Sha(string value)=>value.Length==64&&value.All(char.IsAsciiHexDigit);
    private static string Role(string path)
    {
        var portable=PortableRelative(path);var name=portable[(portable.LastIndexOf('/')+1)..];
        if(name=="exclusions.jsonl")return "private_exclusion_identity_ledger";
        if(name is "manifest.json" or "corpus-bindings.json" or "source-rights.json" or "source-assets.json")return "source_metadata_descriptor";
        if(name is "SOURCE-README.md" or "SOURCE-NOTICE.txt" or "UPSTREAM-README.md" or "SOURCE_LICENSES.md"||
            name.StartsWith("README",StringComparison.OrdinalIgnoreCase)||name.StartsWith("LICENSE",StringComparison.OrdinalIgnoreCase)||
            name.StartsWith("NOTICE",StringComparison.OrdinalIgnoreCase)||name.StartsWith("COPYING",StringComparison.OrdinalIgnoreCase)||name.Equals("CITATION.cff",StringComparison.OrdinalIgnoreCase))return "source_notice_declared_private";
        throw new InvalidDataException("Source notice binding references unsupported/raw-example filename; preserve the failed package.");
    }
    private static void PathShape(string path)
    {
        var parts=PortableRelative(path).Split('/');
        if(parts.Length is <1 or >16||parts.Any(p=>p.Length==0||p is "." or ".."||p.Equals("shards",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Provenance path depth/raw-shard bound.");
    }

    internal static string Export(string corpusRoot,string manifestSha,string bindingsSha,string packageRoot,RunBudget budget)
    {
        var watch=Stopwatch.StartNew();long copiedBytes=0;var files=new List<CorpusProvenanceFile>();var notices=new Dictionary<string,string>(StringComparer.Ordinal);
        void Check(){budget.Check();if(watch.Elapsed.TotalSeconds>=120)throw new OperationCanceledException("Provenance copy deadline120s.");}
        string Copy(string sourceRoot,string relative,string destination,string expected,string role)
        {
            Check();PathShape(relative);PathShape(destination);var source=Data.Within(sourceRoot,PortableRelative(relative));var output=Data.Within(packageRoot,destination);var bytes=new FileInfo(source).Length;
            if(!Sha(expected)||bytes>MaximumFileBytes||files.Count>=MaximumItems||copiedBytes+bytes>MaximumTotalBytes||Data.FileHash(source,budget)!=expected)throw new InvalidDataException("Provenance byte/item/SHA gate differs.");
            if(File.Exists(output)||Directory.Exists(output))throw new IOException("Provenance output exists; preserve it.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);Assets.CopyNew(source,output,budget);Check();
            if(new FileInfo(output).Length!=bytes||Data.FileHash(output,budget)!=expected||Data.FileHash(source,budget)!=expected)throw new InvalidDataException("Source metadata changed or copied bytes differ.");
            copiedBytes+=bytes;files.Add(new(destination,Path.GetFullPath(sourceRoot),relative,expected,bytes,role));return destination;
        }
        Check();corpusRoot=Path.GetFullPath(corpusRoot);packageRoot=Path.GetFullPath(packageRoot);
        var bindingPath=Data.Within(corpusRoot,"corpus-bindings.json");
        if(!Sha(manifestSha)||!Sha(bindingsSha)||Data.FileHash(bindingPath,budget)!=bindingsSha||Data.FileHash(Data.Within(corpusRoot,"manifest.json"),budget)!=manifestSha)throw new InvalidDataException("Pinned corpus/metadata binding changed before packaging.");
        var binding=Assets.Read(bindingPath,TrainerJson.Default.CorpusBindings);
        if(binding.ManifestSha256!=manifestSha||binding.FormalTrainingAdmitted||!Sha(binding.SourceRightsSha256)||!Sha(binding.SourceAssetsSha256))throw new InvalidDataException("Experimental source metadata bindings differ.");
        Copy(corpusRoot,"manifest.json","input-manifest.json",manifestSha,"source_metadata_descriptor");
        Copy(corpusRoot,"corpus-bindings.json","input-corpus-bindings.json",bindingsSha,"original_binding_historical_paths");
        Copy(corpusRoot,"source-rights.json","input-source-rights.json",binding.SourceRightsSha256,"source_rights_declared_private");
        string assetsRoot,assetsRelative;
        if(binding.SourceAssetsPath is not null){assetsRoot=corpusRoot;assetsRelative=binding.SourceAssetsPath;}
        else if(File.Exists(Data.Within(corpusRoot,"source-assets.json"))){assetsRoot=corpusRoot;assetsRelative="source-assets.json";}
        else if(binding.SourceRoot is not null){assetsRoot=Path.GetFullPath(binding.SourceRoot);assetsRelative="ASSET_MANIFEST.json";}
        else throw new InvalidDataException("SourceRoot absent and local bound source-assets missing.");
        Copy(assetsRoot,assetsRelative,"input-source-assets.json",binding.SourceAssetsSha256,"source_asset_descriptors_no_raw_payloads");
        using(var input=File.OpenRead(Data.Within(packageRoot,"input-manifest.json")))
        using(var manifest=JsonDocument.Parse(input,new(){MaxDepth=32}))
        if(manifest.RootElement.TryGetProperty("source_notice_bindings",out var bindings))
        {
            if(bindings.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Notice bindings must be an explicit path/SHA object.");
            foreach(var item in bindings.EnumerateObject())
            {
                Check();PathShape(item.Name);var role=Role(item.Name);var sha=item.Value.GetString()??throw new InvalidDataException("Missing source notice SHA.");
                var destination="corpus-provenance/"+item.Name.Replace('\\','/');
                Copy(corpusRoot,item.Name,destination,sha,role);if(!notices.TryAdd(destination,sha))throw new InvalidDataException("Duplicate source notice path.");
            }
        }
        Check();var receipt=new CorpusProvenanceReceipt(1,"PASS_private_bound_source_metadata_bytes_only",manifestSha,bindingsSha,binding.SourceAssetsPath,binding.SourceRoot,
            "input-source-assets.json",null,files.ToArray(),notices,true,false,false,files.Any(f=>f.Role=="private_exclusion_identity_ledger"));
        var receiptPath=Data.Within(packageRoot,"corpus-provenance.json");var pending=Data.Within(packageRoot,"corpus-provenance.pending");
        using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(stream,receipt,CorpusProvenanceJson.Default.CorpusProvenanceReceipt);stream.Flush(true);}
        Check();var receiptSha=Data.FileHash(pending,budget);Check();File.Move(pending,receiptPath,false);return receiptSha;
    }

    internal static void Verify(string packageRoot,string receiptSha,string expectedManifestSha,string expectedBindingsSha,RunBudget budget)
    {
        var watch=Stopwatch.StartNew();budget.Check();var path=Data.Within(packageRoot,"corpus-provenance.json");
        if(!Sha(receiptSha)||Data.FileHash(path,budget)!=receiptSha)throw new InvalidDataException("Standalone source provenance receipt SHA differs.");
        var receipt=Assets.Read(path,CorpusProvenanceJson.Default.CorpusProvenanceReceipt);
        if(receipt.SchemaVersion!=1||receipt.Status!="PASS_private_bound_source_metadata_bytes_only"||receipt.Files.Length is <4 or >MaximumItems||!receipt.PrivateSourceMetadata||receipt.PublicPublicationAccepted||receipt.ContainsRawExamplesOrGoldLabels||receipt.SourceAssetsPath!="input-source-assets.json"||receipt.SourceRoot is not null||receipt.Files.Select(f=>f.PackagePath).Distinct(StringComparer.Ordinal).Count()!=receipt.Files.Length||!Sha(expectedManifestSha)||!Sha(expectedBindingsSha)||receipt.CorpusManifestSha256!=expectedManifestSha||receipt.OriginalBindingsSha256!=expectedBindingsSha)throw new InvalidDataException("Private standalone provenance contract/global identity differs.");
        long total=0;
        foreach(var file in receipt.Files)
        {
            budget.Check();if(watch.Elapsed.TotalSeconds>=120)throw new OperationCanceledException("Standalone provenance deadline120s.");PathShape(file.PackagePath);
            var source=Data.Within(packageRoot,file.PackagePath);total=checked(total+file.Bytes);
            if(file.Bytes<0||file.Bytes>MaximumFileBytes||total>MaximumTotalBytes||!Sha(file.Sha256)||new FileInfo(source).Length!=file.Bytes||Data.FileHash(source,budget)!=file.Sha256)throw new InvalidDataException("Standalone source metadata/notice byte identity differs.");
        }
        var mapped=receipt.Files.ToDictionary(f=>f.PackagePath,f=>f,StringComparer.Ordinal);
        if(!mapped.TryGetValue("input-manifest.json",out var manifest)||manifest.Sha256!=receipt.CorpusManifestSha256||!mapped.TryGetValue("input-corpus-bindings.json",out var bindings)||bindings.Sha256!=receipt.OriginalBindingsSha256||!mapped.ContainsKey(receipt.SourceAssetsPath)||!mapped.ContainsKey("input-source-rights.json"))throw new InvalidDataException("Standalone source path mapping differs.");
        var original=Assets.Read(Data.Within(packageRoot,"input-corpus-bindings.json"),TrainerJson.Default.CorpusBindings);
        if(original.FormalTrainingAdmitted||original.ManifestSha256!=receipt.CorpusManifestSha256||original.SourceAssetsSha256!=mapped[receipt.SourceAssetsPath].Sha256||original.SourceRightsSha256!=mapped["input-source-rights.json"].Sha256||original.SourceRoot!=receipt.OriginalSourceRoot||original.SourceAssetsPath!=receipt.OriginalSourceAssetsPath)throw new InvalidDataException("Historical source-root/assets semantics changed.");
        var declared=new Dictionary<string,string>(StringComparer.Ordinal);
        using(var stream=File.OpenRead(Data.Within(packageRoot,"input-manifest.json")))
        using(var manifestJson=JsonDocument.Parse(stream,new(){MaxDepth=32}))
        if(manifestJson.RootElement.TryGetProperty("source_notice_bindings",out var noticeBindings))
        {
            if(noticeBindings.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Historical notice bindings object differs.");
            foreach(var notice in noticeBindings.EnumerateObject())
            {
                budget.Check();if(declared.Count>=MaximumItems)throw new InvalidDataException("Historical notice item bound512.");PathShape(notice.Name);
                var destination="corpus-provenance/"+notice.Name.Replace('\\','/');var role=Role(notice.Name);var sha=notice.Value.GetString()??throw new InvalidDataException("Historical notice SHA absent.");
                if(!declared.TryAdd(destination,sha)||!mapped.TryGetValue(destination,out var file)||file.Sha256!=sha||file.Role!=role||file.OriginalRelativePath!=notice.Name)throw new InvalidDataException("Complete historical notice mapping differs.");
            }
        }
        if(declared.Count!=receipt.SourceNoticeBindings.Count||mapped.Count!=4+declared.Count)throw new InvalidDataException("Source metadata receipt omits or adds declared files.");
        foreach(var notice in receipt.SourceNoticeBindings)
        {budget.Check();if(!declared.TryGetValue(notice.Key,out var expected)||expected!=notice.Value||!mapped.TryGetValue(notice.Key,out var file)||file.Sha256!=notice.Value)throw new InvalidDataException("Notice SHA/mapping differs.");}
        if(receipt.ContainsPerRecordExclusionIdentities!=receipt.Files.Any(f=>f.Role=="private_exclusion_identity_ledger"))throw new InvalidDataException("Private exclusion identity declaration differs.");
    }

    internal static int Checks(string root,RunBudget budget)
    {
        var checks=0;var source=Data.Within(root,"private-provenance-source");Directory.CreateDirectory(source);
        var component=Data.Within(source,"components/root-00");Directory.CreateDirectory(Data.Within(component,"source-notices"));
        File.WriteAllText(Data.Within(source,"source-assets.json"),"[]",new UTF8Encoding(false));File.WriteAllText(Data.Within(source,"source-rights.json"),"[]",new UTF8Encoding(false));File.WriteAllText(Data.Within(source,"exclusions.jsonl"),"",new UTF8Encoding(false));
        File.WriteAllText(Data.Within(component,"manifest.json"),"{\"status\":\"COMPLETE_PREPARATION_ONLY\"}",new UTF8Encoding(false));
        File.WriteAllText(Data.Within(component,"exclusions.jsonl"),"{\"record_id\":\"synthetic-private-record-id\",\"reason\":\"conflict\"}\n",new UTF8Encoding(false));
        var notice=Data.Within(component,"source-notices/UPSTREAM-README.md");File.WriteAllText(notice,"Private fixture upstream notice. 中文原字节。\n",new UTF8Encoding(false));
        var noticeSha=Data.FileHash(notice,budget);var componentSha=Data.FileHash(Data.Within(component,"manifest.json"),budget);var exclusionsSha=Data.FileHash(Data.Within(component,"exclusions.jsonl"),budget);
        File.WriteAllText(Data.Within(source,"manifest.json"),"{\"schema_version\":1,\"source_notice_bindings\":{\"components/root-00/manifest.json\":\""+componentSha+"\",\"components/root-00/exclusions.jsonl\":\""+exclusionsSha+"\",\"components/root-00/source-notices/UPSTREAM-README.md\":\""+noticeSha+"\"}}",new UTF8Encoding(false));
        var manifestSha=Data.FileHash(Data.Within(source,"manifest.json"),budget);var binding=new CorpusBindings(manifestSha,Data.FileHash(Data.Within(source,"exclusions.jsonl"),budget),Data.FileHash(Data.Within(source,"source-rights.json"),budget),Data.FileHash(Data.Within(source,"source-assets.json"),budget),false,null,"source-assets.json");
        Data.WriteNew(Data.Within(source,"corpus-bindings.json"),binding,TrainerJson.Default.CorpusBindings);var bindingsSha=Data.FileHash(Data.Within(source,"corpus-bindings.json"),budget);
        var package=Data.Within(root,"private-provenance-package");Directory.CreateDirectory(package);var receiptSha=Export(source,manifestSha,bindingsSha,package,budget);Verify(package,receiptSha,manifestSha,bindingsSha,budget);checks++;
        var receipt=Assets.Read(Data.Within(package,"corpus-provenance.json"),CorpusProvenanceJson.Default.CorpusProvenanceReceipt);
        if(receipt.OriginalSourceRoot is not null||receipt.SourceRoot is not null||!receipt.PrivateSourceMetadata||receipt.PublicPublicationAccepted||!receipt.ContainsPerRecordExclusionIdentities||receipt.Files.Length!=7)throw new InvalidDataException("Absent SourceRoot/composed private descriptor mapping differs.");checks++;
        var copy=Data.Within(package,"corpus-provenance/components/root-00/source-notices/UPSTREAM-README.md");if(!File.ReadAllBytes(copy).SequenceEqual(File.ReadAllBytes(notice)))throw new InvalidDataException("Bound source notice original bytes changed.");checks++;
        var bytes=File.ReadAllBytes(copy);var changedRejected=false;
        try{var changed=bytes.ToArray();changed[0]^=1;File.WriteAllBytes(copy,changed);try{Verify(package,receiptSha,manifestSha,bindingsSha,budget);}catch(InvalidDataException){changedRejected=true;}}
        finally{File.WriteAllBytes(copy,bytes);}
        if(!changedRejected)throw new InvalidDataException("Packaged notice mutation accepted.");checks++;
        var badSource=Data.Within(root,"private-provenance-tamper-failed");Directory.CreateDirectory(badSource);var sourceBytes=File.ReadAllBytes(notice);var sourceChangedRejected=false;
        try{var changed=sourceBytes.ToArray();changed[0]^=1;File.WriteAllBytes(notice,changed);try{Export(source,manifestSha,bindingsSha,badSource,budget);}catch(InvalidDataException){sourceChangedRejected=true;}}
        finally{File.WriteAllBytes(notice,sourceBytes);}
        if(!sourceChangedRejected||File.Exists(Data.Within(badSource,"corpus-provenance.json")))throw new InvalidDataException("Source notice mutation produced a complete provenance marker.");checks++;
        var assets=Data.Within(source,"source-assets.json");var retained=Data.Within(source,"source-assets.retained");var missing=Data.Within(root,"private-provenance-missing-assets-failed");Directory.CreateDirectory(missing);var missingRejected=false;
        try{File.Move(assets,retained,false);try{Export(source,manifestSha,bindingsSha,missing,budget);}catch(FileNotFoundException){missingRejected=true;}}
        finally{File.Move(retained,assets,false);}
        if(!missingRejected)throw new InvalidDataException("Absent SourceRoot and bound local assets were accepted.");checks++;
        var identityRejected=false;try{Verify(package,receiptSha,new('0',64),bindingsSha,budget);}catch(InvalidDataException){identityRejected=true;}
        if(!identityRejected)throw new InvalidDataException("Self-consistent provenance from another global corpus accepted.");checks++;
        var receiptPath=Data.Within(package,"corpus-provenance.json");var receiptBytes=File.ReadAllBytes(receiptPath);var missingMappingRejected=false;
        try
        {
            var reduced=receipt with{SourceNoticeBindings=new(receipt.SourceNoticeBindings.Where(p=>p.Key!=receipt.SourceNoticeBindings.Keys.Last()),StringComparer.Ordinal)};
            File.WriteAllText(receiptPath,JsonSerializer.Serialize(reduced,CorpusProvenanceJson.Default.CorpusProvenanceReceipt),new UTF8Encoding(false));
            try{Verify(package,Data.FileHash(receiptPath,budget),manifestSha,bindingsSha,budget);}catch(InvalidDataException){missingMappingRejected=true;}
        }
        finally{File.WriteAllBytes(receiptPath,receiptBytes);}
        if(!missingMappingRejected)throw new InvalidDataException("Provenance omitted a declared notice mapping.");checks++;
        Verify(package,receiptSha,manifestSha,bindingsSha,budget);
        // Keep the original eight provenance checks unchanged. This separate
        // fixture exercises the exact license_evidence filename bound by the
        // real composed corpus; other SOURCE_* payload names remain rejected.
        var portableLicense=PortableRelative(@"source-notices\SOURCE_LICENSES.md");
        if(portableLicense!="source-notices/SOURCE_LICENSES.md"||portableLicense.Contains('\\')||PortableRelative(portableLicense)!=portableLicense||Role(@"source-notices\SOURCE_LICENSES.md")!=Role(portableLicense))throw new InvalidDataException("Windows notice path does not normalize to host-independent Unix separators/name.");checks++;
        var licenseSource=Data.Within(root,"private-provenance-license-evidence-source");Directory.CreateDirectory(Data.Within(licenseSource,"source-notices"));
        File.WriteAllText(Data.Within(licenseSource,"source-assets.json"),"[]",new UTF8Encoding(false));File.WriteAllText(Data.Within(licenseSource,"source-rights.json"),"[]",new UTF8Encoding(false));File.WriteAllText(Data.Within(licenseSource,"exclusions.jsonl"),"",new UTF8Encoding(false));
        var licensePath=Data.Within(licenseSource,"source-notices/SOURCE_LICENSES.md");File.WriteAllText(licensePath,"Synthetic source license evidence. 中文许可原字节。\n",new UTF8Encoding(false));var licenseSha=Data.FileHash(licensePath,budget);
        File.WriteAllText(Data.Within(licenseSource,"manifest.json"),"{\"schema_version\":1,\"source_notice_bindings\":{\"source-notices\\\\SOURCE_LICENSES.md\":\""+licenseSha+"\"}}",new UTF8Encoding(false));
        var licenseManifestSha=Data.FileHash(Data.Within(licenseSource,"manifest.json"),budget);var licenseBinding=new CorpusBindings(licenseManifestSha,Data.FileHash(Data.Within(licenseSource,"exclusions.jsonl"),budget),Data.FileHash(Data.Within(licenseSource,"source-rights.json"),budget),Data.FileHash(Data.Within(licenseSource,"source-assets.json"),budget),false,null,"source-assets.json");
        Data.WriteNew(Data.Within(licenseSource,"corpus-bindings.json"),licenseBinding,TrainerJson.Default.CorpusBindings);var licenseBindingsSha=Data.FileHash(Data.Within(licenseSource,"corpus-bindings.json"),budget);
        var licensePackage=Data.Within(root,"private-provenance-license-evidence-package");Directory.CreateDirectory(licensePackage);var licenseReceiptSha=Export(licenseSource,licenseManifestSha,licenseBindingsSha,licensePackage,budget);Verify(licensePackage,licenseReceiptSha,licenseManifestSha,licenseBindingsSha,budget);
        var licenseReceipt=Assets.Read(Data.Within(licensePackage,"corpus-provenance.json"),CorpusProvenanceJson.Default.CorpusProvenanceReceipt);var licenseMapping=licenseReceipt.Files.Single(f=>f.PackagePath=="corpus-provenance/source-notices/SOURCE_LICENSES.md");
        if(licenseReceipt.Files.Length!=5||licenseMapping.Sha256!=licenseSha||licenseMapping.OriginalRelativePath!=@"source-notices\SOURCE_LICENSES.md"||licenseMapping.Role!="source_notice_declared_private"||!File.ReadAllBytes(licensePath).SequenceEqual(File.ReadAllBytes(Data.Within(licensePackage,licenseMapping.PackagePath))))throw new InvalidDataException("Exact SOURCE_LICENSES.md bound SHA/byte/original Windows path mapping differs.");checks++;
        var licenseFailed=Data.Within(root,"private-provenance-license-evidence-sha-failed");Directory.CreateDirectory(licenseFailed);var originalLicenseBytes=File.ReadAllBytes(licensePath);var licenseChangedRejected=false;
        try{var changed=originalLicenseBytes.ToArray();changed[0]^=1;File.WriteAllBytes(licensePath,changed);try{Export(licenseSource,licenseManifestSha,licenseBindingsSha,licenseFailed,budget);}catch(InvalidDataException){licenseChangedRejected=true;}}
        finally{File.WriteAllBytes(licensePath,originalLicenseBytes);}
        if(!licenseChangedRejected||File.Exists(Data.Within(licenseFailed,"corpus-provenance.json")))throw new InvalidDataException("SOURCE_LICENSES.md original SHA changed but package completed.");checks++;
        return checks;
    }
}
