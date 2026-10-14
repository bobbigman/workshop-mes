using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Services;

/// <summary>客服知识目录状态（docs/25 §3.6 的状态区分）。</summary>
public enum SupportDocsState
{
    Disabled,        // 未启用（Enabled=false）
    NotConfigured,   // 未配置目录（RootPath 空）
    EmptyDirectory,  // 目录无 md/txt
    Ready,           // 正常加载（含部分文件失败但仍有可用文件）
    Failed           // 无任何可用文件（全部失败/超限）
}

/// <summary>已加载的客服文档（文件级）。</summary>
public class SupportDocFile
{
    public string SourceId { get; set; } = "";   // 服务端生成，不暴露磁盘路径
    public string RelativePath { get; set; } = ""; // 仅服务端内部用，不进模型/前端
    public string Title { get; set; } = "";
    public string? Role { get; set; }
    public string? Version { get; set; }
    public string? SystemVersion { get; set; }
    public string? UpdatedAt { get; set; }
    public string Content { get; set; } = "";
    public int CharCount { get; set; }
}

/// <summary>加载结果。</summary>
public class SupportDocLoadResult
{
    public SupportDocsState State { get; set; }
    public List<SupportDocFile> Files { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int TotalChars { get; set; }
}

/// <summary>检索返回的单个来源片段（不暴露磁盘路径）。</summary>
public class SupportSourceDto
{
    public string SourceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Chapter { get; set; }
    public string? Version { get; set; }
    public string? UpdatedAt { get; set; }
    public string Excerpt { get; set; } = "";
}

/// <summary>检索结果（含状态区分，供 support 工具与前端使用）。</summary>
public class SupportSearchResult
{
    public string State { get; set; } = ""; // ready/no_match/disabled/not_configured/empty/failed
    public string Message { get; set; } = "";
    public List<SupportSourceDto> Sources { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool Truncated { get; set; }
    public int TotalMatched { get; set; }
    /// <summary>_retrieval.json 的 version；缺文件/坏 JSON 用内置时为 builtin。</summary>
    public string RetrievalVersion { get; set; } = "";
    /// <summary>file = 读到外置规则；builtin = 回落代码默认。</summary>
    public string RetrievalSource { get; set; } = "";
}

/// <summary>FAQ 分组（docs/113：## = 分组）。</summary>
public class FaqGroupDto
{
    public string Group { get; set; } = "";
    public List<FaqQuestionDto> Questions { get; set; } = new();
}

/// <summary>FAQ 单条（docs/113：### = 问题标题，正文 = 口径）。</summary>
public class FaqQuestionDto
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// 智能客服知识目录服务（docs/25 §3、§4）。只读专用发布目录的 UTF-8 md/txt；
/// 有界文件读取、路径逃逸防护、元信息解析、章节检索、指纹缓存失效。
/// </summary>
public class SupportDocumentService
{
    private const int BlockMaxChars = 1500;   // 长章节分块上限
    private const int ExcerptMaxChars = 800;  // 单个来源摘录上限（300→800，减少截断逼模型脑补）
    private const string FaqFileName = "10-系统能力50问.md";
    private const string FaqEmptyHint = "常见问题暂未收录";
    private const string RetrievalFileName = "_retrieval.json"; // docs/2J：与手册同目录，改文件热加载

    private static readonly JsonSerializerOptions RetrievalJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class SupportChapter
    {
        public string SourceId { get; set; } = "";
        public string FileTitle { get; set; } = "";
        public string? Chapter { get; set; }
        public string? Version { get; set; }
        public string? UpdatedAt { get; set; }
        public string Content { get; set; } = "";
    }

    private sealed class BoostRule
    {
        public string[] QueryContains { get; set; } = Array.Empty<string>();
        public string[] ChapterContains { get; set; } = Array.Empty<string>();
        public int Bonus { get; set; }
    }

    /// <summary>可外置检索规则（SupportDocs/_retrieval.json）；缺文件时用代码默认。</summary>
    private sealed class RetrievalRules
    {
        public string Version { get; set; } = "builtin";
        public string Source { get; set; } = "builtin"; // file | builtin
        public string[] KeyPhrases { get; set; } = Array.Empty<string>();
        public Dictionary<string, string[]> Synonyms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludeTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<BoostRule> Boosts { get; set; } = new();
    }

