namespace Sezika;

/// <summary>Stable diagnostic code plus a readable explanation; never a model answer.</summary>
public class DecisionException : Exception
{
    public DecisionException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
