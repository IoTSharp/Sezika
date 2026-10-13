namespace Sezika.Cuda;

/// <summary>Shared bound for full-corpus cursors and the FP32 AdamW state.</summary>
public static class FullEncoderUpdateLimit
{
    public const long MaximumUpdates = 800_000_000;

    public static long Next(long completed)
    {
        if (completed < 0 || completed >= MaximumUpdates)
            throw new ArgumentOutOfRangeException(nameof(completed), "Full encoder update limit reached.");
        return checked(completed + 1);
    }

    public static void ValidateRestore(long completed)
    {
        if (completed < 0 || completed > MaximumUpdates)
            throw new ArgumentOutOfRangeException(nameof(completed), "Full encoder restore cursor outside bounds.");
    }
}
