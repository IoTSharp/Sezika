namespace Sezika.Cuda;

/// <summary>Failure returned by the CUDA Driver API or by backend validation.</summary>
public sealed class CudaException : Exception
{
    public CudaException(string code, string message, int? driverCode = null, Exception? innerException = null, string? operation = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        DriverCode = driverCode;
        Operation = operation;
    }

    public string Code { get; }

    public int? DriverCode { get; }

    public string? Operation { get; }
}