    private sealed class RetrievalRulesFile
    {
        public string? Version { get; set; }
        public string? UpdatedAt { get; set; }
        public string[]? KeyPhrases { get; set; }
        public Dictionary<string, string[]>? Synonyms { get; set; }
        public string[]? ExcludeTitles { get; set; }
        public List<BoostRuleFile>? Boosts { get; set; }
    }

    private sealed class BoostRuleFile
    {
        public string[]? QueryContains { get; set; }
        public string[]? ChapterContains { get; set; }
        public int Bonus { get; set; }
    }

    private sealed class SupportIndex
    {
        public string Fingerprint { get; set; } = "";
        public List<SupportChapter> Chapters { get; set; } = new();
        public RetrievalRules Rules { get; set; } = BuildDefaultRules();
    }

    private readonly SupportDocsOptions _opt;
    private readonly ILogger<SupportDocumentService> _logger;
    private readonly object _lock = new();
    private SupportIndex? _index;

    public SupportDocumentService(IOptions<AiOptions> aiOptions, ILogger<SupportDocumentService> logger)
    {
        _opt = aiOptions.Value.SupportDocs ?? new SupportDocsOptions();
        _logger = logger;
    }

    /// <summary>客服是否启用且已配置目录。</summary>
    public bool IsReady => _opt.Enabled && _opt.IsConfigured;

    /// <summary>解析并规范化客服根目录；未配置或非法路径抛错。</summary>
    public string ResolveRoot()
    {
        if (!_opt.Enabled)
            throw ThrowHelper.Biz(nameof(ResolveRoot), "智能客服未启用（Ai:SupportDocs:Enabled=false）");
        if (!_opt.IsConfigured)
            throw ThrowHelper.Biz(nameof(ResolveRoot), "未配置客服知识目录（Ai:SupportDocs:RootPath 为空）");

        var root = _opt.RootPath.Trim();
        string full;
        if (Path.IsPathRooted(root))
            full = root;
        else
            full = Path.Combine(AppContext.BaseDirectory, root);

        full = Path.GetFullPath(full);
        if (!Directory.Exists(full))
            throw ThrowHelper.Biz(nameof(ResolveRoot), $"客服知识目录不存在：{full}");
        return full;
    }

    /// <summary>
    /// PC 常见问题页（docs/113）：读 10-系统能力50问.md，按 ##=分组、###=问题+正文 解析。
    /// md 缺失/解析失败返回空列表 + 占位文案，不抛错不编造。
    /// </summary>
    public List<FaqGroupDto> GetFaq()
    {
        try
        {
            if (!_opt.IsConfigured) return new List<FaqGroupDto>();
            var root = TryResolveRootPath();
            if (root == null) return new List<FaqGroupDto>();

            var fullPath = Path.GetFullPath(Path.Combine(root, FaqFileName));
            if (!fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("FAQ 路径逃逸拦截 file={File}", FaqFileName);
                return new List<FaqGroupDto>();
            }
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("FAQ 文件不存在 path={Path}", fullPath);
                return new List<FaqGroupDto>();
            }

            var text = ReadUtf8(fullPath);
            var (_, content) = SplitFrontMatter(text);
            return ParseFaqMarkdown(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FAQ 读取/解析失败 file={File}", FaqFileName);
            return new List<FaqGroupDto>();
        }
    }

    /// <summary>FAQ 占位文案（md 缺失或无分组时前端展示）。</summary>
    public static string FaqPlaceholder => FaqEmptyHint;

    /// <summary>解析 RootPath，不要求 Enabled；失败返回 null。</summary>
    private string? TryResolveRootPath()
    {
        if (!_opt.IsConfigured) return null;
        var root = _opt.RootPath.Trim();
        var full = Path.IsPathRooted(root)
            ? root
            : Path.Combine(AppContext.BaseDirectory, root);
        full = Path.GetFullPath(full);
        return Directory.Exists(full) ? full : null;
    }

