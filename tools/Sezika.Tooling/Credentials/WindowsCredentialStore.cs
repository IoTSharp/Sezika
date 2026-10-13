using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Sezika.Tooling;

internal sealed class CredentialValue : IDisposable
{
    internal string UserName { get; }
    private byte[] bytes;
    internal CredentialValue(string userName, byte[] value) { UserName = userName; bytes = value; }
    internal byte[] CopySecret() { ObjectDisposedException.ThrowIf(bytes.Length == 0, this); return bytes.ToArray(); }
    internal string Text(string encoding = "utf8")
    {
        ObjectDisposedException.ThrowIf(bytes.Length == 0, this);
        return encoding switch { "utf8" => new UTF8Encoding(false, true).GetString(bytes), "utf16le" when bytes.Length % 2 == 0 => new UnicodeEncoding(false, false, true).GetString(bytes), _ => throw new InvalidDataException("Explicit UTF8 or UTF16LE credential encoding required.") };
    }
    public void Dispose() { CryptographicOperations.ZeroMemory(bytes); bytes = []; }
    public override string ToString() => "<credential:redacted>";
}

internal static partial class WindowsCredentialStore
{
    internal static unsafe CredentialValue Read(string target)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credential Manager reads require Windows; Linux receives credentials only through a bounded memory channel.");
        if (target.Length is < 1 or > 256 || target.Any(char.IsControl)) throw new ArgumentException("Explicit bounded credential target required.");
        if (CredRead(target, 1, 0, out var address) == 0) throw new IOException("Credential Manager target unavailable; native status " + Marshal.GetLastPInvokeError());
        try
        {
            var value = *(NativeCredential*)address;
            if (value.Type != 1 || value.Size is < 1 or > 2560 || value.Blob == 0) throw new InvalidDataException("Credential type/blob bounds differ.");
            var storedTarget = Marshal.PtrToStringUni(value.Target) ?? "";
            if (!storedTarget.Equals(target, StringComparison.Ordinal)) throw new InvalidDataException("Credential target identity differs.");
            var bytes = new byte[value.Size]; Marshal.Copy(value.Blob, bytes, 0, bytes.Length);
            return new(Marshal.PtrToStringUni(value.UserName) ?? "", bytes);
        }
        finally { CredFree(address); }
    }

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
    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CredRead(string target, uint type, uint flags, out nint value);
    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(nint value);
}
