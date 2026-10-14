using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class BundleProof
{
    internal const int MaximumFiles = 512;
    internal const long MaximumBytes = 512L * 1024 * 1024;

    internal static BundlePin Capture(string sourceDirectory, string remoteDirectory, string entryPoint, RuntimeBundleKind runtime, Budget budget)
    {
        var manifest = new RuntimeBundleManifest(1, remoteDirectory, entryPoint, runtime, Inventory(sourceDirectory, budget));
        var result = new BundlePin(manifest, Hash(manifest)); Validate(result, budget); return result;
    }

    internal static string Hash(RuntimeBundleManifest manifest) => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(manifest)));

    internal static byte[] CanonicalBytes(RuntimeBundleManifest manifest)
    {
        // A manifest crosses Windows/Linux boundaries. Indented JSON must use explicit LF;
        // the shared context's platform-default newline is appropriate only for local reports.
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            JsonSerializer.Serialize(writer, manifest, RemoteJsonContext.Default.RuntimeBundleManifest); writer.Flush();
        }
        return output.WrittenSpan.ToArray();
    }

    internal static void Validate(BundlePin pin, Budget budget)
    {
        budget.Check(); RemotePaths.Sha(pin.Sha256); var manifest = pin.Manifest; RemotePaths.Absolute(manifest.Directory);
        if (manifest.SchemaVersion != 1 || manifest.Files.Length is < 1 or > MaximumFiles || !Enum.IsDefined(manifest.Runtime) ||
            manifest.EntryPoint.Contains('/') || SafePaths.Relative(manifest.EntryPoint) != manifest.EntryPoint || Hash(manifest) != pin.Sha256)
            throw new InvalidDataException("Bundle schema/count/entry/complete-manifest SHA differs.");
        long bytes = 0; string? previous = null; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            budget.Check(); RemotePaths.Sha(file.Sha256); var path = SafePaths.Relative(file.RelativePath);
            if (path.Split('/').Length > 8 || path.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
                file.Bytes < 0 || (bytes = checked(bytes + file.Bytes)) > MaximumBytes || !names.Add(path) ||
                previous is not null && string.CompareOrdinal(previous, path) >= 0)
                throw new InvalidDataException("Bundle file path/depth/byte/sorted exact inventory differs.");
            previous = path;
        }
        if (!names.Contains(manifest.EntryPoint)) throw new InvalidDataException("Bundle entry executable absent from its complete inventory.");
        if (manifest.Runtime == RuntimeBundleKind.NativeAotImage)
        {
            if (manifest.Files.Any(f => f.RelativePath != manifest.EntryPoint && f.RelativePath != manifest.EntryPoint + ".dbg" && f.RelativePath != manifest.EntryPoint + ".pdb"))
                throw new InvalidDataException("NativeAOT bundle must contain its one native image and optional debug symbols only.");
        }
        else
        {
            var entry = manifest.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? manifest.EntryPoint[..^4] : manifest.EntryPoint;
            foreach (var required in new[] { entry + ".dll", entry + ".deps.json", entry + ".runtimeconfig.json", "System.Private.CoreLib.dll", "libcoreclr.so", "libhostpolicy.so" })
                if (!names.Contains(required)) throw new InvalidDataException("Self-contained Linux .NET bundle is missing required code/runtime metadata.");
        }
    }

    internal static void ValidateBinding(BundlePin? pin, string executable, string sha256, string root, Budget budget, bool nativeWorker)
    {
        if (pin is null) throw new InvalidDataException("Complete code/runtime bundle binding is required for worker dispatch.");
        Validate(pin, budget);
        if (!RemotePaths.IsWithin(root, pin.Manifest.Directory) || pin.Manifest.Directory + "/" + pin.Manifest.EntryPoint != executable ||
            pin.Manifest.Files.Single(f => f.RelativePath == pin.Manifest.EntryPoint).Sha256 != sha256 ||
            nativeWorker && pin.Manifest.Runtime != RuntimeBundleKind.NativeAotImage)
            throw new InvalidDataException("Bundle root/entry/native-worker/code identity differs.");
    }

    internal static void VerifyLocal(BundlePin pin, Budget budget, string? explicitTestDirectory = null)
    {
        Validate(pin, budget); var actual = Inventory(explicitTestDirectory ?? pin.Manifest.Directory, budget);
        if (!actual.SequenceEqual(pin.Manifest.Files)) throw new InvalidDataException("Actual complete code/runtime directory differs from the frozen bundle inventory.");
    }

    private static BundleFile[] Inventory(string directory, Budget budget)
    {
        directory = Path.GetFullPath(directory); SafePaths.NoLinks(directory, budget);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Bundle source directory absent.");
        var files = new List<BundleFile>(); var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((directory, 0)); long bytes = 0; var entries = 0;
        for (var directoryIndex = 0; directoryIndex <= MaximumFiles; directoryIndex++)
        {
            budget.Check(); if (pending.Count == 0) return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
            var current = pending.Dequeue();
            foreach (var item in Directory.EnumerateFileSystemEntries(current.Path))
            {
                budget.Check(); if (++entries > MaximumFiles * 2) throw new InvalidDataException("Bundle total directory-entry cap exceeded.");
                SafePaths.NoLinks(item, budget);
                if (Directory.Exists(item))
                {
                    if (current.Depth >= 7 || pending.Count >= MaximumFiles) throw new InvalidDataException("Bundle directory depth/count cap exceeded.");
                    pending.Enqueue((item, current.Depth + 1)); continue;
                }
                RegularFiles.Require(item, budget); var length = new FileInfo(item).Length;
                if (files.Count >= MaximumFiles || (bytes = checked(bytes + length)) > MaximumBytes) throw new InvalidDataException("Bundle complete file count/byte cap exceeded.");
                var relative = SafePaths.Relative(Path.GetRelativePath(directory, item).Replace('\\', '/'));
                var sha = AssetTransport.FileHash(item, budget);
                if (new FileInfo(item).Length != length) throw new InvalidDataException("Bundle file changed during complete SHA read.");
                files.Add(new(relative, length, sha));
            }
        }
        throw new InvalidDataException("Bundle directory iteration cap exceeded.");
    }
}
