#:property PublishAot=false
#:property PublishTrimmed=false
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

var promptPath = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var assetRoot = Path.GetFullPath(@"D:\source\Sezika\docs\blogs\2026-10-13\assets") + Path.DirectorySeparatorChar;
if (!promptPath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase) || !output.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset root mismatch");
if (File.Exists(output)) throw new IOException("Existing generation must be preserved");
var credential = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("Configured key unavailable");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(260));
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(250) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
var input = new ImageInput("gpt-image-2", File.ReadAllText(promptPath), 1, "1536x1024", "medium", "png");
using var content = new StringContent(JsonSerializer.Serialize(input, ImageJson.Default.ImageInput), System.Text.Encoding.UTF8, "application/json");
using var response = await http.PostAsync("https://sonnet.vip/v1/images/generations", content, deadline.Token);
Console.WriteLine("Image HTTP status: " + (int)response.StatusCode);
if (!response.IsSuccessStatusCode) throw new HttpRequestException("Image provider rejected request: HTTP " + (int)response.StatusCode);
if (response.Content.Headers.ContentLength > 50_000_000) throw new InvalidDataException("Image response budget");
var text = await response.Content.ReadAsStringAsync(deadline.Token);
if (text.Length > 50_000_000) throw new InvalidDataException("Image response budget");
var result = JsonSerializer.Deserialize(text, ImageJson.Default.ImageOutput) ?? throw new InvalidDataException("Empty image response");
if (result.Data.Length != 1 || string.IsNullOrEmpty(result.Data[0].Base64)) throw new InvalidDataException("Expected one returned image");
await File.WriteAllBytesAsync(output, Convert.FromBase64String(result.Data[0].Base64), deadline.Token);
Console.WriteLine("Image saved: " + output);
internal sealed record ImageInput(
    [property:JsonPropertyName("model")]string Model,
    [property:JsonPropertyName("prompt")]string Prompt,
    [property:JsonPropertyName("n")]int Count,
    [property:JsonPropertyName("size")]string Size,
    [property:JsonPropertyName("quality")]string Quality,
    [property:JsonPropertyName("output_format")]string Format);
internal sealed record ImageData([property:JsonPropertyName("b64_json")]string Base64);
internal sealed record ImageOutput([property:JsonPropertyName("data")]ImageData[] Data);
[JsonSerializable(typeof(ImageInput))]
[JsonSerializable(typeof(ImageOutput))]
internal partial class ImageJson:JsonSerializerContext;
