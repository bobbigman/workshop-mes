using System.Text;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

public class SupportDocumentSearchTests
{
    [Theory]
    [InlineData("账上多少钱", "方向一：车间和财务串起来", "金蝶账目还没接进来，现在看不到")]
    [InlineData("材料涨价按新价算", "方向二：报价不拍脑袋", "调用金蝶 MCP 集成")]
    [InlineData("材料涨价按新价算报价", "方向二：报价不拍脑袋", "调用金蝶 MCP 集成")]
    [InlineData("销售报价的时候，AI 能帮着算算料钱吗？", "方向二：报价不拍脑袋", "调用金蝶 MCP 集成")]
    public void IntegrationQuestions_RetrieveCurrentCapabilityBoundary(string question, string chapter, string boundary)
    {
        var result = Build(FindRepoSupportDocs()).Search(question);
        Assert.Equal("ready", result.State);
        Assert.Equal("file", result.RetrievalSource);
        Assert.Contains(result.Sources, s => s.Title.StartsWith("AI 能力展示口径")
            && s.Chapter?.Contains(chapter) == true && s.Excerpt.Contains(boundary));
    }

    [Theory]
    [InlineData("车间干的活，能跟财务的账串起来吗？", "方向一：车间和财务串起来")]
    [InlineData("工人干完活，财务不用再对着 Excel 抄一遍吧？", "方向一：车间和财务串起来")]
    [InlineData("老板能不能一张表看清车间干了多少、账上多少钱？", "方向一：车间和财务串起来")]
    [InlineData("销售报价的时候，AI 能帮着算算料钱吗？", "方向二：报价不拍脑袋")]
    [InlineData("材料这阵子涨价了，报价按新价算吗？", "方向二：报价不拍脑袋")]
    [InlineData("除了金蝶，还能连别的软件吗？", "方向三：还能干嘛")]
    [InlineData("车间出问题，能自动发消息到班组长手机上吗？", "方向三：还能干嘛")]
    [InlineData("以后想加新功能，留路子了吗？", "方向三：还能干嘛")]
    [InlineData("AI 除了查工单，还能帮我们干点啥？", "方向三：还能干嘛")]
    public void ComputerDepartment_VisionQuestions_RetrieveDirectionAndClosing(string question, string chapter)
    {
        var result = Build(FindRepoSupportDocs()).Search(question);
        Assert.Equal("ready", result.State);
        var integration = result.Sources.Where(s => s.Title.StartsWith("AI 能力展示口径")).ToList();
        Assert.Contains(integration, s => s.Chapter?.Contains(chapter) == true);
        Assert.Contains(integration, s => s.Chapter?.Contains("追问收口表") == true
            && s.Excerpt.Contains("配好就能出"));
        Assert.DoesNotContain(integration, s => s.Excerpt.Contains("第一步") || s.Excerpt.Contains("第二步"));
    }

    private static SupportDocumentService Build(string root, SupportDocsOptions? o = null)
    {
        var opt = o ?? new SupportDocsOptions();
        opt.RootPath = root;
        opt.Enabled = true;
        return new SupportDocumentService(Options.Create(new AiOptions { SupportDocs = opt }), NullLogger<SupportDocumentService>.Instance);
    }

