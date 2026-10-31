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

    [Theory]
    [InlineData("inline-token", false, null, "inline-token")]
    [InlineData("inline-token", true, "environment-token", "inline-token")]
    [InlineData("inline-token", true, null, "inline-token")]
    [InlineData(null, true, "environment-token", "environment-token")]
    [InlineData("", true, "environment-token", "environment-token")]
    [InlineData("   ", true, "environment-token", "environment-token")]
    [InlineData(null, false, null, null)]
    public async Task Inline_api_key_takes_priority_over_optional_environment_authentication(
        string? apiKey, bool useEnvironment, string? environmentKey, string? expectedKey)
    {
        var variable = "CODESEMANTIC4LLM_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, environmentKey);

        try
        {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var generator = new OpenAiSymbolSummaryGenerator(client);
            var configuration = new SemanticIndexingConfiguration(llm: new LlmSummaryConfiguration(
                true, "http://localhost:1234/v1", "local-test-model",
                useEnvironment ? variable : null, apiKey));
            var symbol = new SymbolDefinition("csharp", "method", "Run", "Demo.Run",
                new SourceLocation("Demo.cs", 1, 1));

            await generator.SummarizeAsync(symbol, "public void Run() {}", configuration,
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedKey is null ? null : "Bearer " + expectedKey, handler.Authorization);
            if (expectedKey is not null)
            {
                Assert.DoesNotContain(expectedKey, handler.Payload!);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_environment_key_without_an_inline_key_fails_before_sending(string? environmentKey)
    {
        var variable = "CODESEMANTIC4LLM_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, environmentKey);

        try
        {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var generator = new OpenAiSymbolSummaryGenerator(client);
            var configuration = new SemanticIndexingConfiguration(llm: new LlmSummaryConfiguration(
                true, "http://localhost:1234/v1", "local-test-model", variable));
            var symbol = new SymbolDefinition("csharp", "method", "Run", "Demo.Run",
                new SourceLocation("Demo.cs", 1, 1));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.SummarizeAsync(symbol, "public void Run() {}", configuration,
                    TestContext.Current.CancellationToken));

            Assert.Contains("empty or missing", error.Message);
            Assert.Null(handler.Url);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Method { get; private set; }
        public string? Payload { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Method = request.Method.Method;
            Authorization = request.Headers.Authorization?.ToString();
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