    /// <summary>## = 分组；### = 问题标题；其余行归入当前问题正文。忽略 # 一级标题。</summary>
    private static List<FaqGroupDto> ParseFaqMarkdown(string content)
    {
        var groups = new List<FaqGroupDto>();
        FaqGroupDto? curGroup = null;
        FaqQuestionDto? curQ = null;
        var body = new StringBuilder();

        void FlushQuestion()
        {
            if (curGroup == null || curQ == null) return;
            curQ.Content = body.ToString().Trim();
            body.Clear();
            if (!string.IsNullOrWhiteSpace(curQ.Title))
                curGroup.Questions.Add(curQ);
            curQ = null;
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var t = line.Trim();
            if (t.StartsWith("### "))
            {
                FlushQuestion();
                if (curGroup == null)
                {
                    curGroup = new FaqGroupDto { Group = "未分组" };
                    groups.Add(curGroup);
                }
                curQ = new FaqQuestionDto { Title = t[4..].Trim() };
                continue;
            }
            if (t.StartsWith("## "))
            {
                FlushQuestion();
                curGroup = new FaqGroupDto { Group = t[3..].Trim() };
                groups.Add(curGroup);
                continue;
            }
            if (t.StartsWith("# "))
                continue; // 文档总标题，忽略
            if (curQ != null)
                body.AppendLine(line);
        }
        FlushQuestion();
        return groups.Where(g => g.Questions.Count > 0).ToList();
    }

    /// <summary>扫描并加载客服文档（S03：有界读取 + 元信息 + 路径安全 + 状态区分）。</summary>
    public SupportDocLoadResult Load()
    {
        var result = new SupportDocLoadResult();

        if (!_opt.Enabled)
        {
            result.State = SupportDocsState.Disabled;
            return result;
        }
        if (!_opt.IsConfigured)
        {
            result.State = SupportDocsState.NotConfigured;
            return result;
        }

        string root;
        try { root = ResolveRoot(); }
        catch (BusinessException bex)
        {
            result.State = SupportDocsState.Failed;
            result.Warnings.Add(bex.Message);
            return result;
        }

        var files = new List<string>();
        CollectFiles(root, files, result.Warnings);

        if (files.Count == 0 && result.Warnings.Count == 0)
        {
            result.State = SupportDocsState.EmptyDirectory;
            return result;
        }

        long totalBytes = 0;
        var docs = new List<SupportDocFile>();
        foreach (var fullPath in files)
        {
            var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            try
            {
                var info = new FileInfo(fullPath);
                if (info.Length > _opt.MaxFileBytes)
                {
                    result.Warnings.Add($"文件「{rel}」超过单文件上限 {_opt.MaxFileBytes} 字节，已跳过");
                    continue;
                }
                if (totalBytes + info.Length > _opt.MaxTotalBytes)
                {
                    result.Warnings.Add($"目录总量超过上限 {_opt.MaxTotalBytes} 字节，「{rel}」及之后不再加载");
                    break;
                }
                totalBytes += info.Length;

                var text = ReadUtf8(fullPath);
                var doc = Parse(rel, text);
                docs.Add(doc);
            }
            catch (DecoderFallbackException)
            {
                result.Warnings.Add($"文件「{rel}」不是有效 UTF-8 编码（错误编码），已跳过");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"文件「{rel}」读取失败：{ex.Message}");
                _logger.LogWarning(ex, "客服文档读取失败 file={File}", rel);
            }
        }

        result.Files = docs;
        result.TotalChars = docs.Sum(d => d.CharCount);

        result.State = docs.Count == 0 ? SupportDocsState.Failed : SupportDocsState.Ready;
        return result;
    }

