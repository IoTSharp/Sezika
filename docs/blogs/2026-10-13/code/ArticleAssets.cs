#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var root = Path.GetFullPath(@"D:\source\Sezika\docs\blogs\2026-10-13");
var assetRoot = Path.Combine(root, "assets");
Directory.CreateDirectory(assetRoot);
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
string[] subjects = ["本地多语言决策引擎", "选择、评分与真假判断", "编码器与决策头", "C# 与 Native AOT", "C# GPU 计算", "多语言训练与数据", "概率校准与可信决策", "本地部署与应用集成"];
string[][] stages = [
    ["业务状态|文本或 JSON", "明确问题|有限候选与标准", "模型判断|类型化结果与分布", "业务处理|复核、事实与授权"],
    ["Choice|有限候选选择", "Score|有序等级评价", "Boolean|明确命题判断", "类型化结果|保留分布与含义"],
    ["构造序列|状态、问题与候选", "编码器|上下文表示", "决策头|候选位置评分", "结果映射|类型化概率分布"],
    ["C# kernels|模型数值算子", "构建工具|ILGPU 导出 PTX", "静态驱动绑定|AOT 程序加载", "GPU 设备|执行可信计算产物"],
    ["数据准备|匹配的输入与资产", "前向与反向|预测、损失与梯度", "优化器更新|权重与状态", "完整保存|一致性与恢复检查"],
    ["数据准入|来源、许可与标签", "决策训练|编码器与决策头", "一致恢复|权重、状态与游标", "独立评测|语言、任务与质量"],
    ["模型分布|相对倾向", "独立评测|真实标签与误判", "概率校准|核对适用范围", "业务策略|接纳或人工复核"],
    ["业务请求|状态与判定标准", "受控推理|预算、模型与协议", "结果复核|状态与任务范围", "授权执行|事实、权限与流程"]
];
var mode = args.FirstOrDefault() ?? "diagrams";
for (var i = 0; i < 8; i++)
{
    budget.Token.ThrowIfCancellationRequested();
    var id = (i + 1).ToString("00");
    if (mode == "fallback" && args.Length > 1 && id == args[1])
    {
        var destination = Path.Combine(assetRoot, id + "-art.png");
        if (File.Exists(destination)) throw new IOException("Existing art must be preserved");
        using var bitmap = new Bitmap(1536, 1024);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new LinearGradientBrush(new Rectangle(0, 0, 1536, 1024), ColorTranslator.FromHtml("#0C203A"), ColorTranslator.FromHtml("#22676D"), 25f);
        g.FillRectangle(bg, 0, 0, 1536, 1024);
        using var line = new Pen(Color.FromArgb(90, 134, 219, 200), 3);
        using var copper = new SolidBrush(ColorTranslator.FromHtml("#D4A374"));
        for (var j = 0; j < 12; j++)
        {
            budget.Token.ThrowIfCancellationRequested();
            var y = 260 + j * 35;
            g.DrawLine(line, 130, y, 460, y);
            g.DrawLine(line, 1076, y, 1406, y);
            g.FillEllipse(copper, 120, y - 5, 10, 10);
            g.FillEllipse(copper, 1406, y - 5, 10, 10);
        }
        using var chip = new SolidBrush(ColorTranslator.FromHtml("#153F51"));
        g.FillRectangle(chip, 460, 220, 616, 490);
        g.DrawRectangle(line, 460, 220, 616, 490);
        for (var x = 0; x < 8; x++) for (var y = 0; y < 5; y++)
        {
            budget.Token.ThrowIfCancellationRequested();
            using var cell = new SolidBrush((x + y) % 3 == 0 ? ColorTranslator.FromHtml("#D4A374") : ColorTranslator.FromHtml("#43B6B4"));
            g.FillRectangle(cell, 505 + x * 67, 282 + y * 69, 43, 43);
        }
        bitmap.Save(destination, ImageFormat.Png);
        File.WriteAllText(Path.Combine(assetRoot, id + "-provenance.txt"), "Original geometric illustration rendered with C# / System.Drawing. No model output or benchmark is represented.");
    }
    if (mode == "diagrams")
    {
        using var bitmap = new Bitmap(1600, 600);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(ColorTranslator.FromHtml("#F5F6F3"));
        Text(g, "SEZIKA  /  " + id, 52, 34, 1400, 50, 21, ColorTranslator.FromHtml("#187A7C"), true);
        Text(g, subjects[i], 52, 90, 1490, 80, 40, ColorTranslator.FromHtml("#132C45"), true);
        for (var j = 0; j < 4; j++)
        {
            budget.Token.ThrowIfCancellationRequested();
            var x = 52 + j * 382;
            using var bg = new SolidBrush(j == 3 ? ColorTranslator.FromHtml("#DDECE6") : Color.White);
            g.FillRectangle(bg, x, 244, 332, 176);
            using var accent = new SolidBrush(ColorTranslator.FromHtml("#187A7C"));
            g.FillRectangle(accent, x, 244, 332, 5);
            var lines = stages[i][j].Split('|');
            Text(g, lines[0], x + 18, 276, 297, 59, 30, ColorTranslator.FromHtml("#132C45"), true);
            Text(g, lines[1], x + 18, 344, 297, 48, 20, ColorTranslator.FromHtml("#536575"), false);
            if (j < 3 && i != 1)
            {
                using var pen = new Pen(ColorTranslator.FromHtml("#BC8853"), 3);
                g.DrawLine(pen, x + 346, 330, x + 367, 330);
                g.DrawLine(pen, x + 367, 330, x + 358, 322);
                g.DrawLine(pen, x + 367, 330, x + 358, 338);
            }
        }
        string[] captions = ["模型提供语义判断；业务流程负责事实核验、复核与授权。", "Choice、Score、Boolean 是并列原语，均输出结构化判断。", "状态、问题与候选共同参与编码；结果映射回稳定标识。", "GPU 编译在构建期完成；推理程序使用静态驱动绑定。", "前向、反向、更新与保存共同组成训练循环。", "训练进展与独立质量评测分别衡量。", "分布集中度描述相对倾向；可信使用需要评测与校准。", "以明确任务开展受控试点，依据评测逐步扩大应用范围。"];
        string foot = captions[i];
        Text(g, foot, 52, 488, 1480, 70, 22, ColorTranslator.FromHtml("#657080"), false);
        bitmap.Save(Path.Combine(assetRoot, id + "-diagram.png"), ImageFormat.Png);
    }
    if (mode is "covers" or "preview-covers")
    {
        var revised = Path.Combine(assetRoot, id + "-art-v2.png");
        var source = File.Exists(revised) ? revised : Path.Combine(assetRoot, id + "-art.png");
        if (mode == "preview-covers" && (!File.Exists(source) || id == "05")) continue;
        if (!File.Exists(source)) throw new FileNotFoundException("Cover art missing", source);
        using var art = Image.FromFile(source);
        using var cover = new Bitmap(1600, 900);
        using var g = Graphics.FromImage(cover);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var h = art.Width * 0.5625f;
        g.DrawImage(art, new Rectangle(0, 0, 1600, 900), new RectangleF(0, Math.Max(0, (art.Height - h) / 2), art.Width, h), GraphicsUnit.Pixel);
        using var banner = new SolidBrush(Color.FromArgb(246, 248, 249, 245));
        g.FillRectangle(banner, 0, 692, 1600, 208);
        Text(g, "SEZIKA  /  技术专栏 " + id, 55, 716, 1480, 44, 23, ColorTranslator.FromHtml("#187A7C"), true);
        Text(g, subjects[i], 55, 768, 1480, 85, 48, ColorTranslator.FromHtml("#132C45"), true);
        var encoder = ImageCodecInfo.GetImageEncoders().Single(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var settings = new EncoderParameters(1);
        settings.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        cover.Save(Path.Combine(assetRoot, id + "-cover.jpg"), encoder, settings);
    }
}
if (mode is "diagrams" or "covers" or "preview-covers")
{
    var covers = mode != "diagrams";
    using var sheet = new Bitmap(1600, covers ? 1800 : 1200);
    using var g = Graphics.FromImage(sheet);
    g.Clear(Color.White);
    for (var i = 0; i < 8; i++)
    {
        budget.Token.ThrowIfCancellationRequested();
        var path = Path.Combine(assetRoot, (i + 1).ToString("00") + (covers ? "-cover.jpg" : "-diagram.png"));
        if (mode == "preview-covers" && !File.Exists(path)) continue;
        using var image = Image.FromFile(path);
        g.DrawImage(image, (i % 2) * 800, (i / 2) * (covers ? 450 : 300), 800, covers ? 450 : 300);
    }
    sheet.Save(Path.Combine(assetRoot, mode + "-contact.png"), ImageFormat.Png);
}
if (mode == "html")
{
    var items = new List<Article>();
    var files = Directory.GetFiles(root, "0?-*.md").OrderBy(x => x, StringComparer.Ordinal).Take(9).ToArray();
    if (files.Length != 8) throw new InvalidOperationException("Expected exactly eight articles");
    foreach (var file in files)
    {
        budget.Token.ThrowIfCancellationRequested();
        var raw = File.ReadAllText(file);
        var match = Regex.Match(raw, @"\A---\r?\n(?<meta>[\s\S]*?)\r?\n---\r?\n(?<body>[\s\S]*)", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException("Missing metadata");
        var title = Regex.Match(match.Groups["meta"].Value, @"(?m)^title: (.+)$").Groups[1].Value.Trim();
        var description = Regex.Match(match.Groups["meta"].Value, @"(?m)^description: (.+)$").Groups[1].Value.Trim();
        var body = match.Groups["body"].Value;
        var html = ToHtml(body);
        var htmlPath = Path.ChangeExtension(file, ".html");
        File.WriteAllText(htmlPath, "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(title) + "</title></head><body>" + html + "</body></html>", new UTF8Encoding(false));
        var chineseCharacters = body.Count(c => c >= 0x4e00 && c <= 0x9fff);
        if (chineseCharacters < 950) throw new InvalidDataException("Article too short: " + title);
        if (Regex.IsMatch(body, @"提交代码|commit|checkpoint-\d|/data/|\.artifacts|SHA256|监督者|交接|回执|本轮\s*PID", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Internal process content: " + title);
        items.Add(new Article(Path.GetFileNameWithoutExtension(file)[..2], title, description, file, htmlPath, chineseCharacters));
    }
    File.WriteAllText(Path.Combine(root, "articles.json"), JsonSerializer.Serialize(items, ArticleJson.Default.ListArticle), new UTF8Encoding(false));
    Console.WriteLine("Prepared HTML and validated 8 articles.");
}
Console.WriteLine("Completed: " + mode);

static void Text(Graphics g, string value, float x, float y, float w, float h, float size, Color color, bool bold)
{
    using var font = new Font("Microsoft YaHei", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
    using var brush = new SolidBrush(color);
    using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
    g.DrawString(value, font, brush, new RectangleF(x, y, w, h), format);
}

static string ToHtml(string markdown)
{
    var html = new StringBuilder("<section style=\"font-family:Microsoft YaHei,sans-serif;font-size:16px;line-height:1.9;color:#263746;max-width:760px;margin:0 auto;padding:20px;\">");
    var para = new List<string>();
    var code = false;
    var table = false;
    void Flush() { if (para.Count == 0) return; html.Append("<p style=\"margin:16px 0;text-align:justify;\">").Append(Inline(string.Join(" ", para))).Append("</p>"); para.Clear(); }
    foreach (var raw in markdown.Split('\n').Take(500))
    {
        var line = raw.TrimEnd('\r');
        if (line.StartsWith("```", StringComparison.Ordinal)) { Flush(); html.Append(code ? "</code></pre>" : "<pre style=\"white-space:pre-wrap;word-break:break-all;background:#f2f5f6;padding:16px;font-size:13px;line-height:1.7;\"><code>"); code = !code; continue; }
        if (code) { html.Append(WebUtility.HtmlEncode(line)).Append('\n'); continue; }
        if (line.StartsWith('|'))
        {
            Flush(); if (!table) { html.Append("<table style=\"border-collapse:collapse;width:100%;font-size:14px;\"><tbody>"); table = true; }
            if (Regex.IsMatch(line, @"^\|[\s:|\-]+$", RegexOptions.None, TimeSpan.FromSeconds(1))) continue;
            html.Append("<tr>"); foreach (var cell in line.Trim('|').Split('|').Take(8)) html.Append("<td style=\"border:1px solid #d7e0e1;padding:9px;\">").Append(Inline(cell.Trim())).Append("</td>"); html.Append("</tr>"); continue;
        }
        if (table) { html.Append("</tbody></table>"); table = false; }
        if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
        if (line.StartsWith("# ", StringComparison.Ordinal)) { Flush(); html.Append("<h1 style=\"font-size:25px;line-height:1.45;color:#132c45;margin:12px 0 24px;\">").Append(Inline(line[2..])).Append("</h1>"); continue; }
        if (line.StartsWith("## ", StringComparison.Ordinal)) { Flush(); html.Append("<h2 style=\"font-size:21px;color:#187a7c;border-left:4px solid #187a7c;padding-left:12px;margin:30px 0 16px;\">").Append(Inline(line[3..])).Append("</h2>"); continue; }
        var image = Regex.Match(line, @"^!\[(.*?)\]\((.*?)\)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (image.Success) { Flush(); html.Append("<p style=\"margin:24px 0;\"><img style=\"width:100%;height:auto;\" alt=\"").Append(WebUtility.HtmlEncode(image.Groups[1].Value)).Append("\" src=\"").Append(WebUtility.HtmlEncode(image.Groups[2].Value)).Append("\" /></p>"); continue; }
        para.Add(line);
    }
    Flush(); if (table) html.Append("</tbody></table>"); if (code) throw new InvalidDataException("Unclosed code block"); html.Append("</section>"); return html.ToString();
}
static string Inline(string text)
{
    var value = WebUtility.HtmlEncode(text);
    value = Regex.Replace(value, @"`([^`]+)`", "<code style=\"background:#f0f3f4;padding:2px 4px;\">$1</code>", RegexOptions.None, TimeSpan.FromSeconds(1));
    value = Regex.Replace(value, @"\[([^\]]+)\]\((https?://[^)]+)\)", "<a href=\"$2\">$1</a>", RegexOptions.None, TimeSpan.FromSeconds(1));
    return value;
}
internal sealed record Article(string Id, string Title, string Description, string Markdown, string Html, int ChineseCharacters);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Article>))]
internal partial class ArticleJson : JsonSerializerContext;
