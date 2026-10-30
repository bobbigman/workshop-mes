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
            throw ThrowHelper.Biz(nameof(Extract), "知识库存放目录还没设置。请在后端设置文件 appsettings.json 中添加 KnowledgeBase:RootPath 项，指向知识库文件所在的服务器目录。");

        var fullPath = Path.Combine(root, relativePath);
        if (!File.Exists(fullPath))
            throw ThrowHelper.Biz(nameof(Extract), $"知识库中指定的文件在服务器上不存在：{relativePath}。请检查知识库目录下是否有该文件，确认文件名和路径拼写正确。");

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
            _ => throw ThrowHelper.Biz(nameof(Extract), $"知识库不支持当前文件格式：{fileType}。目前支持 pdf、docx、xlsx、jpg、png、webp，请把文件转成支持的格式后重新上传。")
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
            throw ThrowHelper.Biz(nameof(ExtractDocx), $"文件 {Path.GetFileName(path)} 读取不出文字内容，可能是空文件或已损坏。请确认文件能正常打开且有文字内容后重新上传。");
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
            throw ThrowHelper.Biz(nameof(ExtractPdf), $"文件 {Path.GetFileName(path)} 读取不出文字（可能是扫描图片版 PDF）。请提供可编辑文字的电子版 PDF 后重新上传。");
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
            throw ThrowHelper.Biz(nameof(ExtractExcel), $"文件 {Path.GetFileName(path)} 读取不出内容，可能是空文件。请确认文件中有数据后重新上传。");
        return result;
    }
}
