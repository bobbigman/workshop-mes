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
    [InlineData("咱们车间的数据，能跟金蝶打通做业财一体化吗？", "方向一：业财打通")]
    [InlineData("车间干完的活，能不能自动进金蝶，财务不用再抄一遍？", "方向一：业财打通")]
    [InlineData("老板想一张表看全车间的生产和钱，系统撑得起来吗？", "方向一：业财打通")]
    [InlineData("销售报价时，AI 能不能帮着算材料成本？", "方向二：成本报价")]
    [InlineData("产品 BOM 改了，AI 算成本能不能跟着变？", "方向二：成本报价")]
    [InlineData("材料涨价了，报价时 AI 能不能按最新行情算？", "方向二：成本报价")]
    [InlineData("除了金蝶，这系统还能连别的管理软件吗？", "方向三：扩展兼容")]
    [InlineData("车间出异常，能不能自动推到班组长手机上？", "方向三：扩展兼容")]
    [InlineData("我们以后想自己加功能、接自己的系统，口子留好了吗？", "方向三：扩展兼容")]
    [InlineData("AI 除了查工单，还能帮 IT 部门干点别的活吗？", "方向三：扩展兼容")]
    public void ComputerDepartment_VisionQuestions_RetrieveDirectionAndBoundary(string question, string chapter)
    {
        var result = Build(FindRepoSupportDocs()).Search(question);
        Assert.Equal("ready", result.State);
        var integration = result.Sources.Where(s => s.Title.StartsWith("业财打通远景口径")).ToList();
        Assert.Contains(integration, s => s.Chapter?.Contains(chapter) == true);
        Assert.Contains(integration, s => s.Chapter?.Contains("边界：不教操作、不承诺一键出数") == true
            && s.Excerpt.Contains("当前聊天没有相应执行工具"));
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
