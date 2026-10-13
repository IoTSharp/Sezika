#:property PublishAot=false
#:property PublishTrimmed=false
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

const string root = @"D:\source\Sezika\docs\blogs\2026-10-13";
const string evidence = @"D:\source\Sezika\.artifacts\articles-20261013";
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(210));
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
using var proxyHandler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy("http://127.0.0.1:7890") };
using var proxy = new HttpClient(proxyHandler) { Timeout = TimeSpan.FromSeconds(15) };
using var articles = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "articles.json")));
var receipts = new List<PublicReceipt>();
if (articles.RootElement.GetArrayLength() != 8) throw new InvalidDataException("Article count mismatch");
foreach (var article in articles.RootElement.EnumerateArray())
{
    deadline.Token.ThrowIfCancellationRequested();
    var id = article.GetProperty("Id").GetString()!;
    var title = article.GetProperty("Title").GetString()!;
    var raw = File.ReadAllText(article.GetProperty("Markdown").GetString()!);
    var body = Regex.Match(raw, @"\A---[\s\S]*?\r?\n---\r?\n([\s\S]*)", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
    var paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith('#') && !x.StartsWith('!')).ToArray();
    var intro = Normalize(paragraphs[0]);
    using var osc = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "oschina-" + id + "-submit.json")));
    var images = osc.RootElement.GetProperty("uploadedArticleImages").EnumerateArray().Select(x => x.GetProperty("url").GetString()!).ToArray();
    foreach (var platform in new[] { "cnblogs", "oschina" })
    {
        deadline.Token.ThrowIfCancellationRequested();
        using var submitted = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, platform + "-" + id + "-submit.json")));
        var url = submitted.RootElement.GetProperty("url").GetString()!;
        var uri = new Uri(url);
        if (uri.Scheme != "https" || !(uri.Host == "www.cnblogs.com" || uri.Host == "my.oschina.net")) throw new InvalidDataException("Public host mismatch");
        var saved = Path.Combine(evidence, platform + "-" + id + "-public.html");
        string html;
        try
        {
            html = await http.GetStringAsync(uri, deadline.Token);
        }
        catch (HttpRequestException) { html = await proxy.GetStringAsync(uri, deadline.Token); }
        catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { html = await proxy.GetStringAsync(uri, deadline.Token); }
        if (html.Length > 8_000_000) throw new InvalidDataException("Response size budget");
        File.WriteAllText(saved, html);
        var decoded = WebUtility.HtmlDecode(html);
        var plain = Normalize(Regex.Replace(decoded, @"<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(2)));
        var result = new PublicReceipt(id, platform, url, decoded.Contains(title, StringComparison.Ordinal), plain.Contains(intro, StringComparison.Ordinal), images.All(decoded.Contains), saved);
        receipts.Add(result);
        Console.WriteLine(id + " " + platform + " title=" + result.Title + " intro=" + result.Intro + " images=" + result.Images);
    }
}
File.WriteAllText(Path.Combine(root, "public-verification.json"), JsonSerializer.Serialize(receipts, VerifyJson.Default.ListPublicReceipt));
Console.WriteLine("Public pages verified: " + receipts.Count(x => x.Title && x.Intro && x.Images) + "/16");
static string Normalize(string text) => Regex.Replace(text, @"[^\p{L}\p{N}]", "", RegexOptions.None, TimeSpan.FromSeconds(2));
internal sealed record PublicReceipt(string Article, string Platform, string Url, bool Title, bool Intro, bool Images, string Evidence);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<PublicReceipt>))]
internal partial class VerifyJson:JsonSerializerContext;
