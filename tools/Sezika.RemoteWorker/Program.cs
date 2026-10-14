using System.Runtime.CompilerServices;
using Sezika.RemoteTool;
using Sezika.Tooling;

if (RuntimeFeature.IsDynamicCodeSupported) throw new PlatformNotSupportedException("RemoteWorker requires its actual NativeAOT published image.");
if (args.Length == 3 && args[0] == "process" && args[1] is "broker" or "watchdog")
    return args[1] == "broker" ? ProcessExecutor.RunBroker(args[2], CancellationToken.None) : ProcessExecutor.RunWatchdog(args[2], CancellationToken.None);
if (args.Length != 5 || args[0] != "worker" || args[1] != "--request" || args[3] != "--sha256")
    throw new ArgumentException("Fixed native worker accepts only worker --request <typed-json> --sha256 <trusted-sha256>.");
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(31));
ConsoleCancelEventHandler cancel = (_, value) => { value.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try { StartupGate.Wait(cancellation.Token); return await RemoteWorker.RunAsync(args[2], args[4], cancellation.Token); }
catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException)
{
    Console.Error.WriteLine("native-worker: " + error.GetType().Name + "; preserve request and process receipts");
    return error is OperationCanceledException or TimeoutException ? 124 : 1;
}
finally { Console.CancelKeyPress -= cancel; }
