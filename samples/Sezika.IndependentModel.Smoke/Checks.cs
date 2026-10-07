using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.IndependentSmoke;

internal static class Checks
{
    internal static void Run(int maximum, Budget budget)
    {
        var root = ArtifactIO.Safe(Path.Combine(Path.GetTempPath(), "sezika-independent-aot-check-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); var owned = new List<(string Path, FileId Id, string Hash)>();
        void Reject(Action action) { bool rejected = false; try { action(); } catch (Exception error) when (error is InvalidDataException or OperationCanceledException or JsonException or IOException) { rejected = true; } ArtifactIO.Require(rejected, "Negative check unexpectedly passed"); }
        Action[] checks = [
            () => { var head = Diagnostic.Create(budget); var actual = head.Asset.Head.CopyWeights(); var actualBytes = new byte[actual.Length * 4]; ArtifactIO.Require(BitConverter.IsLittleEndian, "Self-check host requires little endian"); Buffer.BlockCopy(actual, 0, actualBytes, 0, actualBytes.Length); ArtifactIO.Require(head.Descriptor.MemoryBytes == 9216 && head.Descriptor.HiddenSize == 768 && actual.Distinct().Count() > 2 && ArtifactIO.Hash(actualBytes) == head.Descriptor.MemorySha256, "Actual copied head memory identity differs"); Array.Clear(actual); Array.Clear(actualBytes); },
            () => { var a = Diagnostic.Create(budget); var b = Diagnostic.Create(budget); ArtifactIO.Require(a.Descriptor == b.Descriptor && a.Asset.Head.CopyWeights().SequenceEqual(b.Asset.Head.CopyWeights()), "Diagnostic initialization is not deterministic"); },
            () => { var value = Diagnostic.Create(budget).Descriptor; var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SmokeJson.Default.DiagnosticDescriptor); ArtifactIO.JsonGuard(bytes, budget); ArtifactIO.Require(JsonSerializer.Deserialize(bytes, SmokeJson.Default.DiagnosticDescriptor) == value, "Source-generated descriptor roundtrip failed"); },
            () => { Reject(() => ArtifactIO.JsonGuard(Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}"), budget)); Reject(() => ArtifactIO.JsonGuard(Encoding.UTF8.GetBytes("{\"a\":1e400}"), budget)); },
            () => { Reject(() => Options.Parse(["run", "--backend", "cuda", "--backend", "cpu"])); Reject(() => Options.Parse(["probe", "--device", "NaN"])); Reject(() => Options.Parse(["probe", "--output", "x", "--timeout-seconds", "31"])); },
            () => { Reject(() => ArtifactIO.Relative(root, "../escape")); Reject(() => ArtifactIO.Relative(root, root)); },
            () => { using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool rejected = false; try { IndependentModelLoader.Load(Path.Combine(root, "not-installed"), cancelled.Token); } catch (OperationCanceledException) { rejected = true; } ArtifactIO.Require(rejected, "Production loader ignored precancel"); },
            () => {
                var emptyPath = Path.Combine(root, "empty-owned.tmp"); FileId emptyId;
                using (var empty = new FileStream(emptyPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) emptyId = ArtifactIO.Id(empty);
                ArtifactIO.RemoveOwned(emptyPath, emptyId, ArtifactIO.Hash([])); ArtifactIO.Require(!File.Exists(emptyPath), "Owned zero-byte partial not reclaimed");
                var path = Path.Combine(root, "output.json"); var data = Encoding.UTF8.GetBytes("{\"fixture_only\":true}");
                var published = ArtifactIO.Publish(path, data, budget); using (var lease = ArtifactIO.Lease(path)) owned.Add((path, ArtifactIO.Id(lease), published.Sha256));
                Reject(() => ArtifactIO.Publish(path, data, budget)); ArtifactIO.Require(File.ReadAllBytes(path).SequenceEqual(data), "No-overwrite output changed");
                using var isolated = new Budget(5); var cancelledPath = Path.Combine(root, "cancelled.json");
                Reject(() => ArtifactIO.Publish(cancelledPath, data, isolated, isolated.Cancel)); ArtifactIO.Require(!File.Exists(cancelledPath), "Actual Move cancellation retained success output");
            }
        ];
        try { for (var i = 0; i < Math.Min(maximum, checks.Length); i++) { budget.Check(); checks[i](); Console.WriteLine($"PASS {i + 1}/{maximum}: fixture_only=true; model_forward=false; GPU=false"); } }
        finally
        {
            var cleanupClock = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < owned.Count && i < 8; i++) { ArtifactIO.Require(cleanupClock.Elapsed < TimeSpan.FromSeconds(5), "Fixture cleanup deadline"); ArtifactIO.RemoveOwned(owned[i].Path, owned[i].Id, owned[i].Hash); }
            try { Directory.Delete(root, recursive: false); } catch (Exception error) { throw new IOException("Fixture cleanup retained: " + root, error); }
        }
    }
}
