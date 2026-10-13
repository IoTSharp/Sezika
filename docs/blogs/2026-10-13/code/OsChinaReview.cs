#:property PublishAot=false
#:property PublishTrimmed=false
#:property AllowUnsafeBlocks=true
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string evidence = @"D:\source\Sezika\.artifacts\articles-20261013";
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(110));
var publicResults = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(evidence, "oschina-public-browser-verification.json")), ReviewJson.Default.ListPublicItem)!;
if (publicResults.Count != 8) throw new InvalidDataException("Expected eight public checks");
var targets = publicResults.Where(x => x.Blocked).Take(9).ToArray();
if (targets.Length > 8) throw new InvalidDataException("Review target budget");
if (!Native.CredRead("Codex:OSChina:Session:mysticboy@live.com", 1, 0, out var pointer)) throw new InvalidOperationException("Existing OSChina session is unavailable");
Session session;
try
{
    var credential = Marshal.PtrToStructure<Credential>(pointer);
    if (!string.Equals(Marshal.PtrToStringUni(credential.UserName), "mysticboy@live.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Credential account mismatch");
    if (credential.BlobSize == 0 || credential.BlobSize > 5120 || credential.BlobSize % 2 != 0) throw new InvalidDataException("Session blob budget");
    var blob = new byte[credential.BlobSize];
    Marshal.Copy(credential.Blob, blob, 0, blob.Length);
    try { session = JsonSerializer.Deserialize(Encoding.Unicode.GetString(blob), ReviewJson.Default.Session) ?? throw new InvalidDataException("Session format"); }
    finally { CryptographicOperations.ZeroMemory(blob); }
}
finally { Native.CredFree(pointer); }
if (session.Version != 1 || session.Cookies.Length > 20) throw new InvalidDataException("Session schema mismatch");
var cookies = new CookieContainer();
foreach (var item in session.Cookies)
{
    deadline.Token.ThrowIfCancellationRequested();
    if (!(item.Name is "oscid" or "BEC" or "sl-session") || !(item.Domain.TrimStart('.').ToLowerInvariant() is "oschina.net" or "www.oschina.net" or "my.oschina.net" or "apiv1.oschina.net")) continue;
    if (item.Value.Length > 4096 || item.Value.Contains('\r') || item.Value.Contains('\n') || !item.Path.StartsWith('/')) throw new InvalidDataException("Cookie validation failed");
    cookies.Add(new Cookie(item.Name, item.Value, item.Path, item.Domain) { Secure = item.Secure, HttpOnly = item.HttpOnly });
}
using var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true, UseProxy = false };
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
if (!string.IsNullOrWhiteSpace(session.UserAgent) && session.UserAgent.Length <= 512 && !session.UserAgent.Contains('\r') && !session.UserAgent.Contains('\n')) http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", session.UserAgent);
http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.oschina.net");
http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
http.DefaultRequestHeaders.Referrer = new Uri("https://my.oschina.net/u/7172");
using var identityResponse = await http.GetAsync("https://apiv1.oschina.net/oschinapi/user/myDetails", deadline.Token);
identityResponse.EnsureSuccessStatusCode();
using var identity = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync(deadline.Token));
if (identity.RootElement.GetProperty("code").GetInt32() != 200 || identity.RootElement.GetProperty("result").GetProperty("userId").GetInt32() != 7172) throw new InvalidOperationException("OSChina session identity could not be confirmed");
var receipts = new List<ReviewReceipt>();
foreach (var target in targets)
{
    deadline.Token.ThrowIfCancellationRequested();
    using var submitted = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "oschina-" + target.Article + "-submit.json")));
    if (submitted.RootElement.GetProperty("remoteId").GetInt32() != target.Id || submitted.RootElement.GetProperty("title").GetString() != target.Title) throw new InvalidDataException("Original article binding mismatch");
    var attempt = Path.Combine(evidence, "oschina-" + target.Article + "-review.attempt");
    if (File.Exists(attempt)) throw new IOException("Existing review attempt requires reconciliation");
    File.WriteAllText(attempt, DateTimeOffset.UtcNow.ToString("O"));
    using var content = new FormUrlEncodedContent([new("objId", target.Id.ToString()), new("objType", "3")]);
    using var response = await http.PostAsync("https://apiv1.oschina.net/oschinapi/check/urge", content, deadline.Token);
    response.EnsureSuccessStatusCode();
    using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
    var code = data.RootElement.GetProperty("code").GetInt32();
    var accepted = code == 200;
    var result = new ReviewReceipt(target.Article, target.Id, code, accepted, DateTimeOffset.UtcNow.ToString("O"));
    receipts.Add(result);
    File.WriteAllText(Path.Combine(evidence, "oschina-" + target.Article + "-review.json"), JsonSerializer.Serialize(result, ReviewJson.Default.ReviewReceipt));
    Console.WriteLine("Review " + target.Article + " original ID " + target.Id + ": code=" + code + ", accepted=" + accepted);
    if (!accepted) throw new InvalidOperationException("Review request was refused; original article retained");
}
File.WriteAllText(Path.Combine(evidence, "oschina-review-receipts.json"), JsonSerializer.Serialize(receipts, ReviewJson.Default.ListReviewReceipt));
Console.WriteLine("Submitted reviews: " + receipts.Count);

[StructLayout(LayoutKind.Sequential)]
internal struct Credential
{
    public uint Flags; public uint Type; public nint Target; public nint Comment;
    public long LastWritten; public uint BlobSize; public nint Blob;
    public uint Persist; public uint AttributeCount; public nint Attributes;
    public nint Alias; public nint UserName;
}
internal static partial class Native
{
    [LibraryImport("advapi32.dll", EntryPoint="CredReadW", StringMarshalling=StringMarshalling.Utf16, SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CredRead(string target, uint type, uint flags, out nint credential);
    [LibraryImport("advapi32.dll")]
    internal static partial void CredFree(nint buffer);
}
internal sealed record Session([property:JsonPropertyName("version")]int Version,[property:JsonPropertyName("cookies")]SessionCookie[] Cookies,[property:JsonPropertyName("userAgent")]string UserAgent);
internal sealed record SessionCookie([property:JsonPropertyName("name")]string Name,[property:JsonPropertyName("value")]string Value,[property:JsonPropertyName("domain")]string Domain,[property:JsonPropertyName("path")]string Path,[property:JsonPropertyName("secure")]bool Secure,[property:JsonPropertyName("httpOnly")]bool HttpOnly);
internal sealed record PublicItem([property:JsonPropertyName("article")]string Article,[property:JsonPropertyName("id")]int Id,[property:JsonPropertyName("title")]string Title,[property:JsonPropertyName("blocked")]bool Blocked);
internal sealed record ReviewReceipt(string Article,int Id,int Code,bool Accepted,string RequestedAt);
[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(List<PublicItem>))]
[JsonSerializable(typeof(ReviewReceipt))]
[JsonSerializable(typeof(List<ReviewReceipt>))]
internal partial class ReviewJson:JsonSerializerContext;
