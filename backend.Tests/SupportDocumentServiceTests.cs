using System.Text;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

public class SupportDocumentServiceTests
{
    private static SupportDocumentService Build(string root, SupportDocsOptions? overrides = null)
    {
        var opt = overrides ?? new SupportDocsOptions();
        opt.RootPath = root;
        opt.Enabled = true;
        var ai = new AiOptions { SupportDocs = opt };
        return new SupportDocumentService(Options.Create(ai), NullLogger<SupportDocumentService>.Instance);
    }

    private static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "ws_supportdocs_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void Write(string dir, string name, string content)
        => File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));

    [Fact]
    public void Disabled_ReturnsDisabledState()
    {
        var ai = new AiOptions { SupportDocs = new SupportDocsOptions { Enabled = false, RootPath = TempDir() } };
        var svc = new SupportDocumentService(Options.Create(ai), NullLogger<SupportDocumentService>.Instance);
        Assert.Equal(SupportDocsState.Disabled, svc.Load().State);
    }

    [Fact]
    public void NotConfigured_ReturnsNotConfiguredState()
    {
        var ai = new AiOptions { SupportDocs = new SupportDocsOptions { Enabled = true, RootPath = "" } };
        var svc = new SupportDocumentService(Options.Create(ai), NullLogger<SupportDocumentService>.Instance);
        Assert.Equal(SupportDocsState.NotConfigured, svc.Load().State);
    }

    [Fact]
    public void EmptyDirectory_ReturnsEmptyState()
    {
        var dir = TempDir();
        try
        {
            var r = Build(dir).Load();
            Assert.Equal(SupportDocsState.EmptyDirectory, r.State);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NormalFiles_Loaded_WithMetadata()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: 操作入门\nversion: 1.2\nupdatedAt: 2026-09-20\n---\n# 正文\n登录步骤");
            Write(dir, "b.txt", "纯文本文件，没有 front matter");
            Write(dir, "c.docx", "应被忽略");
            var r = Build(dir).Load();
            Assert.Equal(SupportDocsState.Ready, r.State);
            Assert.Equal(2, r.Files.Count);
            var a = r.Files.First(f => f.Title == "操作入门");
            Assert.Equal("1.2", a.Version);
            Assert.Equal("2026-09-20", a.UpdatedAt);
            Assert.False(string.IsNullOrEmpty(a.SourceId));
            Assert.DoesNotContain(dir, a.SourceId); // 不暴露磁盘路径
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WrongEncoding_SkippedWithWarning()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "ok.md", "---\ntitle: ok\n---\n正文");
            File.WriteAllBytes(Path.Combine(dir, "bad.txt"), new byte[] { 0x48, 0x69, 0xFF, 0xFE, 0x00 });
            var r = Build(dir).Load();
            Assert.Equal(SupportDocsState.Ready, r.State);
            Assert.Single(r.Files);
            Assert.Contains(r.Warnings, w => w.Contains("不是有效 UTF-8"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OversizedFile_SkippedWithWarning()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "big.md", "---\ntitle: big\n---\n" + new string('x', 200));
            var opt = new SupportDocsOptions { MaxFileBytes = 50 };
            var r = Build(dir, opt).Load();
            Assert.Equal(SupportDocsState.Failed, r.State);
            Assert.Empty(r.Files);
            Assert.Contains(r.Warnings, w => w.Contains("单文件上限"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TotalOverLimit_WarnsAndStops()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "a.md", "---\ntitle: a\n---\n1");
            Write(dir, "b.md", "---\ntitle: b\n---\n2");
            var opt = new SupportDocsOptions { MaxTotalBytes = 20 };
            var r = Build(dir, opt).Load();
            Assert.Single(r.Files);
            Assert.Contains(r.Warnings, w => w.Contains("总量超过上限"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DirectoryJunction_IsSkipped()
    {
        var dir = TempDir();
        var outside = TempDir();
        try
        {
            Write(dir, "inside.md", "---\ntitle: inside\n---\n正文");
            File.WriteAllText(Path.Combine(outside, "outside.md"), "---\ntitle: outside\n---\n越界文件");

            var junction = Path.Combine(dir, "link");
            var ok = TryCreateJunction(junction, outside);
            if (!ok) return; // 环境不支持创建联接时跳过，不作为失败

            var r = Build(dir).Load();
            Assert.Equal(SupportDocsState.Ready, r.State);
            Assert.DoesNotContain(r.Files, f => f.Title == "outside");
            Assert.Contains(r.Warnings, w => w.Contains("目录联接") || w.Contains("符号链接"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { Directory.Delete(outside, true); } catch { }
        }
    }

    private static bool TryCreateJunction(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{target}\"");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using var p = System.Diagnostics.Process.Start(psi);
                p!.WaitForExit();
                return Directory.Exists(linkPath) && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }
    }
}
