#:property PublishAot=false
#:property PublishTrimmed=false
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(85));
var mode = args[0];
var output = args[1];
if (mode == "cnblogs-api")
{
    using var config = JsonDocument.Parse(File.ReadAllText(@"C:\Users\mysti\.codex\skills\publish-cnblogs\config\settings.json"));
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.RootElement.GetProperty("personalAccessToken").GetString());
    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization-Type", "pat");
    var address = args.Length > 2 ? args[2] : "https://i.cnblogs.com/openapi/v1/posts?pageIndex=1&pageSize=500";
    if (new Uri(address).Host != "i.cnblogs.com") throw new InvalidDataException("Unexpected API host");
    using var response = await http.GetAsync(address, deadline.Token);
    var text = await response.Content.ReadAsStringAsync(deadline.Token);
    if (text.Length > 8_000_000) throw new InvalidDataException("API response limit");
    File.WriteAllText(output, text);
    Console.WriteLine("CNBlogs read HTTP " + (int)response.StatusCode + "; saved " + output);
    if (response.IsSuccessStatusCode) { using var json = JsonDocument.Parse(text); Console.WriteLine("JSON kind: " + json.RootElement.ValueKind); }
}
if (mode == "cnblogs-list" || mode == "cnblogs-get")
{
    using var config = JsonDocument.Parse(File.ReadAllText(@"C:\Users\mysti\.codex\skills\publish-cnblogs\config\settings.json"));
    var root = config.RootElement;
    string Value(string name) => root.TryGetProperty(name, out var v) ? v.ToString() : throw new InvalidDataException("Configuration field missing: " + name);
    var username = Value("metaWeblogUsername");
    var password = Value("metaWeblogPassword");
    var endpoint = Value("metaWeblogEndpoint");
    if (new Uri(endpoint).Host != "rpc.cnblogs.com") throw new InvalidDataException("Unexpected RPC host");
    var blogId = Value("blogId");
    var identity = await Rpc(endpoint, "blogger.getUsersBlogs", ["", username, password], deadline.Token);
    var blogs = identity.Descendants("struct").Select(Fields).Where(x => x.ContainsKey("blogid")).ToArray();
    if (!blogs.Any(x => x["blogid"] == blogId && x.GetValueOrDefault("url", "").Contains("cnblogs.com/IoTSharp", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Blog identity mismatch");
    var response = mode == "cnblogs-list"
        ? await Rpc(endpoint, "metaWeblog.getRecentPosts", [blogId, username, password, 500], deadline.Token)
        : await Rpc(endpoint, "metaWeblog.getPost", [args[2], username, password], deadline.Token);
    var posts = response.Descendants("struct").Select(Fields).Where(x => x.ContainsKey("postid") && x.ContainsKey("title")).Take(501).ToArray();
    if (posts.Length > 500) throw new InvalidDataException("Post result limit");
    var rows = posts.Select(x => new Post(x["postid"], x["title"], x.GetValueOrDefault("link") ?? x.GetValueOrDefault("permaLink") ?? "", x.GetValueOrDefault("description") ?? "")).ToList();
    if (mode == "cnblogs-get" && (rows.Count != 1 || rows[0].Id != args[2])) throw new InvalidDataException("Post readback mismatch");
    File.WriteAllText(output, JsonSerializer.Serialize(rows, ReadJson.Default.ListPost));
    Console.WriteLine(JsonSerializer.Serialize(new ReadSummary("IoTSharp", rows.Count, mode, output), ReadJson.Default.ReadSummary));
}
else if (mode == "public-get")
{
    var uri = new Uri(args[2]);
    if (uri.Scheme != "https" || !(uri.Host == "www.cnblogs.com" || uri.Host == "my.oschina.net")) throw new InvalidDataException("Public read host mismatch");
    using var handler = new HttpClientHandler();
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
    string text;
    try { text = await http.GetStringAsync(uri, deadline.Token); }
    catch (HttpRequestException)
    {
        using var fallbackHandler = new HttpClientHandler { Proxy = new WebProxy("http://127.0.0.1:7890"), UseProxy = true };
        using var fallback = new HttpClient(fallbackHandler) { Timeout = TimeSpan.FromSeconds(25) };
        text = await fallback.GetStringAsync(uri, deadline.Token);
    }
    if (text.Length > 8_000_000) throw new InvalidDataException("Public response limit");
    File.WriteAllText(output, text, new UTF8Encoding(false));
    Console.WriteLine("Public read saved: " + output);
}

static Dictionary<string, string> Fields(XElement record) => record.Elements("member").Where(x => x.Element("name") != null).ToDictionary(x => x.Element("name")!.Value, x => x.Element("value")?.Value ?? "", StringComparer.Ordinal);
static async Task<XDocument> Rpc(string endpoint, string method, object[] arguments, CancellationToken token)
{
    var body = new XDocument(new XElement("methodCall", new XElement("methodName", method), new XElement("params", arguments.Select(x => new XElement("param", new XElement("value", x is int n ? new XElement("int", n) : new XElement("string", x))))))).ToString(SaveOptions.DisableFormatting);
    for (var attempt = 0; attempt < 2; attempt++)
    {
        token.ThrowIfCancellationRequested();
        using var handler = new HttpClientHandler { UseProxy = attempt == 1, Proxy = attempt == 1 ? new WebProxy("http://127.0.0.1:7890") : null };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new StringContent(body, Encoding.UTF8, "text/xml");
        try
        {
            using var response = await http.PostAsync(endpoint, content, token);
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(token);
            if (text.Length > 16_000_000) throw new InvalidDataException("RPC response limit");
            var xml = XDocument.Parse(text);
            if (xml.Descendants("fault").Any()) throw new InvalidDataException("CNBlogs RPC returned a fault");
            return xml;
        }
        catch (HttpRequestException) when (attempt == 0) { Console.WriteLine("Read-only RPC direct connection failed; using configured local proxy."); }
        catch (TaskCanceledException) when (attempt == 0 && !token.IsCancellationRequested) { Console.WriteLine("Read-only RPC direct timeout; using configured local proxy."); }
    }
    throw new HttpRequestException("Read-only RPC failed");
}
internal sealed record Post(string Id, string Title, string Url, string Body);
internal sealed record ReadSummary(string Account, int Count, string Operation, string Evidence);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Post>))]
[JsonSerializable(typeof(ReadSummary))]
internal partial class ReadJson : JsonSerializerContext;