    private static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "ws_supportdocs_search_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void Write(string dir, string name, string content)
        => File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));

    private static string FindRepoSupportDocs()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var c = Path.Combine(d.FullName, "SupportDocs");
            if (Directory.Exists(c)) return c;
            d = d.Parent;
        }
        throw new InvalidOperationException("未找到 SupportDocs 目录");
    }

    // ---- 用真实样例目录验证 S02 问题命中正确章节 ----

    [Fact]
    public void Sample_补报_hits_报工与复核()
    {
        var r = Build(FindRepoSupportDocs()).Search("怎么补报");
        Assert.Equal("ready", r.State);
        Assert.Contains(r.Sources, s => s.Title == "报工与复核");
    }

    [Fact]
    public void Sample_删不掉_hits_常见问题()
    {
        var r = Build(FindRepoSupportDocs()).Search("为什么产品删不掉");
        Assert.Equal("ready", r.State);
        Assert.Contains(r.Sources, s => s.Title == "常见问题");
    }

    [Fact]
    public void Sample_报工权限_hits_基础资料或常见问题()
    {
        var r = Build(FindRepoSupportDocs()).Search("报工权限怎么配");
        Assert.Equal("ready", r.State);
        Assert.Contains(r.Sources, s => s.Title is "基础资料" or "常见问题");
    }

    [Fact]
    public void Sample_完成数100和80_hits_数据口径()
    {
        var r = Build(FindRepoSupportDocs()).Search("两道工序报了100和80为什么完成数是80");
        Assert.Equal("ready", r.State);
        Assert.Contains(r.Sources, s => s.Title == "数据口径");
    }

    [Fact]
    public void Sample_报表没算进去_hits_数据口径()
    {
        var r = Build(FindRepoSupportDocs()).Search("已经报工了为什么生产报表还没算进去");
        Assert.Equal("ready", r.State);
        Assert.Contains(r.Sources, s => s.Title == "数据口径");
    }

    // ---- 检索边界 ----

    [Fact]
    public void NoMatch_ReturnsDistinctState()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: 操作\n---\n# 登录\n登录步骤");
            var r = Build(dir).Search("今天天气怎么样");
            Assert.Equal("no_match", r.State);
            Assert.Empty(r.Sources);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Delete_InvalidatesCache_NoStaleSource()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: 补报说明\n---\n# 补报\n如何补报");
            Write(dir, "b.md", "---\ntitle: 其它说明\n---\n# 其它\n无关内容");
            var svc = Build(dir);

            var r1 = svc.Search("怎么补报");
            Assert.Contains(r1.Sources, s => s.Title == "补报说明");

            File.Delete(Path.Combine(dir, "a.md"));
            var r2 = svc.Search("怎么补报");
            Assert.DoesNotContain(r2.Sources, s => s.Title == "补报说明");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Replace_InvalidatesCache_NewContentTakesEffect()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: 旧标题\n---\n# 章节\n旧内容");
            var svc = Build(dir);
            Assert.Contains(svc.Search("章节").Sources, s => s.Title == "旧标题");

            File.WriteAllText(Path.Combine(dir, "a.md"), "---\ntitle: 新标题\n---\n# 章节\n新内容", new UTF8Encoding(false));
            var r = svc.Search("章节");
            Assert.Contains(r.Sources, s => s.Title == "新标题");
            Assert.DoesNotContain(r.Sources, s => s.Title == "旧标题");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingVersion_NotForged()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: 无版本\n---\n# 章节\n正文");
            var r = Build(dir).Search("章节");
            Assert.Equal("ready", r.State);
            Assert.All(r.Sources, s => Assert.Null(s.Version));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void 派工智能推荐怎么用_截断时仍带回总地图()
    {
        var r = Build(FindRepoSupportDocs()).Search("演示一下\"派工智能推荐\"具体怎么用？");
        Assert.Equal("ready", r.State);
        Assert.True(r.Truncated);
        var howTo = r.Sources.Single(s => s.Title == "系统能力总地图" && (s.Chapter ?? "").Contains("派工智能推荐怎么用"));
        Assert.Contains("推荐派给谁", howTo.Excerpt);
        Assert.Contains("保存派工", howTo.Excerpt);
        Assert.Contains("系统能力总地图", r.Message);
        Assert.Contains("不要因为截断就说没查到", r.Message);
    }

    [Fact]
    public void 其它问题_截断时也带回总地图()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "00.md", "---\ntitle: 系统能力总地图\n---\n# 能力总览\n色码汇总在报表菜单，全员可读。");
            for (var i = 0; i < 4; i++)
                Write(dir, $"f{i}.md", $"---\ntitle: 手册{i}\n---\n# 色码\n色码汇总说明 {i}");
            var opt = new SupportDocsOptions { MaxResults = 2 };
            var r = Build(dir, opt).Search("色码汇总在哪");
            Assert.True(r.Truncated);
            Assert.Contains(r.Sources, s => s.Title == "系统能力总地图" && s.Excerpt.Contains("报表菜单"));
            Assert.Contains("系统能力总地图", r.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ManyMatches_Truncated_FlagSet()
    {
        var dir = TempDir();
        try
        {
            for (var i = 0; i < 6; i++)
                Write(dir, $"f{i}.md", $"---\ntitle: 文档{i}\n---\n# 补报\n补报说明 {i}");
            var opt = new SupportDocsOptions { MaxResults = 3 };
            var r = Build(dir, opt).Search("补报");
            Assert.Equal("ready", r.State);
            Assert.True(r.TotalMatched > r.Sources.Count);
            Assert.True(r.Truncated);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ConflictingVersions_Warns()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: 同文档\nversion: 1.0\n---\n# 章节\n内容A");
            Write(dir, "b.md", "---\ntitle: 同文档\nversion: 2.0\n---\n# 章节\n内容B");
            var r = Build(dir).Search("章节");
            Assert.Contains(r.Warnings, w => w.Contains("多个版本"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
