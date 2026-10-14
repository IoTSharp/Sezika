using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static partial class CredentialChecks
{
    private const string Prefix = "Sezika.Tooling.Fixture.";
    private const int NotFound = 1168;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        internal uint Flags, Type;
        internal nint Target, Comment;
        internal long Written;
        internal uint Size;
        internal nint Blob;
        internal uint Persist, Attributes;
        internal nint AttributeArray, Alias, UserName;
    }
    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    private static partial int CredWrite(ref NativeCredential credential, uint flags);
    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredDelete(string target, uint type, uint flags);
    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredRead(string target, uint type, uint flags, out nint credential);
    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint credential);

    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var clock = Stopwatch.StartNew(); var targets = 0; var processes = 0;
        var ledger = Path.Combine(root, "credential-fixture-targets.txt");
        Budget Limit() => new(25, null, cancellation.Token);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or ObjectDisposedException or PlatformNotSupportedException) { return; }
            throw new InvalidOperationException("Expected credential fixture rejection.");
        }
        void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 28) throw new TimeoutException("Credential checks wall bound30s.");
        }
        string Target()
        {
            Gate(); if (++targets > 4) throw new InvalidDataException("Synthetic credential target bound4.");
            var target = Prefix + Guid.NewGuid().ToString("N");
            // Target identity is recorded before mutation so a host interruption
            // never requires credential enumeration or a prefix-wide cleanup.
            File.AppendAllText(ledger, "registered " + target + "\n", new UTF8Encoding(false)); return target;
        }
        bool Exists(string target)
        {
            if (!target.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(target[Prefix.Length..], "N", out _)) throw new InvalidDataException("Task-owned synthetic target required.");
            if (CredRead(target, 1, 0, out var credential) != 0)
            {
                // Do not inspect a collided/existing blob or user identity.
                CredFree(credential); return true;
            }
            var status = Marshal.GetLastPInvokeError();
            if (status != NotFound) throw new IOException("Synthetic credential existence native status " + status);
            return false;
        }
        void WithTarget(byte[] bytes, string username, Action<string> action)
        {
            Gate();
            if (bytes.Length is < 1 or > 2560 || username.Length > 128) throw new InvalidDataException("Synthetic credential blob/user bound.");
            var target = Target(); var writeAttempted = false;
            try
            {
                if (Exists(target)) throw new IOException("Synthetic target collision; original target preserved.");
                nint targetMemory = 0, userMemory = 0; GCHandle pinned = default;
                try
                {
                    targetMemory = Marshal.StringToCoTaskMemUni(target); userMemory = Marshal.StringToCoTaskMemUni(username);
                    pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    var native = new NativeCredential { Type = 1, Target = targetMemory, UserName = userMemory, Blob = pinned.AddrOfPinnedObject(), Size = (uint)bytes.Length, Persist = 1 };
                    writeAttempted = true;
                    if (CredWrite(ref native, 0) == 0) throw new IOException("Synthetic credential write native status " + Marshal.GetLastPInvokeError());
                }
                finally { if (pinned.IsAllocated) pinned.Free(); if (userMemory != 0) Marshal.FreeCoTaskMem(userMemory); if (targetMemory != 0) Marshal.FreeCoTaskMem(targetMemory); }
                Gate(); action(target);
            }
            finally
            {
                // Cleanup is independent of the cancelled test budget. It only
                // deletes the exact unique target which this action attempted.
                try
                {
                    if (writeAttempted)
                    {
                        if (CredDelete(target, 1, 0) == 0 && Marshal.GetLastPInvokeError() != NotFound) throw new IOException("Synthetic credential delete native status " + Marshal.GetLastPInvokeError());
                        if (Exists(target)) throw new IOException("Synthetic credential target survives exact cleanup.");
                        File.AppendAllText(ledger, "deleted_and_absent " + target + "\n", new UTF8Encoding(false));
                    }
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        void Value(string target, byte[] expected, string username, string encoding, string expectedText)
        {
            using var value = WindowsCredentialStore.Read(target); var copy = value.CopySecret();
            try
            {
                Assert(value.UserName == username && CryptographicOperations.FixedTimeEquals(copy, expected) && value.Text(encoding) == expectedText && value.ToString() == "<credential:redacted>", "Synthetic credential identity/blob/encoding/redaction differs.");
                Reject(() => value.Text("unknown-encoding"));
            }
            finally { CryptographicOperations.ZeroMemory(copy); }
            value.Dispose(); Reject(() => value.CopySecret()); Reject(() => value.Text(encoding));
        }
        void Cleanup(ExecutionResult result)
        {
            Assert(result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 && result.Processes.Length is > 0 and <= 64 && result.RootPid is not null, "Credential channel native cleanup/identity incomplete.");
            foreach (var identity in result.Processes)
            {
                Gate();
                Assert(identity.ParentPid > 0 && identity.ParentChain.Length is > 0 and <= 64 && identity.CommandLine.Length > 0 && (identity.Platform == "windows" ? identity.Started is not null : identity.StartTicks is not null && identity.BootId is not null), "Credential channel PID/birth/full argv/parent chain absent.");
                var current = ProcessObserver.Observe(identity.Pid);
                var reused = current.State == "Present" && (identity.Platform == "windows" ? current.Started is not null && current.Started != identity.Started : current.StartTicks is not null && (current.StartTicks != identity.StartTicks || current.BootId != identity.BootId));
                Assert(current.State == "Absent" || reused, "Credential channel process survives or absence is UNKNOWN.");
            }
        }
        void Channel(string name, byte[] input, string expectedError, string forbiddenValue)
        {
            Gate(); if (++processes > 3 || input.Length > 16384) throw new InvalidDataException("Credential channel fixture process/input bound3/16KiB.");
            var image = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER") ?? throw new InvalidOperationException("Explicit separately built Sezika.Tooling native apphost required for cross-process memory-channel checks.");
            var directory = Path.Combine(root, "credential-channel-" + name); Directory.CreateDirectory(directory);
            var requestPath = Path.Combine(directory, "request.json"); var output = Path.Combine(directory, "result.json");
            // Invalid provider is rejected immediately after bounded stdin decode,
            // before AssetTransport creates a client or accesses any URI.
            var request = new DownloadRequest(new("https://invalid.example/never-accessed", 1, new string('a', 64)), Path.Combine(directory, "never-created.bin"), CredentialSource: "stdin", AuthenticationProvider: "unsupported-fixture-provider");
            File.WriteAllBytes(requestPath, JsonSerializer.SerializeToUtf8Bytes(request, MigrationJsonContext.Default.DownloadRequest));
            ExecutionResult result;
            try
            {
                result = ProcessExecutor.ExecuteAsync(new ExecutionRequest
                {
                    Executable = image, ArgumentList = ["transport", "download", "--request", requestPath, "--output", output, "--timeout-seconds", "10"],
                    WorkingDirectory = directory, OutputDirectory = Path.Combine(directory, "execution"), StandardInput = input,
                    MaximumInputBytes = 16384, MaximumOutputBytes = 16384, TimeoutSeconds = 12, CleanupSeconds = 5,
                    BrokerExecutable = image
                }, new Budget(20, null, cancellation.Token), cancellation.Token).GetAwaiter().GetResult();
                Cleanup(result);
                Assert(result.Status == "FAIL" && result.ExitCode == 1 && result.InputComplete && result.StandardInputBytes == input.Length && !File.Exists(request.Destination), "Credential channel bypassed refusal, failed its bound or accessed download output.");
                using var receipt = JsonDocument.Parse(File.ReadAllBytes(output));
                Assert(receipt.RootElement.GetProperty("Status").GetString() == "FAIL" && receipt.RootElement.GetProperty("Message").GetString() == expectedError, "Credential channel did not reach its exact nonnetwork validation gate.");
                var escaped = JsonEncodedText.Encode(forbiddenValue).ToString();
                var encodedBytes = Encoding.UTF8.GetBytes(forbiddenValue); string encoded;
                try { encoded = Convert.ToBase64String(encodedBytes); } finally { CryptographicOperations.ZeroMemory(encodedBytes); }
                foreach (var file in new[] { requestPath, output, Path.Combine(directory, "execution", "stdout.bin"), Path.Combine(directory, "execution", "stderr.bin"), Path.Combine(directory, "execution", "result.json"), Path.Combine(directory, "execution", "registered.json") })
                {
                    Gate(); var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                    Assert(!text.Contains(forbiddenValue, StringComparison.Ordinal) && !text.Contains(escaped, StringComparison.Ordinal) && !text.Contains(encoded, StringComparison.Ordinal), "Synthetic input leaked into a credential receipt/log.");
                }
            }
            finally { CryptographicOperations.ZeroMemory(input); }
        }

        if (OperatingSystem.IsWindows())
        {
            check("Windows credential tiny actual UTF8 generic target read redaction and exact cleanup", () =>
            {
                var text = "合成凭据🔐UTF8_专用"; var username = "fixture-用户"; var bytes = Encoding.UTF8.GetBytes(text);
                WithTarget(bytes, username, target => Value(target, bytes, username, "utf8", text));
            });
            check("Windows credential actual UTF16LE Unicode blob and account round trip", () =>
            {
                var text = "合成凭据🔐UTF16LE_专用"; var username = "fixture-账户"; var bytes = new UnicodeEncoding(false, false, true).GetBytes(text);
                WithTarget(bytes, username, target => Value(target, bytes, username, "utf16le", text));
            });
            check("Windows credential malformed Unicode bytes and unknown encoding refuse decoding", () =>
            {
                WithTarget([255, 254, 255], "fixture-invalid", target =>
                {
                    using var value = WindowsCredentialStore.Read(target); Reject(() => value.Text("utf8")); Reject(() => value.Text("utf16le")); Reject(() => value.Text("unknown-encoding"));
                });
            });
            check("Windows credential absent exact synthetic target and malformed targets refuse reads", () =>
            {
                var target = Target(); Assert(!Exists(target), "Unique absent synthetic target unexpectedly exists."); Reject(() => WindowsCredentialStore.Read(target));
                Reject(() => WindowsCredentialStore.Read("bad\nfixture-target")); Assert(!Exists(target), "A missing-target check created a credential.");
                File.AppendAllText(ledger, "never_written_and_absent " + target + "\n", new UTF8Encoding(false));
            });
        }
        else
        {
            check("Non-Windows secure store remains explicitly unsupported without Windows PASS", () =>
            {
                var rejected = false; try { WindowsCredentialStore.Read(Prefix + Guid.NewGuid().ToString("N")); } catch (PlatformNotSupportedException) { rejected = true; }
                Assert(rejected, "Non-Windows store acquired unsupported credential capability.");
            });
        }
        check("Credential managed ownership zeroes original buffer and retains only redacted representation", () =>
        {
            var bytes = Encoding.UTF8.GetBytes("合成内存凭据_专用"); var value = new CredentialValue("fixture-memory-user", bytes); var copy = value.CopySecret();
            try { Assert(copy.Length == bytes.Length && !ReferenceEquals(copy, bytes) && value.ToString() == "<credential:redacted>", "Managed ownership or redaction differs."); }
            finally { value.Dispose(); CryptographicOperations.ZeroMemory(copy); }
            Assert(bytes.All(b => b == 0), "Disposed credential retained bytes in its owned original buffer."); Reject(() => value.CopySecret()); Reject(() => value.Text()); value.Dispose();
        });
        check("HTTP source none and invalid source contracts never materialize a secret", () =>
        {
            Assert(HttpCredentials.Read("none", null, "utf8", "bearer", Limit()) is null && HttpCredentials.Read("auto", null, "utf8", "unsupported-fixture-provider", Limit()) is null, "No-secret source fabricated an authorization header.");
            Reject(() => HttpCredentials.Read("none", "unreachable-fixture-target", "utf8", "bearer", Limit()));
            Reject(() => HttpCredentials.Read("unsupported-source", null, "utf8", "bearer", Limit()));
        });
        check("Credential bounded Unicode stdin is decoded in a separate native process and invalid provider refused", () =>
        {
            var token = "合成内存输入_不作真实认证";
            Channel("unicode", JsonSerializer.SerializeToUtf8Bytes(new HttpSecretInput(token, "fixture-user"), HttpCredentialJson.Default.HttpSecretInput), "HTTP credential provider differs.", token);
        });
        check("Credential bounded stdin refuses excessive token content without logging its original value", () =>
        {
            var token = new string('x', 8193);
            Channel("content-bound", JsonSerializer.SerializeToUtf8Bytes(new HttpSecretInput(token), HttpCredentialJson.Default.HttpSecretInput), "Credential content bounds differ.", token);
        });
    }
}
