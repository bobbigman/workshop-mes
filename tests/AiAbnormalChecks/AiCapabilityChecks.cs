using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class AiCapabilityChecks
{
    public static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SupportDocs", "19-AI功能价值与卖点问答.md"))) directory = directory.Parent;
        if (directory == null) throw new Exception("未找到AI能力问答原文");
        var content = File.ReadAllText(Path.Combine(directory.FullName, "SupportDocs", "19-AI功能价值与卖点问答.md"));
        var queries = content.Split('\n').Where(line => line.StartsWith("### ")).Select(line => line[4..].Trim()).ToList();
        if (queries.Count != 10) throw new Exception("电脑部门须有10道可检索问题");
        var trainingStart = content.IndexOf("## 七、电脑部门：内部培训与AI办公", StringComparison.Ordinal);
        if (trainingStart < 0) throw new Exception("电脑部门内部培训章节缺失");
        var training = content[trainingStart..];
        if (new[] { "给客户演示", "合作伙伴", "签单", "电脑工程师" }.Any(training.Contains))
            throw new Exception("电脑部门培训仍混入外部推销口气");
        if (!training.Contains("核对") || !training.Contains("演示或测试环境") || !training.Contains("不会自动启动WorkBuddy"))
            throw new Exception("培训核对、隔离练习或外部工具边界缺失");
        var root = Path.Combine(AppContext.BaseDirectory, "training-260-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "19-AI功能价值与卖点问答.md"), content);
            var service = new SupportDocumentService(Options.Create(new AiOptions
            { SupportDocs = new SupportDocsOptions { Enabled = true, RootPath = root } }), NullLogger<SupportDocumentService>.Instance);
            foreach (var query in queries)
            {
                var result = service.Search(query);
                if (result.State != "ready" || !result.Sources.Any(source => source.Chapter == query))
                    throw new Exception("AI能力问题未命中对应章节：" + query);
            }
            var fullLibrary = new SupportDocumentService(Options.Create(new AiOptions
            { SupportDocs = new SupportDocsOptions { Enabled = true, RootPath = Path.Combine(directory.FullName, "SupportDocs") } }), NullLogger<SupportDocumentService>.Instance);
            foreach (var query in queries)
            {
                var result = fullLibrary.Search(query);
                if (result.State != "ready" || !result.Sources.Any(source => source.Title == "AI功能价值与卖点问答" && source.Chapter == query))
                    throw new Exception("完整知识库检索未命中对应章节：" + query);
            }
            foreach (var wrong in new[] { "真工资", "装您内网、不出厂", "页面第一次用会提示", "现在能确定的是基础资料用 Excel 导入、查进度、问工艺、智能客服、WorkBuddy 对接金蝶清洗数据" })
                if (content.Contains(wrong)) throw new Exception("能力资料仍有错误口径：" + wrong);
            Console.WriteLine("PASS: 电脑部门内部培训10题在独立资料和完整知识库逐题命中对应章节；工资/云模型/首次提示/金蝶承诺口径检查通过");
        }
        finally { Directory.Delete(root, true); }
    }
}
