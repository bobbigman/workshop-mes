using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Services;

/// <summary>
/// 制造知识库「翻译官」（docs/48 §6.3）：把 docx / pdf / xlsx 解析成文字。
/// 【业务背景】大模型不会读二进制文件，这里负责把文件现读成文字；只读文件、不落库。
///             带内存缓存（键 = 相对路径 + 文件最后修改时间），文件没变不重翻。
///             扫描型 pdf 抽不出文字时明确报错，不静默造假（对齐异常处理规约）。
/// </summary>
public class KnowledgeFileParser
{
    private readonly IConfiguration _config;
    private readonly object _lock = new();
    // relativePath -> (缓存键, 文字内容)
    private readonly Dictionary<string, (string Key, string Content)> _cache = new();

    public KnowledgeFileParser(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>把相对路径文件解析成文字。文件不存在/解析失败一律抛错，不返回空串装成功。</summary>
    public string Extract(string relativePath, string fileType)
    {
        var root = (_config["KnowledgeBase:RootPath"] ?? "").Trim();
        if (string.IsNullOrEmpty(root))
            throw ThrowHelper.Biz(nameof(Extract), "未配置 KnowledgeBase:RootPath");

        var fullPath = Path.Combine(root, relativePath);
        if (!File.Exists(fullPath))
            throw ThrowHelper.Biz(nameof(Extract), $"知识库文件不存在：{relativePath}");

        var cacheKey = $"{relativePath}|{File.GetLastWriteTimeUtc(fullPath).Ticks}";

        lock (_lock)
        {
            if (_cache.TryGetValue(relativePath, out var hit) && hit.Key == cacheKey)
                return hit.Content;
        }

        var content = fileType.ToLowerInvariant() switch
        {
            "docx" => ExtractDocx(fullPath),
            "pdf" => ExtractPdf(fullPath),
            "excel" or "xlsx" => ExtractExcel(fullPath),
            _ => throw ThrowHelper.Biz(nameof(Extract), $"不支持的文件类型：{fileType}")
        };

        lock (_lock)
        {
            _cache[relativePath] = (cacheKey, content);
        }
        return content;
    }

    private static string ExtractDocx(string path)
    {
        var sb = new StringBuilder();
        using (var doc = WordprocessingDocument.Open(path, false))
        {
            var body = doc.MainDocumentPart?.Document.Body;
            if (body != null)
            {
                foreach (var text in body.Descendants<Text>())
                    sb.AppendLine(text.Text);
            }
        }
        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
            throw ThrowHelper.Biz(nameof(ExtractDocx), $"docx 未解析出文字，文件可能为空或损坏：{Path.GetFileName(path)}");
        return result;
    }

    private static string ExtractPdf(string path)
    {
        var sb = new StringBuilder();
        using (var pdf = PdfDocument.Open(path))
        {
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
        }
        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
            throw ThrowHelper.Biz(nameof(ExtractPdf), $"pdf 未解析出文字（可能为扫描件），请提供电子版：{Path.GetFileName(path)}");
        return result;
    }

    private static string ExtractExcel(string path)
    {
        var sb = new StringBuilder();
        using (var wb = new XLWorkbook(path))
        {
            foreach (var ws in wb.Worksheets)
            {
                sb.AppendLine($"[工作表]{ws.Name}");
                foreach (var row in ws.RowsUsed())
                {
                    var cells = row.CellsUsed().Select(c => c.GetFormattedString());
                    sb.AppendLine(string.Join("\t", cells));
                }
            }
        }
        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
            throw ThrowHelper.Biz(nameof(ExtractExcel), $"excel 未解析出内容，文件可能为空：{Path.GetFileName(path)}");
        return result;
    }
}
