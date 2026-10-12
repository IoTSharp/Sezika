using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

// Hardware candidacy is separate from actual PTX/forward/backward qualification.
// The 4090 route still needs a fresh full-shape corpus profile on the pinned GPU.
internal static class GpuAdmission
{
    private const ulong GiB=1024UL*1024*1024;
    private const ulong MaximumOwnedBytes=24*GiB;
    private static bool Is4090(CudaDeviceInfo info)=>info.Name=="NVIDIA GeForce RTX 4090";

    internal static void RequireDevice(CudaDeviceInfo info,string uuid)
    {
        var existing5090=info.Name.IndexOf("5090",StringComparison.OrdinalIgnoreCase)>=0;
        var candidate4090=Is4090(info)&&info.ComputeCapabilityMajor==8&&info.ComputeCapabilityMinor==9;
        if(uuid.Length!=32||!uuid.All(char.IsAsciiHexDigit)||info.Uuid!=uuid||
            (!existing5090&&!candidate4090)||info.TotalMemoryBytes<30*GiB||info.DriverVersion<12080)
            throw new InvalidDataException("Pinned GPU requires RTX5090 or candidate RTX4090/CC8.9, at least30GiB, CUDA Driver12.8 and exact UUID; ordinary24GiB GPUs are not admitted.");
    }

    internal static void RequireMemory(ulong freeBytes,ulong ownedBytes)
    {
        if(ownedBytes is <1048576 or >MaximumOwnedBytes||freeBytes<ownedBytes+GiB)
            throw new InvalidDataException("Owned GPU ceiling24GiB and measured free memory with1GiB reserved overhead required.");
    }

    internal static int ProfileMaximumTokens(CudaDeviceInfo info,int planMaximum,int longestTokens)=>
        Is4090(info)?planMaximum:longestTokens;

    internal static void SaveProfileBinding(string output,CorpusRunPlan plan,CudaDeviceInfo info,ulong baselineFreeBytes,RunBudget budget)
    {
        if(!Is4090(info))return;
        RequireDevice(info,info.Uuid);RequireMemory(baselineFreeBytes,plan.MaximumOwnedBytes);
        var profilePath=Data.Within(output,"profile.json");var profile=Assets.Read(profilePath,CorpusWorkflowJson.Default.CorpusProfileReceipt);
        if(profile.MaximumTokensTested!=plan.MaximumTokens||profile.PeakOwnedBytes>plan.MaximumOwnedBytes)
            throw new InvalidDataException("Candidate4090 profile did not cover the full declared shape within the owned memory ceiling.");
        Data.WriteNew(Data.Within(output,"gpu-admission.json"),new GpuProfileBindingReceipt(1,"PASS_real_corpus_GPU_profile_binding_only",
            Data.FileHash(profilePath,budget),info,baselineFreeBytes,plan.MaximumOwnedBytes,profile.MaximumTokensTested),GpuAdmissionJson.Default.GpuProfileBindingReceipt);
    }

    internal static void RequireProfile(string profilePath,CorpusRunPlan plan,CorpusProfileReceipt profile,CudaDeviceInfo info,RunBudget budget)
    {
        if(!Is4090(info))return;
        var bindingPath=Data.Within(Path.GetDirectoryName(Path.GetFullPath(profilePath))!,"gpu-admission.json");
        var binding=Assets.Read(bindingPath,GpuAdmissionJson.Default.GpuProfileBindingReceipt);
        RequireBinding(binding,Data.FileHash(profilePath,budget),info,plan.MaximumOwnedBytes,plan.MaximumTokens,profile.MaximumTokensTested);
        if(profile.PeakOwnedBytes>plan.MaximumOwnedBytes)throw new InvalidDataException("Profile owned memory exceeded the fixed ceiling.");
    }

    private static void RequireBinding(GpuProfileBindingReceipt binding,string profileSha,CudaDeviceInfo info,ulong owned,int maximum,int tested)
    {
        if(binding.SchemaVersion!=1||binding.Status!="PASS_real_corpus_GPU_profile_binding_only"||binding.ProfileSha256!=profileSha||
            binding.Device!=info||binding.MaximumOwnedBytes!=owned||binding.MaximumTokensTested!=maximum||tested!=maximum||
            binding.BaselineFreeBytes>info.TotalMemoryBytes)
            throw new InvalidDataException("Candidate4090 requires an exact device/driver/architecture/memory-bound full-shape corpus profile.");
        RequireMemory(binding.BaselineFreeBytes,owned);
    }

