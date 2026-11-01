using System.Net;
using System.Text;
using System.Text.Json;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Mcp.Server.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

public sealed class OpenAiSymbolSummaryGeneratorTests
{
    [Fact]
    public async Task Sends_entire_declaration_and_XML_docs_to_openai_chat_completions()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var generator = new OpenAiSymbolSummaryGenerator(client, NullLogger<OpenAiSymbolSummaryGenerator>.Instance);
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
        Assert.False(request.RootElement.TryGetProperty("reasoning_effort", out _));
        Assert.False(request.RootElement.TryGetProperty("max_completion_tokens", out _));
        Assert.Equal(320, request.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Theory]
    [InlineData("Summarize in {lang}.\nReturn {\"language\": \"{lang}\"}. Keep {0}, {Lang} and {}.",
        "Summarize in fr-CH.\nReturn {\"language\": \"fr-CH\"}. Keep {0}, {Lang} and {}.")]
    [InlineData("Describe public void Run() { }.", "Describe public void Run() { }.")]
    public async Task Custom_system_prompt_replaces_only_named_language_tokens(string prompt, string expected)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var generator = new OpenAiSymbolSummaryGenerator(client, NullLogger<OpenAiSymbolSummaryGenerator>.Instance);
        var configuration = new SemanticIndexingConfiguration("FR-ch", new LlmSummaryConfiguration(
            true, "http://localhost:1234/v1", "local-test-model", systemPrompt: prompt));
        var symbol = new SymbolDefinition("csharp", "method", "Run", "Demo.Run",
            new SourceLocation("Demo.cs", 1, 1));

        await generator.SummarizeAsync(symbol, "public void Run() {}", configuration,
            TestContext.Current.CancellationToken);

        using var request = JsonDocument.Parse(handler.Payload!);
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal(expected, messages[0].GetProperty("content").GetString());
        Assert.Contains("public void Run() {}", messages[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"choices\":null}")]
    [InlineData("{\"choices\":{}}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{}]}")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":42}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\" \\r\\n\\t \"}}]}")]
    public async Task Missing_text_summary_returns_empty_and_logs_a_warning(string responseJson)
    {
        var handler = new RecordingHandler { ResponseJson = responseJson };
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        var generator = new OpenAiSymbolSummaryGenerator(client, logger);
        var configuration = new SemanticIndexingConfiguration(llm: new LlmSummaryConfiguration(
            true, "http://localhost:1234/v1", "local-test-model", apiKey: "API_KEY_MUST_NOT_BE_LOGGED"));
        var symbol = new SymbolDefinition("csharp", "method", "Run", "Demo.Run",
            new SourceLocation("Demo.cs", 3, 1));

        var result = await generator.SummarizeAsync(
            symbol, "public void Run() { /* SOURCE_MUST_NOT_BE_LOGGED */ }", configuration,
            TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
        Assert.Equal(1, handler.RequestCount);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("did not return a text summary", entry.Message);
        Assert.Contains("Demo.Run", entry.Message);
        Assert.Contains("Demo.cs:3", entry.Message);
        Assert.DoesNotContain("SOURCE_MUST_NOT_BE_LOGGED", entry.Message);
        Assert.DoesNotContain("API_KEY_MUST_NOT_BE_LOGGED", entry.Message);
    }

    [Theory]
    [InlineData("low", null, 2048)]
    [InlineData("none", null, 320)]
    [InlineData("minimal", 1024, 1024)]
    [InlineData("medium", 4096, 4096)]
    [InlineData(null, 1024, 1024)]
    public async Task Configured_reasoning_uses_the_modern_completion_budget(
        string? effort, int? tokenBudget, int expectedBudget)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var generator = new OpenAiSymbolSummaryGenerator(client, NullLogger<OpenAiSymbolSummaryGenerator>.Instance);
        var configuration = new SemanticIndexingConfiguration(llm: new LlmSummaryConfiguration(
            true, "http://localhost:1234/v1", "reasoning-model",
            reasoningEffort: effort, maxCompletionTokens: tokenBudget));
        var symbol = new SymbolDefinition("csharp", "method", "Run", "Demo.Run",
            new SourceLocation("Demo.cs", 1, 1));

        await generator.SummarizeAsync(symbol, "public void Run() {}", configuration,
            TestContext.Current.CancellationToken);

        using var request = JsonDocument.Parse(handler.Payload!);
        Assert.Equal(expectedBudget, request.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(request.RootElement.TryGetProperty("max_tokens", out _));
        if (effort is null)
        {
            Assert.False(request.RootElement.TryGetProperty("reasoning_effort", out _));
        }
        else
        {
            Assert.Equal(effort, request.RootElement.GetProperty("reasoning_effort").GetString());
        }
    }

    [Fact]
    public async Task Unsupported_reasoning_parameters_fail_without_retrying_with_different_settings()
    {
        var handler = new RecordingHandler { StatusCode = HttpStatusCode.BadRequest };
        using var client = new HttpClient(handler);
        var generator = new OpenAiSymbolSummaryGenerator(client, NullLogger<OpenAiSymbolSummaryGenerator>.Instance);
        var configuration = new SemanticIndexingConfiguration(llm: new LlmSummaryConfiguration(
            true, "http://localhost:1234/v1", "model-without-reasoning", reasoningEffort: "low"));
        var symbol = new SymbolDefinition("csharp", "method", "Run", "Demo.Run",
            new SourceLocation("Demo.cs", 1, 1));

        await Assert.ThrowsAsync<HttpRequestException>(() => generator.SummarizeAsync(
            symbol, "public void Run() {}", configuration, TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.RequestCount);
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
            var generator = new OpenAiSymbolSummaryGenerator(client, NullLogger<OpenAiSymbolSummaryGenerator>.Instance);
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
            var generator = new OpenAiSymbolSummaryGenerator(client, NullLogger<OpenAiSymbolSummaryGenerator>.Instance);
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

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger<OpenAiSymbolSummaryGenerator>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Method { get; private set; }
        public string? Payload { get; private set; }
        public string? Authorization { get; private set; }
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string? ResponseJson { get; init; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Url = request.RequestUri!.ToString();
            Method = request.Method.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            var json = ResponseJson ?? JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = "First line\nSecond line" } } }
            });
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
