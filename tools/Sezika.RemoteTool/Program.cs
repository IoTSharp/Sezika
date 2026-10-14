using System.Text.Json;
using Renci.SshNet.Common;
using Sezika.RemoteTool;
using Sezika.Tooling;

return await RemoteCommand.RunAsync(args);

internal static class RemoteCommand
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 3 && arguments[0] == "process" && arguments[1] is "broker" or "watchdog")
            return arguments[1] == "broker" ? ProcessExecutor.RunBroker(arguments[2], CancellationToken.None) : ProcessExecutor.RunWatchdog(arguments[2], CancellationToken.None);
        if (arguments.Length == 0 || arguments is ["--help"])
        {
            Console.WriteLine("Sezika.RemoteTool upload|download --connection <no-secret-json> --request <typed-transfer-json> --local <file> --output <new-receipt-json>");
            Console.WriteLine("Sezika.RemoteTool dispatch --connection <no-secret-json> --request <typed-worker-json> --logs <new-owned-dir> --output <new-receipt-json>");
            Console.WriteLine("Sezika.RemoteWorker worker --request <absolute-posix-json> --sha256 <trusted-sha256> (separate NativeAOT image)");
            Console.WriteLine("Sezika.RemoteTool checks --output <new-owned-directory> --maximum-checks <1..64>");
            Console.WriteLine("Sezika.RemoteTool integration-checks --connection <literal-loopback-fixture-json> --output <new-owned-directory> --maximum-checks <1..32> [--worker-request <native-fixture-request>]");
            Console.WriteLine("Sezika.RemoteTool bundle --directory <frozen-local-dir> --remote-directory <absolute-posix-dir> --entry <filename> --runtime <native-aot|self-contained-dotnet> --output <new-manifest-json>");
            Console.WriteLine("SSH/SFTP JIT .NET10; explicit host-key pin; credentials only from bounded stdin or an explicitly named secure store. Fixed C# native worker; no arbitrary shell/script command.");
            return 0;
        }
        string? output = null; var outputAvailable = false;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); }; Console.CancelKeyPress += cancel;
        try
        {
            StartupGate.Wait(cancellation.Token);
            if (arguments.Length is < 3 or > 25 || arguments.Length % 2 != 1 || arguments.Any(a => a.Length > 32768)) throw new ArgumentException("Remote argument count/text cap exceeded.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < arguments.Length; index += 2)
                if (!arguments[index].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(arguments[index], arguments[index + 1]) || string.IsNullOrWhiteSpace(arguments[index + 1])) throw new ArgumentException("Remote duplicate/empty option.");
            string Required(string name) => options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing option " + name);
            if (arguments[0] == "bundle")
            {
                if (options.Count != 5 || options.Keys.Any(key => key is not ("--directory" or "--remote-directory" or "--entry" or "--runtime" or "--output")))
                    throw new ArgumentException("Read-only bundle producer accepts only explicit local/remote/entry/runtime/output options.");
                cancellation.CancelAfter(TimeSpan.FromSeconds(30)); var bundleBudget = new Budget(30, null, cancellation.Token);
                var localDirectory = Path.GetFullPath(Required("--directory")); var target = Path.GetFullPath(Required("--output"));
                SafePaths.NoLinks(localDirectory, bundleBudget); SafePaths.NoLinks(target, bundleBudget);
                if (Path.Exists(target) || !Directory.Exists(Path.GetDirectoryName(target)) ||
                    target.StartsWith(Path.TrimEndingDirectorySeparator(localDirectory) + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException("Bundle manifest requires a fresh file outside its frozen source directory.");
                var runtime = Required("--runtime") switch
                {
                    "native-aot" => RuntimeBundleKind.NativeAotImage,
                    "self-contained-dotnet" => RuntimeBundleKind.SelfContainedDotnet,
                    _ => throw new ArgumentException("Explicit bundle runtime kind required.")
                };
                var bundle = BundleProof.Capture(localDirectory, Required("--remote-directory"), Required("--entry"), runtime, bundleBudget);
                BundleProof.VerifyLocal(bundle, bundleBudget, localDirectory);
                AssetTransport.WriteJson(target, bundle, RemoteJsonContext.Default.BundlePin, bundleBudget);
                Console.WriteLine("PASS_prepared_complete_local_bundle_manifest;files=" + bundle.Manifest.Files.Length + ";remote-transfer/native-execution=NOT_RUN");
                return 0;
            }
            if (arguments[0] == "worker")
            {
                if (options.Count != 2 || !options.ContainsKey("--request") || !options.ContainsKey("--sha256")) throw new ArgumentException("Worker accepts only the typed request and trusted SHA.");
                return await RemoteWorker.RunAsync(Required("--request"), Required("--sha256"), cancellation.Token);
            }
            if (arguments[0] == "checks")
            {
                if (options.Count != 2 || !int.TryParse(Required("--maximum-checks"), out var maximum) || maximum is < 1 or > 64) throw new ArgumentException("Remote checks require explicit 1..64 item cap.");
                return RemoteChecks.Run(Required("--output"), maximum, cancellation.Token);
            }
            if (arguments[0] == "integration-checks")
            {
                if (options.Keys.Any(key => key is not ("--connection" or "--output" or "--maximum-checks" or "--worker-request")) ||
                    !int.TryParse(Required("--maximum-checks"), out var maximum) || maximum is < 1 or > 32) throw new ArgumentException("Actual loopback integration option/item cap differs.");
                var integrationAdmission = new Budget(15, null, cancellation.Token);
                var loopbackConfig = AssetTransport.ReadJson(Required("--connection"), RemoteJsonContext.Default.RemoteConfiguration, integrationAdmission); RemotePaths.Validate(loopbackConfig);
                cancellation.CancelAfter(TimeSpan.FromSeconds(Math.Min(300, loopbackConfig.MaximumSeconds)));
                var fixtureInvocation = await ReadInvocationAsync(integrationAdmission, cancellation.Token);
                WorkerRequest? templateWorker = options.TryGetValue("--worker-request", out var file) ? AssetTransport.ReadJson(file, RemoteJsonContext.Default.WorkerRequest, integrationAdmission) : null;
                return await IntegrationChecks.RunAsync(loopbackConfig, fixtureInvocation, Required("--output"), maximum, templateWorker, cancellation.Token);
            }
            var allowed = arguments[0] switch
            {
                "upload" or "download" => new[] { "--connection", "--request", "--local", "--output" },
                "dispatch" => ["--connection", "--request", "--logs", "--output"],
                _ => throw new ArgumentException("Unknown remote mode.")
            };
            if (options.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("Unsupported remote option.");
            var admission = new Budget(15, null, cancellation.Token);
            output = Path.GetFullPath(Required("--output")); SafePaths.NoLinks(output, admission);
            if (Path.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output))) throw new IOException("Receipt requires a fresh file in an existing safe directory."); outputAvailable = true;
            var config = AssetTransport.ReadJson(Required("--connection"), RemoteJsonContext.Default.RemoteConfiguration, admission); RemotePaths.Validate(config);
            cancellation.CancelAfter(TimeSpan.FromSeconds(config.MaximumSeconds)); var budget = new Budget(config.MaximumSeconds, null, cancellation.Token);
            WorkerRequest? worker = arguments[0] == "dispatch" ? AssetTransport.ReadJson(Required("--request"), RemoteJsonContext.Default.WorkerRequest, budget) : null;
            RemoteInvocationInput? invocation = null;
            if (config.AuthenticationSource == "stdin" || worker?.StandardInputBytes > 0) invocation = await ReadInvocationAsync(budget, cancellation.Token);
            var input = invocation?.StandardInput ?? [];
            RemoteSecretInput secret;
            if (config.AuthenticationSource == "credential-manager")
            {
                using var stored = WindowsCredentialStore.Read(config.CredentialTarget!);
                if (stored.UserName.Length != 0 && stored.UserName != config.UserName) throw new InvalidDataException("Secure-store SSH username differs from the explicit connection identity.");
                var value = stored.Text(config.CredentialEncoding);
                secret = config.AuthenticationKind == "password" ? new(Password: value) : new(PrivateKey: value);
            }
            else secret = invocation?.Authentication ?? throw new InvalidDataException("SSH authentication memory-channel input missing.");
            try
            {
                using var session = await PinnedSession.OpenAsync(config, secret, worker is not null, budget, cancellation.Token);
                if (worker is not null)
                {
                    var report = await RemoteDispatch.RunAsync(session, worker, input, Required("--logs"), budget, cancellation.Token);
                    AssetTransport.WriteJson(output, report, RemoteJsonContext.Default.RemoteDispatchReport, budget); outputAvailable = false;
                    return report.Status.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1;
                }
                if (input.Length != 0) throw new InvalidDataException("File transport does not consume payload standard input.");
                var transfer = AssetTransport.ReadJson(Required("--request"), RemoteJsonContext.Default.TransferRequest, budget);
                var result = arguments[0] == "upload" ? await SftpTransfers.UploadAsync(session, transfer, Required("--local"), budget, cancellation.Token) :
                    await SftpTransfers.DownloadAsync(session, transfer, Required("--local"), budget, cancellation.Token);
                AssetTransport.WriteJson(output, result, RemoteJsonContext.Default.RemoteTransferReport, budget); outputAvailable = false; return 0;
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(input); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or
            OperationCanceledException or TimeoutException or SshException or System.Net.Sockets.SocketException or KeyNotFoundException or FormatException)
        {
            if (outputAvailable && output is not null && !Path.Exists(output))
                AssetTransport.WriteJson(output, new RemoteFailure(error is OperationCanceledException or TimeoutException ? "CANCELLED_OR_TIMED_OUT" : "FAIL", error.GetType().Name),
                    RemoteJsonContext.Default.RemoteFailure, new Budget(5, null, CancellationToken.None));
            Console.Error.WriteLine("remote-tool: " + error.GetType().Name + "; remote state is preserved; inspect before retry.");
            return error is OperationCanceledException or TimeoutException ? 124 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static async Task<RemoteInvocationInput> ReadInvocationAsync(Budget budget, CancellationToken cancellation)
    {
        using var deadline = PinnedSession.Deadline(budget, cancellation, 10); using var stream = Console.OpenStandardInput();
        using var memory = new MemoryStream(); var buffer = new byte[65536]; const int cap = 24 * 1024 * 1024;
        try
        {
            for (var readIndex = 0; readIndex <= cap; readIndex++)
            {
                budget.Check(); var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read == 0)
                {
                    using var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 16 }); AssetTransport.ValidateJson(document.RootElement, budget);
                    return document.RootElement.Deserialize(RemoteJsonContext.Default.RemoteInvocationInput) ?? throw new InvalidDataException("SSH memory-channel input missing.");
                }
                if (memory.Length + read > cap) throw new InvalidDataException("SSH memory-channel input cap exceeded."); memory.Write(buffer, 0, read);
            }
            throw new InvalidDataException("SSH memory-channel input read cap exceeded.");
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer); if (memory.TryGetBuffer(out var bytes)) System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes.AsSpan()); }
    }
}
