using System.Security.Cryptography;

namespace Sezika.Tooling;

internal static class NativeGitHook
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellation)
    {
        if (args.Length != 2) throw new ArgumentException("Native pre-push hook expects remote name and URL.");
        var lfs = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_GIT_LFS") ?? throw new InvalidDataException("Explicit native LFS executable missing.");
        var logs = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_HOOK_LOG_ROOT") ?? throw new InvalidDataException("Explicit native hook evidence root missing.");
        var fixture = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_GIT_FIXTURE") == "1";
        var broker = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_GIT_BROKER");
        NativeGit.ValidateRemote(args[1], fixture);
        if (args[0].Length is < 1 or > 128 || args[0].Any(char.IsControl)) throw new InvalidDataException("Remote name bound differs.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(600));
        var budget = new Budget(600, null, timeout.Token);
        var bytes = new byte[1048577]; var count = 0;
        using var input = Console.OpenStandardInput();
        for (var read = 0; read < 1025; read++)
        {
            budget.Check();
            var size = await input.ReadAsync(bytes.AsMemory(count, Math.Min(1024, bytes.Length - count)), timeout.Token).ConfigureAwait(false);
            if (size == 0) break;
            count += size;
            if (count > 1048576) throw new InvalidDataException("LFS pre-push reference input exceeds1MiB.");
            if (read == 1024) throw new InvalidDataException("LFS pre-push read bound1025.");
        }
        try
        {
            ProcessExecutor.ValidateExecutable(lfs, budget);
            SafePaths.NoLinks(logs, budget);
            if (!Directory.Exists(logs)) throw new DirectoryNotFoundException("Native hook evidence root required.");
            var request = new ExecutionRequest
            {
                Executable = lfs, ArgumentList = ["pre-push", args[0], args[1]], StandardInput = bytes.AsSpan(0, count).ToArray(),
                WorkingDirectory = Environment.CurrentDirectory, OutputDirectory = Path.Combine(logs, "pre-push-" + Guid.NewGuid().ToString("N")),
                TimeoutSeconds = 590, CleanupSeconds = 10, MaximumOutputBytes = 4 * 1024 * 1024, BrokerExecutable = broker
            };
            var result = await ProcessExecutor.ExecuteAsync(request, budget, timeout.Token).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(request.StandardInput);
            return result.Status == "PASS" && result.ExitCode == 0 ? 0 : 1;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static void VerifyDirectory(string directory, string sha256, Budget budget)
    {
        TaskOperations.Sha(sha256); SafePaths.NoLinks(directory, budget);
        var files = 0;
        string[] hooks = ["applypatch-msg", "pre-applypatch", "post-applypatch", "pre-commit", "pre-merge-commit", "prepare-commit-msg", "commit-msg", "post-commit", "pre-rebase", "post-checkout", "post-merge", "pre-receive", "update", "proc-receive", "post-receive", "post-update", "reference-transaction", "push-to-checkout", "pre-auto-gc", "post-rewrite", "sendemail-validate", "fsmonitor-watchman", "p4-changelist", "p4-prepare-changelist", "p4-post-changelist", "p4-pre-submit"];
        foreach (var file in Directory.EnumerateFileSystemEntries(directory))
        {
            budget.Check();
            if (++files > 256 || Directory.Exists(file)) throw new InvalidDataException("Native hook payload entry bound or unexpected directory.");
            RegularFiles.Require(file, budget);
            var name = Path.GetFileName(file);
            if (hooks.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase) || TaskOperations.IsScript(name)) throw new InvalidDataException("Unreviewed executable/script Git hooks are not allowed.");
        }
        var hook = Path.Combine(Path.GetFullPath(directory), "pre-push");
        // An explicit native image replaces the historical shell pre-push only
        // after a separate fixture has accepted its LFS delegation contract.
        ProcessExecutor.ValidateExecutable(hook, budget);
        TaskOperations.VerifyFile(hook, new FileInfo(hook).Length, sha256, budget);
    }
}
