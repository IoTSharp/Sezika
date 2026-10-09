using System.Diagnostics;
using System.Runtime.InteropServices;
using Sezika;

namespace Sezika.Tests;

internal static class LinuxPublicationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(Marshal.SizeOf<LinuxFileNative.StatxBuffer>() == 256 &&
            Marshal.OffsetOf<LinuxFileNative.StatxBuffer>(nameof(LinuxFileNative.StatxBuffer.Inode)).ToInt32() == 32 &&
            Marshal.OffsetOf<LinuxFileNative.StatxBuffer>(nameof(LinuxFileNative.StatxBuffer.DeviceMajor)).ToInt32() == 136,
            "statx fixed native ABI size and identity offsets");
        if (!OperatingSystem.IsLinux()) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var clock = Stopwatch.StartNew();
        void Check() { deadline.Token.ThrowIfCancellationRequested(); }
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sezika-linux-publication-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var owned = new Dictionary<string,LinuxFileIdentity>(StringComparer.Ordinal);
        void Track(string path) { Check(); if(owned.Count>=16)throw new InvalidOperationException("fixture file cap"); owned[path]=LinuxFileIdentity.ReadPath(path); }
        var state = new MarkerTrainingState(new MarkerFeatureIdentity("sezika/linux-publication-fixture",new string('A',64),new string('B',64),new string('C',64),2),new string('F',64),17,0.001f,0,0,[0.1f,0.2f,0.3f,0.4f,0.5f,0.6f]);
        try
        {
            var path=Path.Combine(root,"identity.bin");
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1))
            {
                Track(path); var before=LinuxFileIdentity.ReadHandle(file.SafeFileHandle);
                file.Write(new byte[256]); file.Flush(true);
                check(before==LinuxFileIdentity.ReadHandle(file.SafeFileHandle) && before==LinuxFileIdentity.ReadPath(path),"Linux identity survives real writes and matches handle/path");
            }
            var target=Path.Combine(root,"roundtrip.asset");
            var hash=IndependentMarkerHeadAssetStore.SaveNew(target,state,deadline.Token);Track(target);
            var loaded=IndependentMarkerHeadAssetStore.Load(target,state.Identity,state.FeatureSha256,hash,deadline.Token);
            check(loaded.Head.CopyWeights().SequenceEqual(state.Head.CopyWeights()),"Linux atomic head publication and disk roundtrip");
            var rejected=Path.Combine(root,"cancelled.asset");string? temporary=null;
            using(var cancelled=new CancellationTokenSource())
            {
                try
                {
                    IndependentMarkerHeadAssetStore.SaveForPublicationChecks(rejected,state,new HeadAssetPublicationHooks {Observe=(stage,p)=> {if(stage==HeadAssetPublicationStage.Created)temporary=p;if(stage==HeadAssetPublicationStage.AfterPrefix)cancelled.Cancel();}},cancelled.Token);
                    throw new InvalidOperationException("cancellation accepted");
                }
                catch(OperationCanceledException){check(temporary is not null && !File.Exists(temporary) && !File.Exists(rejected),"Linux cancellation deletes only its verified partial prefix");}
            }
            rejected=Path.Combine(root,"replacement.asset");var replacementRetained=false;
            try
            {
                IndependentMarkerHeadAssetStore.SaveForPublicationChecks(rejected,state,new HeadAssetPublicationHooks {Observe=(stage,p)=>
                {
                    Check();if(stage!=HeadAssetPublicationStage.BeforePublish)return;
                    var old=LinuxFileIdentity.ReadPath(p);var stash=p+".original";
                    File.Move(p,stash);Track(stash);File.Copy(stash,p,false);Track(p);
                    check(LinuxFileIdentity.ReadPath(p)!=old,"identical-byte replacement has a different inode identity");temporary=p;
                }},deadline.Token);
            }
            catch(IOException){replacementRetained=true;}
            catch(AggregateException){replacementRetained=true;}
            check(replacementRetained && temporary is not null && File.Exists(temporary) && !File.Exists(rejected),"Linux identical-byte replacement is retained and never published or deleted");
        }
        finally
        {
            var cleanup=Stopwatch.StartNew();
            foreach(var entry in owned)
            {
                if(cleanup.Elapsed.TotalSeconds>=5)throw new IOException("fixture cleanup deadline");
                if(File.Exists(entry.Key))
                {
                    if(Path.GetDirectoryName(entry.Key)!=root || LinuxFileIdentity.ReadPath(entry.Key)!=entry.Value)throw new IOException("fixture ownership changed; retained "+entry.Key);
                    File.Delete(entry.Key);
                }
            }
            if(Directory.EnumerateFileSystemEntries(root).Take(1).Any())throw new IOException("fixture unknown files retained "+root);
            Directory.Delete(root,false);
            Console.WriteLine($"linux_publication_cleanup_returned=true elapsed_seconds={clock.Elapsed.TotalSeconds:R}");
        }
    }
}
