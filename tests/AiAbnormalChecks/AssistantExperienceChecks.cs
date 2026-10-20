using System.Reflection;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class AssistantExperienceChecks
{
    public static void Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "bee-assistant-258-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "operations.md"), "---\ntitle: 报工与复核\n---\n## 现场异常怎么上报\n设备坏了怎么上报：手机底部异常，选择设备故障，填写描述提交。\n");
            File.WriteAllText(Path.Combine(root, "sales.md"), "---\ntitle: 王雩销售应答50问\n---\n## 销售应答守则\n设备坏了怎么上报，销售先反问客户标准，销售话术。\n");
            File.WriteAllText(Path.Combine(root, "readme.md"), "---\ntitle: README\n---\n## 专属排除词\n独有测试关键词\n");
            var service = new SupportDocumentService(Options.Create(new AiOptions
            { SupportDocs = new SupportDocsOptions { Enabled = true, RootPath = root } }), NullLogger<SupportDocumentService>.Instance);
            var operation = service.Search("设备坏了我要怎么上报");
            if (operation.State != "ready" || !operation.Sources.Any(s => s.Title == "报工与复核")
                || operation.Sources.Any(s => s.Title.Contains("销售"))) throw new Exception("操作检索混入销售资料或漏掉步骤");
            if (!service.Search("销售话术").Sources.Any(s => s.Title.Contains("销售"))) throw new Exception("销售问题不应被全局屏蔽");
            if (service.Search("独有测试关键词").Sources.Count != 0) throw new Exception("排除文档不能兜底混回");
            File.WriteAllText(Path.Combine(root, "_retrieval.json"), "{\"version\":\"test\",\"excludeTitles\":[\"报工与复核\",\"README\"]}");
            var custom = service.Search("设备坏了我要怎么上报");
            if (custom.Sources.Any(s => s.Title == "报工与复核" || s.Title == "README")) throw new Exception("外置排除规则失效");
            var prompt = typeof(AiAssistantService).GetMethod("SystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!;
            var text = (string)prompt.Invoke(null, new object[] { "support" })!;
            if (!text.Contains("最多三步") || !text.Contains("目标150字") || !text.Contains("后台核验")) throw new Exception("短答与证据规则缺失");
            Console.WriteLine("PASS: 操作检索排除销售、销售查询可用、默认/外置排除规则、简短回答提示（5项）");
        }
        finally { Directory.Delete(root, true); }
    }
}
