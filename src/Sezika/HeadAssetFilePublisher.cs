using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;

namespace Sezika;

// Pure bounded file publication shared by independent wire formats.
internal static class HeadAssetFilePublisher
{
    private const int MaxBytes = 128 * 1024;

    internal static void PublishNew(string path, byte[] bytes, string hash, Action check,
        TimeSpan cleanupDuration, HeadAssetPublicationHooks? hooks = null)
    {
        // Publish atomically so cancellation or process interruption cannot
        // create a truncated asset at the requested destination.
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        // Prepare owner storage before acquiring a file. After CreateNew
        // returns, registration is only assignment and cannot allocate.
        var owner = new HeadAssetTemporaryOwner(temporary);
        FileStream? file = null;
        var acquired = false;
        Stopwatch? cleanupClock = null;
        Exception? executionError = null, prefixError = null, disposalError = null, cleanupError = null;
        var published = false;
        void CheckCleanup()
        {
            if (cleanupClock!.Elapsed >= cleanupDuration)
                throw new IOException($"Head asset cleanup budget expired; retained task path: {temporary}");
        }
        try
        {
            check();
            // Disable the managed write buffer: a failed write is audited from
            // the real handle, rather than from the intended payload.
            file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1);
            acquired = true;
            owner.CaptureCreation(file);
            hooks?.Observe?.Invoke(HeadAssetPublicationStage.Created, temporary);
            for (var offset = 0; offset < bytes.Length; offset += 256)
            {
                check();
                file.Write(bytes, offset, Math.Min(256, bytes.Length - offset));
                if (offset == 0) hooks?.Observe?.Invoke(HeadAssetPublicationStage.AfterPrefix, temporary);
            }
            file.Flush(flushToDisk: true);
            hooks?.Observe?.Invoke(HeadAssetPublicationStage.AfterFlush, temporary);
            check();
        }
        catch (Exception error) { executionError = error; }
        finally
        {
            // One independent reserve covers prefix capture, disposal and
            // cleanup, regardless of cancellation or the expired save budget.
            cleanupClock = Stopwatch.StartNew();
            if (acquired)
            {
                try { owner.CapturePrefix(file!, CheckCleanup); }
                catch (Exception error) { prefixError = error; }
                try { file!.Dispose(); }
                catch (Exception error) { disposalError = error; }
            }
        }
        try
        {
            if (executionError is null && prefixError is null && disposalError is null)
            {
                hooks?.Observe?.Invoke(HeadAssetPublicationStage.BeforePublish, temporary);
                check();
                if (owner.Length != bytes.Length || !owner.Sha256.Equals(hash, StringComparison.Ordinal))
                    throw new IOException($"Head asset written bytes differ from the payload; retained path: {temporary}");
                owner.Verify(check);
                check();
                File.Move(temporary, destination); // No overwrite, including an existing target.
                published = true;
            }
        }
        catch (Exception error) { executionError = error; }
        finally
        {
            if (!published && acquired)
            {
                try
                {
                    hooks?.Observe?.Invoke(HeadAssetPublicationStage.BeforeCleanup, temporary);
                    CheckCleanup();
                    owner.DeleteVerified(CheckCleanup);
                }
                catch (Exception error) { cleanupError = error; }
            }
        }
        var errors = new[] { executionError, prefixError, disposalError, cleanupError }
            .Where(error => error is not null).Cast<Exception>().ToArray();
        if (errors.Length == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Length > 1)
            throw new AggregateException($"Head asset publication/cleanup failed; inspect retained task path: {temporary}", errors);
    }

    private sealed class HeadAssetTemporaryOwner(string path)
    {
        private readonly string _path = Path.GetFullPath(path);
        private DateTime _creationUtc;
        private LinuxFileIdentity _linuxIdentity;
        private bool _creationKnown, _prefixKnown;
        internal long Length { get; private set; }
        internal string Sha256 { get; private set; } = string.Empty;

        internal void CaptureCreation(FileStream file)
        {
            var info = Inspect();
            if (OperatingSystem.IsLinux())
            {
                _linuxIdentity = LinuxFileIdentity.ReadHandle(file.SafeFileHandle);
                if (LinuxFileIdentity.ReadPath(_path) != _linuxIdentity)
                    throw new IOException($"Head asset path and opened file differ; retained path: {_path}");
            }
            else _creationUtc = info.CreationTimeUtc;
            _creationKnown = true;
        }

        internal void CapturePrefix(FileStream file, Action check)
        {
            check();
            RequireCreation(file);
            var length = file.Length;
            if (length is < 0 or > MaxBytes)
                throw new IOException($"Head asset actual prefix is outside its bound; retained path: {_path}");
            var hash = HashHandle(file, length, check);
            RequireCreation(file);
            if (file.Length != length)
                throw new IOException($"Head asset prefix changed during capture; retained path: {_path}");
            Length = length;
            Sha256 = hash;
            _prefixKnown = true;
        }

        internal void Verify(Action check)
        {
            check();
            if (!_prefixKnown)
                throw new IOException($"Head asset actual prefix ownership is unknown; retained path: {_path}");
            RequireCreation();
            using var lease = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1);
            if (lease.Length != Length || !HashHandle(lease, Length, check).Equals(Sha256, StringComparison.Ordinal))
                throw new IOException($"Head asset prefix ownership changed; retained path: {_path}");
            RequireCreation();
            check();
        }

        internal void DeleteVerified(Action check)
        {
            check();
            if (!File.Exists(_path)) return;
            Verify(check);
            // Recheck metadata immediately before the path operation. These
            // synchronous local-file checks cooperate with the wall budget;
            // they do not offer OS-level isolation from hostile directory races.
            RequireCreation();
            check();
            File.Delete(_path);
            check();
        }

        private FileInfo Inspect()
        {
            if (!Path.GetFullPath(_path).Equals(_path, StringComparison.Ordinal))
                throw new IOException($"Head asset path ownership changed; retained path: {_path}");
            var info = new FileInfo(_path);
            info.Refresh();
            if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Head asset path identity is unknown; retained path: {_path}");
            return info;
        }

        private void RequireCreation(FileStream? file = null)
        {
            var info = Inspect();
            var same = OperatingSystem.IsLinux()
                ? LinuxFileIdentity.ReadPath(_path) == _linuxIdentity &&
                    (file is null || LinuxFileIdentity.ReadHandle(file.SafeFileHandle) == _linuxIdentity)
                : info.CreationTimeUtc == _creationUtc;
            if (!_creationKnown || !same)
                throw new IOException($"Head asset creation identity changed; retained path: {_path}");
        }

        private static string HashHandle(FileStream file, long length, Action check)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[4096];
            file.Position = 0;
            long consumed = 0;
            for (var block = 0; consumed < length && block < MaxBytes / 4096; block++)
            {
                check();
                var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - consumed));
                if (count == 0) throw new EndOfStreamException("Head asset prefix ended during ownership capture.");
                hash.AppendData(buffer, 0, count);
                consumed += count;
            }
            if (consumed != length) throw new IOException("Head asset prefix read exceeded its block bound.");
            check();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

}
