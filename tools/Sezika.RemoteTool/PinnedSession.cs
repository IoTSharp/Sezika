using System.Net;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal sealed class PinnedSession : IDisposable
{
    internal RemoteConfiguration Configuration { get; }
    internal SftpClient Sftp { get; }
    internal SshClient Ssh { get; }
    private readonly PrivateKeyFile? privateKey;
    private bool disposed;
    private bool pinRejected;

    internal PinnedSession(RemoteConfiguration configuration, RemoteSecretInput secret, bool useProxy = false)
    {
        RemotePaths.Validate(configuration); Configuration = configuration;
        AuthenticationMethod Authentication()
        {
            if (configuration.AuthenticationKind == "password")
            {
                if (secret.Password is null || secret.Password.Length is < 1 or > 8192 || secret.PrivateKey is not null)
                    throw new InvalidDataException("Password memory-channel input differs from authentication configuration.");
                return new PasswordAuthenticationMethod(configuration.UserName, secret.Password);
            }
            if (secret.PrivateKey is null || secret.PrivateKey.Length is < 32 or > 16384 || secret.Password is not null)
                throw new InvalidDataException("Private-key memory-channel input differs from authentication configuration.");
            return new PrivateKeyAuthenticationMethod(configuration.UserName, new RsaSha2KeySource(privateKey!));
        }
        if (configuration.AuthenticationKind == "private-key")
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(secret.PrivateKey ?? "");
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                privateKey = secret.Passphrase is null ? new PrivateKeyFile(stream) : new PrivateKeyFile(stream, secret.Passphrase);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        ConnectionInfo Info()
        {
            var info = useProxy ? new ConnectionInfo(configuration.HostName, configuration.Port, configuration.UserName, ProxyTypes.Http,
                "127.0.0.1", 7890, "", "", Authentication()) : new ConnectionInfo(configuration.HostName, configuration.Port, configuration.UserName, Authentication());
            info.Timeout = TimeSpan.FromSeconds(Math.Min(15, configuration.MaximumSeconds)); info.Encoding = new UTF8Encoding(false, true); return info;
        }
        Sftp = new SftpClient(Info()) { OperationTimeout = TimeSpan.FromSeconds(Math.Min(30, configuration.MaximumSeconds)), BufferSize = 65536 };
        Ssh = new SshClient(Info());
        Sftp.HostKeyReceived += (_, key) => { key.CanTrust = RemotePaths.MatchesPin(configuration.HostKeySha256, key.HostKey); pinRejected |= !key.CanTrust; };
        Ssh.HostKeyReceived += (_, key) => { key.CanTrust = RemotePaths.MatchesPin(configuration.HostKeySha256, key.HostKey); pinRejected |= !key.CanTrust; };
    }

    private sealed class RsaSha2KeySource : IPrivateKeySource
    {
        public IReadOnlyCollection<Renci.SshNet.Security.HostAlgorithm> HostKeyAlgorithms { get; }
        internal RsaSha2KeySource(PrivateKeyFile key)
        {
            if (key.HostKeyAlgorithms.Count is < 1 or > 16) throw new InvalidDataException("Private-key algorithm inventory cap differs.");
            // SSH.NET enumerates key algorithms in supplied order. Try standardized RSA-SHA2 before the legacy ssh-rsa alias.
            HostKeyAlgorithms = key.HostKeyAlgorithms.OrderBy(algorithm => algorithm.Name switch
            {
                "rsa-sha2-512" => 0, "rsa-sha2-256" => 1, "ssh-rsa" => 2, _ => 0
            }).ToArray();
        }
    }

    internal static async Task<PinnedSession> OpenAsync(RemoteConfiguration config, RemoteSecretInput secret, bool withSsh, Budget budget, CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            budget.Check(); var session = new PinnedSession(config, secret, attempt == 1);
            try { await session.ConnectSftpAsync(budget, cancellation); if (withSsh) await session.ConnectSshAsync(budget, cancellation); return session; }
            catch (Exception error) when (attempt == 0 && !session.pinRejected && !cancellation.IsCancellationRequested &&
                error is System.Net.Sockets.SocketException or SshConnectionException or SshOperationTimeoutException or OperationCanceledException)
            { session.Dispose(); budget.Check(); await Task.Delay(250, cancellation); }
            catch { session.Dispose(); throw; }
        }
        throw new IOException("SSH direct and one local HTTP proxy attempt failed.");
    }

    internal async Task ConnectSftpAsync(Budget budget, CancellationToken cancellation)
    {
        budget.Check(); using var deadline = Deadline(budget, cancellation, 15);
        await Sftp.ConnectAsync(deadline.Token); budget.Check();
    }
    internal async Task ConnectSshAsync(Budget budget, CancellationToken cancellation)
    {
        budget.Check(); using var deadline = Deadline(budget, cancellation, 15);
        await Ssh.ConnectAsync(deadline.Token); budget.Check();
    }
    internal static CancellationTokenSource Deadline(Budget budget, CancellationToken cancellation, int maximumSeconds)
    {
        budget.Check(); var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(.01, Math.Min(maximumSeconds, budget.RemainingSeconds)))); return deadline;
    }

    internal async Task<SftpFileAttributes?> AttributesAsync(string path, bool allowMissingLeaf, Budget budget, CancellationToken cancellation)
    {
        RemotePaths.Absolute(path); var parts = path[1..].Split('/'); var current = "/";
        // SSH.NET GetAttributes normalizes the path before lstat. Walk raw read-dir entries to reject link ancestors before normalization.
        for (var depth = 0; depth < parts.Length && depth < 64; depth++)
        {
            budget.Check(); var attributes = await RawChildAsync(current, parts[depth], allowMissingLeaf && depth == parts.Length - 1, budget, cancellation);
            if (attributes is null) return null;
            if (depth != parts.Length - 1 && !attributes.IsDirectory) throw new InvalidDataException("Remote path parent is not a directory.");
            if (depth == parts.Length - 1) return attributes;
            current = current == "/" ? current + parts[depth] : current + "/" + parts[depth];
        }
        throw new InvalidDataException("Remote path depth cap exceeded.");
    }

    internal async Task<UploadPreflightAttributes> UploadAttributesAsync(string destination, string partial, string state,
        Budget budget, CancellationToken cancellation)
    {
        string[] paths = [destination, partial, state];
        foreach (var path in paths)
        {
            budget.Check(); RemotePaths.Absolute(path);
            if (path == "/" || path[1..].Split('/').Length > 64) throw new InvalidDataException("Remote path depth cap exceeded.");
        }
        var separator = destination.LastIndexOf('/'); var parent = separator == 0 ? "/" : destination[..separator];
        if (partial[..partial.LastIndexOf('/')] != (separator == 0 ? "" : parent) ||
            state[..state.LastIndexOf('/')] != (separator == 0 ? "" : parent))
            throw new InvalidDataException("Upload preflight targets must have one exact parent.");
        // This snapshot belongs only to the upload preflight. Later I/O stages retain their fresh ancestry and SHA checks.
        if (parent != "/")
        {
            var attributes = await AttributesAsync(parent, false, budget, cancellation);
            if (attributes is null || !attributes.IsDirectory) throw new InvalidDataException("Remote path parent is not a directory.");
        }
        var names = paths.Select(path => path[(path.LastIndexOf('/') + 1)..]).ToArray();
        var found = await RawChildrenAsync(parent, names, true, budget, cancellation);
        return new(found[0], found[1], found[2]);
    }

    private async Task<SftpFileAttributes?> RawChildAsync(string verifiedParent, string name, bool allowMissing, Budget budget, CancellationToken cancellation)
    {
        var found = await RawChildrenAsync(verifiedParent, [name], allowMissing, budget, cancellation);
        return found[0];
    }

    private async Task<SftpFileAttributes?[]> RawChildrenAsync(string verifiedParent, string[] names, bool allowMissing,
        Budget budget, CancellationToken cancellation)
    {
        if (names.Length is < 1 or > 3 || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("Remote child lookup requires one to three distinct targets.");
        using var deadline = Deadline(budget, cancellation, 30); var found = new ISftpFile?[names.Length]; var entries = 0;
        await foreach (var item in Sftp.ListDirectoryAsync(verifiedParent, deadline.Token))
        {
            budget.Check(); if (++entries > Configuration.MaximumDirectoryEntries) throw new InvalidDataException("Remote directory entry cap exceeded.");
            for (var index = 0; index < names.Length; index++)
            {
                if (item.Name != names[index]) continue;
                if (found[index] is not null) throw new InvalidDataException("Remote directory contains duplicate target entries.");
                found[index] = item;
            }
        }
        var result = new SftpFileAttributes?[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            budget.Check();
            if (found[index] is null)
            {
                if (allowMissing) continue;
                throw new DirectoryNotFoundException("Explicit remote path ancestor/asset is absent.");
            }
            var attributes = found[index]!.Attributes;
            if (attributes.IsSymbolicLink || attributes.IsSocket || attributes.IsNamedPipe || attributes.IsBlockDevice || attributes.IsCharacterDevice ||
                (!attributes.IsRegularFile && !attributes.IsDirectory)) throw new InvalidDataException("Remote links, devices, sockets and FIFOs are rejected.");
            result[index] = attributes;
        }
        return result;
    }

    internal async Task CreateDirectoriesAsync(string path, Budget budget, CancellationToken cancellation)
    {
        RemotePaths.Absolute(path); var parts = path[1..].Split('/'); var parent = "/";
        for (var depth = 0; depth < parts.Length && depth < 64; depth++)
        {
            budget.Check(); var current = parent == "/" ? parent + parts[depth] : parent + "/" + parts[depth];
            // One raw ancestry walk: each parent was checked before descending, without restarting the walk from slash for every prefix.
            var attributes = await RawChildAsync(parent, parts[depth], true, budget, cancellation);
            if (attributes is not null) { if (!attributes.IsDirectory) throw new InvalidDataException("Remote output parent is not a directory."); parent = current; continue; }
            if (current != Configuration.Root && !RemotePaths.IsWithin(Configuration.Root, current)) throw new InvalidDataException("Creating remote directories outside the configured root is forbidden.");
            using var deadline = Deadline(budget, cancellation, 30); await Sftp.CreateDirectoryAsync(current, deadline.Token);
            var actual = await RawChildAsync(parent, parts[depth], false, budget, cancellation);
            if (actual is null || !actual.IsDirectory) throw new IOException("Created remote directory did not read back.");
            parent = current;
        }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { Ssh.Dispose(); }
        finally { try { Sftp.Dispose(); } finally { privateKey?.Dispose(); } }
    }
}

internal sealed record UploadPreflightAttributes(SftpFileAttributes? Destination, SftpFileAttributes? Partial, SftpFileAttributes? State);
