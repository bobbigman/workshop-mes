using System.Text.Json;
using ahu.MicrosoftMes.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Formatting.Compact;
using Xunit;

namespace WorkshopMes.Tests;

public class ExceptionPresentationTests
{
    private const string Message = "字段“辣度”已有业务数据，不能删除";
    private const string Trace = "presentation-test-trace";

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items[TraceIdMiddleware.ItemKey] = Trace;
        return context;
    }

    private static async Task<JsonElement> ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return doc.RootElement.Clone();
    }

    private static Task DeleteUsedField(HttpContext context) =>
        throw ThrowHelper.Biz(nameof(DeleteUsedField), Message);

    [Fact]
    public async Task BusinessFailure_KeepsResponseContract_AndFileLogStack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mes-presentation-{Guid.NewGuid():N}.log");
        try
        {
            var context = NewContext();
            using (var logger = new LoggerConfiguration().MinimumLevel.Warning()
                .WriteTo.File(new CompactJsonFormatter(), path).CreateLogger())
            using (var factory = LoggerFactory.Create(builder => builder.AddSerilog(logger)))
            {
                await new ExceptionMiddleware(DeleteUsedField,
                    factory.CreateLogger<ExceptionMiddleware>()).InvokeAsync(context);
            }
            var body = await ReadBody(context);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal(1, body.GetProperty("code").GetInt32());
            Assert.Equal(Message, body.GetProperty("msg").GetString());
            Assert.Equal(Trace, context.Response.Headers[TraceIdMiddleware.HeaderName].ToString());
            using var log = JsonDocument.Parse((await File.ReadAllLinesAsync(path)).Single());
            Assert.Equal(Trace, log.RootElement.GetProperty("TraceId").GetString());
            Assert.Contains(nameof(DeleteUsedField), log.RootElement.GetProperty("@x").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CustomBusinessCode_IsPreserved()
    {
        var context = NewContext();
        await new ExceptionMiddleware(_ => throw new BusinessException("业务限制", 42),
            NullLogger<ExceptionMiddleware>.Instance).InvokeAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(42, (await ReadBody(context)).GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TechnicalFailure_ReturnsDetailToClient(bool businessWrapper)
    {
        var context = NewContext();
        await new ExceptionMiddleware(_ => throw (businessWrapper
                ? ThrowHelper.Biz("InternalMethod", "SQL原文: SELECT internal_data")
                : ThrowHelper.General("InternalMethod", "SELECT internal_data")),
            NullLogger<ExceptionMiddleware>.Instance).InvokeAsync(context);
        var body = await ReadBody(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(500, body.GetProperty("code").GetInt32());
        var msg = body.GetProperty("msg").GetString();
        Assert.Contains("SELECT internal_data", msg);
        Assert.DoesNotContain("操作失败，请稍后重试", msg);
        Assert.Equal(Trace, context.Response.Headers[TraceIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task SuccessfulRequest_PassesThrough()
    {
        var context = NewContext();
        await new ExceptionMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("success");
        }, NullLogger<ExceptionMiddleware>.Instance).InvokeAsync(context);
        context.Response.Body.Position = 0;
        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("success", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