    internal static int Checks(RunBudget budget)
    {
        var checks=0;var uuid=new string('1',32);
        var gpu=new CudaDeviceInfo("NVIDIA GeForce RTX 4090",12080,8,9,48*GiB){Uuid=uuid};
        void Accept(Action action){budget.Check();action();checks++;}
        void Reject(Action action)
        {
            budget.Check();var rejected=false;try{action();}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new InvalidDataException("GPU admission negative check accepted an incompatible candidate.");checks++;
        }
        Accept(()=>RequireDevice(gpu,uuid));
        Accept(()=>RequireDevice(gpu with{Name="NVIDIA GeForce RTX 5090",ComputeCapabilityMajor=12,ComputeCapabilityMinor=0,TotalMemoryBytes=32*GiB},uuid));
        Reject(()=>RequireDevice(gpu with{TotalMemoryBytes=24*GiB},uuid));
        Reject(()=>RequireDevice(gpu with{ComputeCapabilityMajor=9},uuid));
        Reject(()=>RequireDevice(gpu with{ComputeCapabilityMinor=0},uuid));
        Reject(()=>RequireDevice(gpu with{DriverVersion=12070},uuid));
        Reject(()=>RequireDevice(gpu with{Name="NVIDIA GeForce RTX 4090 D"},uuid));
        Reject(()=>RequireDevice(gpu,new('2',32)));
        Reject(()=>RequireDevice(gpu,"short"));
        Reject(()=>RequireDevice(gpu,new('g',32)));
        Accept(()=>RequireMemory(25*GiB,24*GiB));
        Reject(()=>RequireMemory(25*GiB-1,24*GiB));
        Reject(()=>RequireMemory(48*GiB,24*GiB+1));
        var sha=new string('3',64);var binding=new GpuProfileBindingReceipt(1,"PASS_real_corpus_GPU_profile_binding_only",sha,gpu,47*GiB,24*GiB,8192);
        Accept(()=>RequireBinding(binding,sha,gpu,24*GiB,8192,8192));
        Accept(()=>{
            var json=JsonSerializer.Serialize(binding,GpuAdmissionJson.Default.GpuProfileBindingReceipt);
            var read=JsonSerializer.Deserialize(json,GpuAdmissionJson.Default.GpuProfileBindingReceipt)??throw new InvalidDataException("GPU binding source-generated read failed.");
            RequireBinding(read,sha,gpu,24*GiB,8192,8192);
        });
        Reject(()=>RequireBinding(binding,sha,gpu with{DriverVersion=13000},24*GiB,8192,8192));
        Reject(()=>RequireBinding(binding,sha,gpu with{TotalMemoryBytes=40*GiB},24*GiB,8192,8192));
        Reject(()=>RequireBinding(binding,new('4',64),gpu,24*GiB,8192,8192));
        Reject(()=>RequireBinding(binding,sha,gpu,24*GiB,8192,7773));
        Reject(()=>RequireBinding(binding with{BaselineFreeBytes=25*GiB-1},sha,gpu,24*GiB,8192,8192));
        Reject(()=>RequireBinding(binding with{BaselineFreeBytes=49*GiB},sha,gpu,24*GiB,8192,8192));
        Accept(()=>{if(ProfileMaximumTokens(gpu,8192,7773)!=8192||ProfileMaximumTokens(gpu with{Name="NVIDIA GeForce RTX 5090"},8192,7773)!=7773)throw new InvalidDataException("Candidate/full shape and legacy shape boundaries differ.");});
        return checks;
    }
}

internal sealed record GpuProfileBindingReceipt(int SchemaVersion,string Status,string ProfileSha256,CudaDeviceInfo Device,
    ulong BaselineFreeBytes,ulong MaximumOwnedBytes,int MaximumTokensTested);

[JsonSerializable(typeof(GpuProfileBindingReceipt))]
[JsonSourceGenerationOptions(WriteIndented=false)]
internal partial class GpuAdmissionJson:JsonSerializerContext;