    /// <summary>
    /// 中文关键词检索（S04）：章节加权、同义词、预算与指纹缓存失效。
    /// 未启用/未配置/空目录/无匹配/失败分别以 State 区分，不抛错也不编造。
    /// </summary>
    public SupportSearchResult Search(string query)
    {
        var result = new SupportSearchResult();
        if (!_opt.Enabled) { result.State = "disabled"; result.Message = "智能客服未启用"; return result; }
        if (!_opt.IsConfigured) { result.State = "not_configured"; result.Message = "未配置客服知识目录"; return result; }
        if (string.IsNullOrWhiteSpace(query)) { result.State = "no_match"; result.Message = "请输入问题"; return result; }

        try
        {
            var idx = EnsureIndex();
            if (idx.Chapters.Count == 0) { result.State = "empty"; result.Message = "客服知识目录为空"; return result; }

            var rules = idx.Rules;
            result.RetrievalVersion = rules.Version;
            result.RetrievalSource = rules.Source;
            var terms = ExtractTerms(query, rules);
            var scored = Score(idx.Chapters, terms, query, rules);
            var matched = scored.Where(s => s.Score > 0).ToList();
            result.TotalMatched = matched.Count;

            if (matched.Count == 0)
            {
                result.State = "no_match";
                result.Message = "没有匹配的操作说明资料";
                return result;
            }

            // 预算：具体手册占满前 N 条；排除标题（默认 README）不进证据；总地图不占名额，截断后另附最贴题的一节。
            var budget = Math.Max(1, _opt.MaxEvidenceChars);
            var used = 0;
            var taken = 0;
            var specific = matched.Where(m => !IsCapabilityMap(m.Chapter) && !IsExcludedTitle(m.Chapter, rules)).ToList();
            var ranked = specific.Count > 0 ? specific : matched.Where(m => !IsExcludedTitle(m.Chapter, rules)).ToList();
            if (ranked.Count == 0) ranked = matched;
            foreach (var m in ranked.Take(_opt.MaxResults))
            {
                if (used >= budget) { result.Truncated = true; break; }
                var excerpt = ExcerptAround(m.Chapter.Content, query, rules);
                if (used + excerpt.Length > budget && taken > 0) { result.Truncated = true; break; }
                used += excerpt.Length;
                result.Sources.Add(new SupportSourceDto
                {
                    SourceId = m.Chapter.SourceId,
                    Title = m.Chapter.FileTitle,
                    Chapter = m.Chapter.Chapter,
                    Version = m.Chapter.Version,
                    UpdatedAt = m.Chapter.UpdatedAt,
                    Excerpt = excerpt
                });
                taken++;
            }
            if (result.TotalMatched > taken) result.Truncated = true;

            var pinnedMap = PinCapabilityMap(query, matched, result, rules);

            if (result.Truncated && pinnedMap)
                result.Message = "检索命中较多，正文已截断。来源里《系统能力总地图》用来兜底：写了操作步骤就按该节回答；只问有没有、在哪、谁能用，也按总地图回答。不要编菜单，不要因为截断就说没查到。";
            else if (result.Truncated)
                result.Message = "检索命中较多，正文已截断；如需完整内容，可缩小问题或追问具体章节。";

            AddConflictWarning(result.Sources, idx.Chapters, result.Warnings);
            result.State = "ready";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "客服检索失败 query={Query}", query);
            result.State = "failed";
            result.Message = $"客服检索失败：{ex.Message}";
            return result;
        }
    }

    private static bool IsCapabilityMap(SupportChapter chapter) =>
        chapter.FileTitle.Contains("系统能力总地图", StringComparison.Ordinal);

    /// <summary>排除标题（默认 README）不占证据名额，避免目录行挤掉真正答案章节。</summary>
    private static bool IsExcludedTitle(SupportChapter chapter, RetrievalRules rules) =>
        rules.ExcludeTitles.Contains(chapter.FileTitle);

    /// <summary>
    /// 其它文档占满前 N 条时，仍带回《系统能力总地图》里最贴题的一节，不占 MaxResults 名额。
    /// 问怎么用/演示时，优先标题含「怎么用」的节。
    /// </summary>
    private static bool PinCapabilityMap(
        string query,
        List<(SupportChapter Chapter, int Score)> matched,
        SupportSearchResult result,
        RetrievalRules rules)
    {
        var map = matched
            .Where(m => m.Score > 0 && IsCapabilityMap(m.Chapter))
            .ToList();
        if (map.Count == 0) return false;

        var pool = map;
        if (QueryWantsSteps(query))
        {
            var howTo = map.Where(m => (m.Chapter.Chapter ?? "").Contains("怎么用", StringComparison.Ordinal)).ToList();
            if (howTo.Count > 0) pool = howTo;
        }

        var best = pool
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Chapter.Chapter)
            .First();
        if (result.Sources.Any(s => s.SourceId == best.Chapter.SourceId)) return true;

