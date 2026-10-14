using System.Net;
using System.Security.Cryptography;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class RemotePaths
{
    internal static string Absolute(string path)
    {
        if (path.Length is < 2 or > 1024 || path[0] != '/' || path.EndsWith('/') || path.Contains('\\')) throw new InvalidDataException("Portable absolute POSIX path required.");
        _ = SafePaths.Relative(path[1..]); return path;
    }
    internal static string Within(string root, string relative)
    {
        root = Absolute(root); relative = SafePaths.Relative(relative);
        if (relative.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Git metadata transport is not a generic file operation.");
        return root + "/" + relative;
    }
    internal static bool IsWithin(string root, string path) => Absolute(path).StartsWith(Absolute(root) + "/", StringComparison.Ordinal);
    internal static void Sha(string value) => AssetTransport.ValidateSha(value);
    internal static void Identity(string value)
    {
        if (value.Length is < 8 or > 64 || value.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
            throw new InvalidDataException("Explicit portable request/operation identity required.");
    }
    internal static byte[] Pin(string value)
    {
        if (!value.StartsWith("SHA256:", StringComparison.Ordinal) || value.Length is < 49 or > 51) throw new InvalidDataException("Explicit OpenSSH SHA256 host-key pin required.");
        var tail = value[7..]; var bytes = Convert.FromBase64String(tail + new string('=', (4 - tail.Length % 4) % 4));
        if (bytes.Length != 32) throw new InvalidDataException("Host-key fingerprint length differs."); return bytes;
    }
    internal static bool MatchesPin(string pin, byte[] key)
    {
        var expected = Pin(pin); return CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(key));
    }
    internal static void Validate(RemoteConfiguration config)
    {
        if (config.SchemaVersion != 1 || config.Port is < 1 or > 65535 || config.HostName.Length is < 1 or > 253 ||
            config.HostName.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '/' or '\\' or '@' or '?') ||
            Uri.CheckHostName(config.HostName) == UriHostNameType.Unknown || config.UserName.Length is < 1 or > 128 ||
            config.UserName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '.')) || config.MaximumSeconds is < 1 or > 1800 ||
            config.MaximumDirectoryEntries is < 1 or > 4096 || config.MaximumFileBytes is < 1 or > AssetTransport.MaximumFileBytes ||
            config.AuthenticationSource is not ("stdin" or "credential-manager") || config.AuthenticationKind is not ("password" or "private-key") ||
            config.CredentialEncoding is not ("utf8" or "utf16le") || config.AllowedExecutables.Length is < 1 or > 64)
            throw new InvalidDataException("Remote connection/authentication/resource scope invalid.");
        _ = Pin(config.HostKeySha256); _ = Absolute(config.Root); _ = Absolute(config.WorkerPath); Sha(config.WorkerSha256);
        if (!IsWithin(config.Root, config.WorkerPath)) throw new InvalidDataException("Fixed worker escaped explicit remote root.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pin in config.AllowedExecutables)
        {
            if (!IsWithin(config.Root, pin.Path) || !paths.Add(pin.Path)) throw new InvalidDataException("Native executable allowlist escaped root or contains duplicates.");
            Sha(pin.Sha256);
        }
        if (config.WorkerBundle is not null) BundleProof.ValidateBinding(config.WorkerBundle, config.WorkerPath, config.WorkerSha256, config.Root,
            new Budget(10, null, CancellationToken.None), nativeWorker: true);
        if (config.AllowedPayloadBundles is not null)
        {
            if (config.AllowedPayloadBundles.Length is < 1 or > 64) throw new InvalidDataException("Configured code/runtime bundle count differs.");
            var bundleBudget = new Budget(10, null, CancellationToken.None); var bundlePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bundle in config.AllowedPayloadBundles)
            {
                bundleBudget.Check(); BundleProof.Validate(bundle, bundleBudget);
                var path = bundle.Manifest.Directory + "/" + bundle.Manifest.EntryPoint;
                if (!bundlePaths.Add(path) || !config.AllowedExecutables.Any(p => p.Path == path && p.Sha256 == bundle.Manifest.Files.Single(f => f.RelativePath == bundle.Manifest.EntryPoint).Sha256))
                    throw new InvalidDataException("Configured complete payload bundle differs from the explicit executable allowlist.");
            }
            if (bundlePaths.Count != config.AllowedExecutables.Length) throw new InvalidDataException("Every allowed executable needs exactly one complete code/runtime bundle.");
        }
        if (config.AuthenticationSource == "credential-manager" && (string.IsNullOrWhiteSpace(config.CredentialTarget) || config.CredentialTarget.Length > 256))
            throw new InvalidDataException("Credential Manager requires an explicit stored target.");
    }
    internal static string Command(RemoteConfiguration config, string requestPath, string requestSha256)
    {
        Validate(config); _ = Absolute(requestPath); Sha(requestSha256);
        if (!IsWithin(config.Root, requestPath)) throw new InvalidDataException("Worker request escaped explicit root.");
        // Every component is validated portable ASCII. No user-provided command text, interpolation or shell operator enters SSH exec.
        return config.WorkerPath + " worker --request " + requestPath + " --sha256 " + requestSha256;
    }
}
