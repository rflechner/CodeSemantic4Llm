using System.Net;
using System.Text;
using System.Text.Json;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Mcp.Server.Llm;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

public sealed class OpenAiSymbolSummaryGeneratorTests
{
    [Fact]
    public async Task Sends_entire_declaration_and_XML_docs_to_openai_chat_completions()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var generator = new OpenAiSymbolSummaryGenerator(client);
        var configuration = new SemanticIndexingConfiguration("fr",
            new LlmSummaryConfiguration(true, "http://localhost:1234/v1", "local-test-model"));
        const string source = """
            /// <summary>Documents the method.</summary>
            public int Double(int value) => value * 2;
            """;
        var symbol = new SymbolDefinition("csharp", "method", "Double", "Demo.Double",
            new SourceLocation("Demo.cs", 2, 12));

        var result = await generator.SummarizeAsync(symbol, source, configuration, TestContext.Current.CancellationToken);

        Assert.Equal("First line\nSecond line", result);
        Assert.Equal("http://localhost:1234/v1/chat/completions", handler.Url);
        Assert.Equal("POST", handler.Method);
        using var request = JsonDocument.Parse(handler.Payload!);
        Assert.Equal("local-test-model", request.RootElement.GetProperty("model").GetString());
        var messages = request.RootElement.GetProperty("messages");
        Assert.Contains("language fr", messages[0].GetProperty("content").GetString());
        Assert.Contains("Documents the method.", messages[1].GetProperty("content").GetString());
        Assert.Contains("value * 2", messages[1].GetProperty("content").GetString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Method { get; private set; }
        public string? Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Method = request.Method.Method;
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            var json = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = "First line\nSecond line" } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
