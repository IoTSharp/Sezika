using System.Diagnostics;
using Sezika.Cuda;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class FullEncoderLimitChecks
{
    // Pure integer checks. No CudaDevice, tensors, kernel or optimizer is loaded.
    internal static void Run(Action<bool, string> check, CancellationToken cancellation, bool tiny = false)
    {
        var clock = Stopwatch.StartNew(); var checks = 0;
        void Check(bool condition, string name)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++checks > 32 || clock.Elapsed.TotalSeconds >= 5) throw new TimeoutException("CPU update-limit check bound32/5s.");
            check(condition, "CPU update limit: " + name);
        }
        void Reject(Action action, string name)
        {
            cancellation.ThrowIfCancellationRequested();
            try { action(); }
            catch (ArgumentOutOfRangeException) { Check(true, name); return; }
            Check(false, name + " was accepted");
        }
        Check(FullEncoderUpdateLimit.MaximumUpdates == 800_000_000, "explicit global bound800M");
        if (tiny)
        {
            Check(FullEncoderUpdateLimit.Next(1_000_000) == 1_000_001, "one-million boundary no longer truncates the full plan");
            return;
        }
        foreach (var completed in new[] { 0L, 1L, 999_999L, 1_000_000L, 1_000_001L, 14_592_719L, 14_592_720L, 799_999_999L })
            Check(FullEncoderUpdateLimit.Next(completed) == completed + 1, "next committed integer after " + completed);
        foreach (var completed in new[] { 0L, 1L, 999_999L, 1_000_000L, 1_000_001L, 14_592_719L, 14_592_720L, 799_999_999L, 800_000_000L })
        {
            cancellation.ThrowIfCancellationRequested(); FullEncoderUpdateLimit.ValidateRestore(completed); Check(true, "restore integer within bound at " + completed);
        }
        foreach (var completed in new[] { long.MinValue, -1L, 800_000_000L, 800_000_001L, long.MaxValue })
            Reject(() => FullEncoderUpdateLimit.Next(completed), "next rejects out-of-range or overflow input " + completed);
        foreach (var completed in new[] { long.MinValue, -1L, 800_000_001L, long.MaxValue })
            Reject(() => FullEncoderUpdateLimit.ValidateRestore(completed), "restore rejects out-of-range input " + completed);
    }
}
