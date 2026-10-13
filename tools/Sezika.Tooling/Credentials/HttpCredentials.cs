using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record HttpSecretInput(string Token, string? UserName = null);
internal static class HttpCredentials
{
    internal static AuthenticationHeaderValue? Read(string source, string? target, string encoding, string provider, Budget budget)
    {
        if (source == "auto") source = target is null ? "none" : "credential-manager";
        if (source == "none") { if (target is not null) throw new InvalidDataException("Credential target supplied without its secure-store source."); return null; }
        HttpSecretInput secret;
        if (source == "credential-manager")
        {
            if (target is null) throw new InvalidDataException("Explicit secure-store target required.");
            using var value = WindowsCredentialStore.Read(target);
            secret = new(value.Text(encoding), value.UserName);
        }
        else if (source == "stdin")
        {
            if (target is not null) throw new InvalidDataException("Memory credentials cannot also name a secure-store target.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Math.Min(10, budget.RemainingSeconds))));
            using var input = Console.OpenStandardInput(); var bytes = new byte[16385]; var count = 0;
            try
            {
                for (var block = 0; block < 18; block++)
                {
                    budget.Check();
                    var read = input.ReadAsync(bytes.AsMemory(count, Math.Min(1024, bytes.Length - count)), timeout.Token).AsTask().GetAwaiter().GetResult();
                    if (read == 0) break; count += read;
                    if (count > 16384 || block == 17) throw new InvalidDataException("Credential memory-channel byte/iteration bound.");
                }
                using var document = JsonDocument.Parse(bytes.AsMemory(0, count)); AssetTransport.ValidateJson(document.RootElement, budget);
                secret = document.RootElement.Deserialize(HttpCredentialJson.Default.HttpSecretInput) ?? throw new InvalidDataException("Memory credential envelope missing.");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        else throw new InvalidDataException("Credential source must be none, secure store or bounded stdin.");
        if (secret.Token.Length is < 1 or > 8192 || secret.Token.Any(char.IsControl) || (secret.UserName?.Any(char.IsControl) ?? false)) throw new InvalidDataException("Credential content bounds differ.");
        if (provider == "moark")
        {
            if (string.IsNullOrWhiteSpace(secret.UserName) || secret.UserName.Contains(':') || secret.UserName.Length > 128) throw new InvalidDataException("MoArk Git/LFS requires an explicit account username.");
            var bytes = Encoding.UTF8.GetBytes(secret.UserName + ":" + secret.Token);
            try { return new("Basic", Convert.ToBase64String(bytes)); } finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        if (provider is not ("huggingface" or "bearer")) throw new InvalidDataException("HTTP credential provider differs.");
        return new("Bearer", secret.Token);
    }
}

[JsonSerializable(typeof(HttpSecretInput))]
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
internal partial class HttpCredentialJson : JsonSerializerContext;
