using System.Net;
using System.Text;
using System.Text.Json;
using ahu.MicrosoftMes.Services;
using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

public class DeepSeekReasoningTests
{
    private sealed class Handler : HttpMessageHandler, IHttpClientFactory
    {
        public string Response = "";
        public string Request = "";
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("  思考\n原文  ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ResponseReasoning_RoundTripsInActualHttpPayload(string? reasoning)
    {
        var handler = new Handler { Response = JsonSerializer.Serialize(new { choices = new[] {
            new { message = new { content = "回答", reasoning_content = reasoning } } } }) };
        var client = new DeepSeekClient(handler, Options.Create(new AiOptions {
            ApiKey = "test", Model = "test", BaseUrl = "https://example.invalid" }),
            NullLogger<DeepSeekClient>.Instance, null!);
        var first = await client.ChatAsync(new[] { new DeepSeekChatMessage { Role = "user", Content = "问题" } }, null, default);
        Assert.Equal(reasoning, first.ReasoningContent);
        await client.ChatAsync(new[] { new DeepSeekChatMessage { Role = "assistant", Content = first.Content,
            ReasoningContent = first.ReasoningContent } }, null, default);
        using var body = JsonDocument.Parse(handler.Request);
        var message = body.RootElement.GetProperty("messages")[0];
        Assert.Equal(reasoning != null, message.TryGetProperty("reasoning_content", out var value));
        if (reasoning != null) Assert.Equal(reasoning, value.GetString());
        await client.ConnectionTestAsync(default);
        using var ping = JsonDocument.Parse(handler.Request);
        Assert.Equal("ping", ping.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }
}