        result.Sources.Add(new SupportSourceDto
        {
            SourceId = best.Chapter.SourceId,
            Title = best.Chapter.FileTitle,
            Chapter = best.Chapter.Chapter,
            Version = best.Chapter.Version,
            UpdatedAt = best.Chapter.UpdatedAt,
            Excerpt = ExcerptAround(best.Chapter.Content, query, rules)
        });
        return true;
    }

    private static bool QueryWantsSteps(string query) =>
        query.Contains("怎么用", StringComparison.Ordinal)
        || query.Contains("演示", StringComparison.Ordinal)
        || query.Contains("怎么点", StringComparison.Ordinal)
        || query.Contains("哪个按钮", StringComparison.Ordinal)
        || query.Contains("哪个菜单", StringComparison.Ordinal);

    /// <summary>长章节摘录从问句里最长的关键词附近截，避免只看到节首/表格开头。</summary>
    private static string ExcerptAround(string content, string query, RetrievalRules rules)
    {
        if (content.Length <= ExcerptMaxChars) return content;
        var idx = -1;
        var hitLen = 0;
        foreach (var kp in rules.KeyPhrases.OrderByDescending(k => k.Length))
        {
            if (!query.Contains(kp, StringComparison.OrdinalIgnoreCase)) continue;
            var at = content.IndexOf(kp, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            // 更长短语优先；同长度取更靠前出现处
            if (kp.Length > hitLen || (kp.Length == hitLen && (idx < 0 || at < idx)))
            {
                idx = at;
                hitLen = kp.Length;
            }
        }
        if (idx < 0) idx = 0;
        var start = Math.Max(0, idx - 120);
        var len = Math.Min(ExcerptMaxChars, content.Length - start);
        var slice = content.Substring(start, len);
        if (start > 0) slice = "..." + slice;
        if (start + len < content.Length) slice += "...";
        return slice;
    }

    /// <summary>确保索引与目录一致：文件列表/长度/修改时间变化即重建；并发下不返回半成品。</summary>
    private SupportIndex EnsureIndex()
    {
        lock (_lock)
        {
            var root = ResolveRoot();
            var fingerprint = ComputeFingerprint(root);
            if (_index != null && _index.Fingerprint == fingerprint)
                return _index;

            var load = Load();
            var chapters = new List<SupportChapter>();
            foreach (var doc in load.Files)
            {
                foreach (var ch in SplitChapters(doc))
                    chapters.Add(ch);
            }
            var rules = LoadRetrievalRules(root);
            var idx = new SupportIndex { Fingerprint = fingerprint, Chapters = chapters, Rules = rules };
            _index = idx;
            return idx;
        }
    }

    private string ComputeFingerprint(string root)
    {
        var files = new List<string>();
        var warnings = new List<string>();
        CollectFiles(root, files, warnings);
        var sb = new StringBuilder();
        foreach (var fullPath in files)
        {
            var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            var info = new FileInfo(fullPath);
            sb.Append(rel).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        // _retrieval.json 不进 md 索引，但必须参与指纹，否则改规则不会热加载
        AppendFileFingerprint(sb, root, RetrievalFileName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static void AppendFileFingerprint(StringBuilder sb, string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        if (!File.Exists(path))
        {
            sb.Append(fileName).Append("|missing\n");
            return;
        }
        var info = new FileInfo(path);
        sb.Append(fileName).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
    }

    private RetrievalRules LoadRetrievalRules(string root)
    {
        var path = Path.Combine(root, RetrievalFileName);
        if (!File.Exists(path))
            return BuildDefaultRules();

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<RetrievalRulesFile>(json, RetrievalJsonOpts);
            if (dto == null)
            {
                _logger.LogWarning("客服检索规则文件为空，使用默认规则 file={File}", RetrievalFileName);
                return BuildDefaultRules();
            }
            return MergeRules(dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "客服检索规则解析失败，使用默认规则 file={File}", RetrievalFileName);
            return BuildDefaultRules();
        }
    }

    private static RetrievalRules MergeRules(RetrievalRulesFile dto)
    {
        var rules = BuildDefaultRules();
        rules.Source = "file";
        rules.Version = string.IsNullOrWhiteSpace(dto.Version) ? "file-unversioned" : dto.Version.Trim();
        if (dto.KeyPhrases is { Length: > 0 })
            rules.KeyPhrases = dto.KeyPhrases.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
        if (dto.Synonyms is { Count: > 0 })
        {
            rules.Synonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dto.Synonyms)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                rules.Synonyms[kv.Key.Trim()] = kv.Value
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToArray();
            }
        }
        if (dto.ExcludeTitles != null)
        {
            rules.ExcludeTitles = new HashSet<string>(
                dto.ExcludeTitles.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }
        if (dto.Boosts != null)
        {
            rules.Boosts = dto.Boosts
                .Where(b => b != null && b.Bonus != 0)
                .Select(b => new BoostRule
                {
                    QueryContains = (b.QueryContains ?? Array.Empty<string>())
                        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray(),
                    ChapterContains = (b.ChapterContains ?? Array.Empty<string>())
                        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray(),
                    Bonus = b.Bonus
                })
                .Where(b => b.QueryContains.Length > 0 && b.ChapterContains.Length > 0)
                .ToList();
        }
        return rules;
    }

    private static List<SupportChapter> SplitChapters(SupportDocFile doc)
    {
        var chapters = new List<SupportChapter>();
        var ext = Path.GetExtension(doc.RelativePath).ToLowerInvariant();
        var units = SplitToUnits(doc.Content, ext);

        var seq = 0;
        foreach (var (title, body) in units)
        {
            if (string.IsNullOrWhiteSpace(body)) continue;
            // 长章节分块
            var blocks = Chunk(body, BlockMaxChars);
            var i = 0;
            foreach (var block in blocks)
            {
                i++;
                seq++;
                var anchor = title != null ? SlugAnchor(title, seq) : "p" + seq;
                chapters.Add(new SupportChapter
                {
                    SourceId = $"{doc.SourceId}#{anchor}",
                    FileTitle = doc.Title,
                    Chapter = title,
                    Version = doc.Version,
                    UpdatedAt = doc.UpdatedAt,
                    Content = block
                });
            }
        }
        return chapters;
    }

    private static List<(string? Title, string Body)> SplitToUnits(string content, string ext)
    {
        var units = new List<(string?, string)>();
        if (ext == ".md")
        {
            string? curTitle = null;
            var sb = new StringBuilder();
            var started = false;
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var t = line.Trim();
                if (t.StartsWith('#'))
                {
                    if (started) units.Add((curTitle, sb.ToString()));
                    curTitle = t.TrimStart('#').Trim();
                    sb.Clear();
                    started = true;
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
            if (started || sb.Length > 0) units.Add((curTitle, sb.ToString()));
            if (units.Count == 0) units.Add((null, content));
        }
        else
        {
            foreach (var p in content.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = p.Trim();
                if (t.Length > 0) units.Add((null, t));
            }
            if (units.Count == 0) units.Add((null, content));
        }
        return units;
    }

    private static IEnumerable<string> Chunk(string text, int max)
    {
        if (text.Length <= max) { yield return text; yield break; }
        var pos = 0;
        while (pos < text.Length)
        {
            var len = Math.Min(max, text.Length - pos);
            yield return text.Substring(pos, len);
            pos += len;
        }
    }

    private static string SlugAnchor(string title, int seq)
    {
        var sb = new StringBuilder();
        foreach (var ch in title)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
        }
        var s = sb.ToString();
        if (s.Length == 0) s = "ch";
        if (s.Length > 16) s = s[..16];
        return $"{s}_{seq}";
    }

    // ---- 检索：关键词提取 + 同义词 + 加权评分（可调数据见 SupportDocs/_retrieval.json）----

    private static readonly HashSet<string> StopBigrams = new(StringComparer.Ordinal)
    {
        "怎么", "如何", "什么", "为什", "什么", "一个", "一下", "在哪", "哪里", "哪些", "这个", "那个", "是不是"
    };

    private static RetrievalRules BuildDefaultRules() => new()
    {
        Version = "builtin",
        Source = "builtin",
        KeyPhrases = new[]
        {
            "一键补报", "批量补报", "补报", "删不掉", "删除", "报工权限", "权限", "复核", "退回",
            "下工单", "下单", "创建工单", "打印", "二维码", "流转卡", "账号", "用户", "完成数",
            "生产报表", "入账", "报表", "交期", "状态", "工序", "工艺路线", "产品", "工单", "单位",
            "不良品项", "部门", "登录", "密码", "看板", "扫码", "报工", "PDA",
            "自动派工", "派工", "排产", "排程", "按工时比例", "工时比例", "按工时", "工时", "分摊", "多人",
            "约定比例", "无人派工", "共同报工", "自动分账"
        },
        Synonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["补报"] = new[] { "补报", "批量补报", "一键补报" },
            ["删不掉"] = new[] { "删除", "删" },
            ["删除"] = new[] { "删除", "删" },
            ["权限"] = new[] { "权限", "报工权限" },
            ["复核"] = new[] { "复核", "审核", "通过", "退回" },
            ["退回"] = new[] { "退回", "复核" },
            ["下工单"] = new[] { "下单", "创建工单", "工单" },
            ["下单"] = new[] { "下单", "创建工单", "工单" },
            ["创建工单"] = new[] { "创建工单", "下单", "工单" },
            ["打印"] = new[] { "打印", "二维码", "流转卡" },
            ["账号"] = new[] { "账号", "用户" },
            ["用户"] = new[] { "用户", "账号" },
            ["完成数"] = new[] { "完成", "完成数", "进度" },
            ["报表"] = new[] { "报表", "生产报表", "入账" },
            ["入账"] = new[] { "入账", "报表", "生产报表" },
            ["工单"] = new[] { "工单", "下单", "创建工单" },
            ["扫码"] = new[] { "扫码", "报工", "PDA", "二维码" },
            ["报工"] = new[] { "报工", "扫码", "提交报工" },
            ["PDA"] = new[] { "PDA", "扫码", "扫码头" },
            ["自动派工"] = new[] { "自动派工", "派工", "无人派工", "自动分派", "班组长不用派工" },
            ["派工"] = new[] { "派工", "自动派工", "无人派工", "手动派工", "派给" },
            ["排产"] = new[] { "排产", "排程", "排优先级", "建议开工顺序", "排顺序" },
            ["工时"] = new[] { "工时", "按工时", "时长", "报工时长" },
            ["按工时"] = new[] { "按工时", "按工时比例", "工时比例", "按报工时长", "按工时加权" },
            ["工时比例"] = new[] { "工时比例", "按工时比例", "按工时", "按报工时长加权" },
            ["按工时比例"] = new[] { "按工时比例", "工时比例", "按工时", "按报工时长加权" },
            ["分摊"] = new[] { "分摊", "分账", "按工时加权", "平均分摊", "多人分摊" },
            ["共同报工"] = new[] { "共同报工", "多人共同报工", "多人分摊" }
        },
        ExcludeTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "README" },
        Boosts = new List<BoostRule>
        {
            new()
            {
                QueryContains = new[] { "工时比例", "按工时比例", "按工时分摊", "自动分配报工" },
                ChapterContains = new[] { "按工时比例", "多人分摊", "Q13", "两人协作的产量" },
                Bonus = 80
            }
        }
    };

    private static List<string> ExtractTerms(string query, RetrievalRules rules)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var q = query.Trim();

        // 1) 已知关键短语命中（语义最强）
        foreach (var kp in rules.KeyPhrases)
            if (q.Contains(kp, StringComparison.OrdinalIgnoreCase))
                terms.Add(kp);

        // 2) ASCII 词
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(q, @"[A-Za-z0-9]+"))
            terms.Add(m.Value);

        // 3) 中文二元词（过滤停用词）
        for (var i = 0; i + 1 < q.Length; i++)
        {
            if (q[i] == ' ' || q[i + 1] == ' ') continue;
            var bg = q.Substring(i, 2);
            if (IsCjk(bg[0]) && IsCjk(bg[1]) && !StopBigrams.Contains(bg))
                terms.Add(bg);
        }

        // 4) 全文（用于标题精确命中）
        if (q.Length <= 32) terms.Add(q);

        // 同义词扩展
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in terms)
        {
            expanded.Add(t);
            if (rules.Synonyms.TryGetValue(t, out var syns))
                foreach (var s in syns) expanded.Add(s);
        }

        var list = expanded.Where(t => t.Length > 0).ToList();
        if (list.Count > 64) list = list.Take(64).ToList();
        return list;
    }

    private static bool IsCjk(char c) => c >= 0x4e00 && c <= 0x9fff;

    private static List<(SupportChapter Chapter, int Score)> Score(
        List<SupportChapter> chapters,
        List<string> terms,
        string query,
        RetrievalRules rules)
    {
        var scored = new List<(SupportChapter Chapter, int Score)>();
        foreach (var ch in chapters)
        {
            var score = 0;
            var titleText = (ch.FileTitle + " " + (ch.Chapter ?? "")).Trim();
            foreach (var t in terms)
            {
                var titleHits = CountOccurrences(titleText, t);
                var bodyHits = CountOccurrences(ch.Content, t);
                score += titleHits * 3 + bodyHits;
            }
            score += ApplyBoosts(query, titleText, rules);
            if (score > 0) scored.Add((ch, score));
        }
        return scored.OrderByDescending(s => s.Score)
                     .ThenBy(s => s.Chapter.FileTitle)
                     .ToList();
    }

    private static int ApplyBoosts(string query, string titleText, RetrievalRules rules)
    {
        var bonus = 0;
        foreach (var b in rules.Boosts)
        {
            if (!b.QueryContains.Any(q => query.Contains(q, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!b.ChapterContains.Any(c => titleText.Contains(c, StringComparison.OrdinalIgnoreCase)))
                continue;
            bonus += b.Bonus;
        }
        return bonus;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private static void AddConflictWarning(List<SupportSourceDto> sources, List<SupportChapter> all, List<string> warnings)
    {
        var byTitle = sources.GroupBy(s => s.Title, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byTitle)
        {
            var versions = g.Select(s => s.Version).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
            if (versions.Count > 1)
                warnings.Add($"文档「{g.Key}」存在多个版本（{string.Join("、", versions)}），请人工确认有效版本");
        }
        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s.Version))
                s.Version = null; // 前端/工具渲染「未标注版本」，不伪造版本号
        }
    }

    // ---- S03 内部实现 ----

    /// <summary>递归收集 md/txt；跳过目录联接/符号链接等重解析点，越出根目录即拒绝。</summary>
    private void CollectFiles(string root, List<string> files, List<string> warnings)
    {
        if (files.Count >= _opt.MaxFiles) return;

        foreach (var dir in SafeEnumerateDirectories(root))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            {
                warnings.Add($"跳过目录联接/符号链接：{Path.GetFileName(dir)}");
                continue;
            }
            CollectFiles(dir, files, warnings);
            if (files.Count >= _opt.MaxFiles) break;
        }

        foreach (var file in SafeEnumerateFiles(root))
        {
            if (files.Count >= _opt.MaxFiles)
            {
                warnings.Add($"文件数超过上限 {_opt.MaxFiles}，其余不再加载");
                break;
            }
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".md" or ".txt")) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                warnings.Add($"跳过文件符号链接：{Path.GetFileName(file)}");
                continue;
            }
            files.Add(file);
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch (Exception ex) { throw ThrowHelper.Biz(nameof(SafeEnumerateDirectories), $"枚举子目录失败：{ex.Message}"); }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        try { return Directory.EnumerateFiles(root); }
        catch (Exception ex) { throw ThrowHelper.Biz(nameof(SafeEnumerateFiles), $"枚举文件失败：{ex.Message}"); }
    }

    private static string ReadUtf8(string fullPath)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        // 严格 UTF-8：非法字节抛 DecoderFallbackException；自动识别并剥离 BOM
        using var sr = new StreamReader(fs, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        return sr.ReadToEnd();
    }

    private static SupportDocFile Parse(string relativePath, string text)
    {
        var (meta, content) = SplitFrontMatter(text);
        var sourceId = "doc_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)))[..10].ToLowerInvariant();

        return new SupportDocFile
        {
            SourceId = sourceId,
            RelativePath = relativePath,
            Title = GetMeta(meta, "title") ?? Path.GetFileNameWithoutExtension(relativePath),
            Role = GetMeta(meta, "role"),
            Version = GetMeta(meta, "version"),
            SystemVersion = GetMeta(meta, "systemVersion"),
            UpdatedAt = GetMeta(meta, "updatedAt"),
            Content = content,
            CharCount = content.Length
        };
    }

    private static (Dictionary<string, string> Meta, string Content) SplitFrontMatter(string text)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!text.StartsWith("---")) return (meta, text);

        var lines = text.Split('\n');
        var endLine = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            var l = lines[i].Trim().TrimEnd('\r');
            if (l is "---" or "...")
            {
                endLine = i;
                break;
            }
        }
        if (endLine < 0) return (meta, text);

        for (var i = 1; i < endLine; i++)
        {
            var t = lines[i].Trim().TrimEnd('\r');
            var ci = t.IndexOf(':');
            if (ci <= 0) continue;
            var k = t[..ci].Trim();
            var v = t[(ci + 1)..].Trim();
            meta[k] = v;
        }

        var content = string.Join("\n", lines[(endLine + 1)..]).TrimStart('\r', '\n');
        return (meta, content);
    }

    private static string? GetMeta(Dictionary<string, string> meta, string key)
    {
        if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim();
        return null;
    }
}
