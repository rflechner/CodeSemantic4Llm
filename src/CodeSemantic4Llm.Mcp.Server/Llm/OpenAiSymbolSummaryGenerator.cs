using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Logging;

namespace CodeSemantic4Llm.Mcp.Server.Llm;

/// <summary>Uses an OpenAI-compatible chat completions endpoint; does not log or persist source prompts.</summary>
public sealed class OpenAiSymbolSummaryGenerator(
    HttpClient client, ILogger<OpenAiSymbolSummaryGenerator> logger) : ISymbolSummaryGenerator
{
    public string CacheVersion => SummaryPrompt.CacheVersion;

    public async Task<string> SummarizeAsync(
        SymbolDefinition symbol, string declarationSource,
        SemanticIndexingConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(declarationSource);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.Llm;

        if (!settings.Enabled)
        {
            throw new InvalidOperationException("LLM summaries are disabled for this repository.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ChatCompletionsEndpoint);
        ApplyAuthentication(request, settings);
        request.Content = CreateRequestContent(symbol, declarationSource, configuration);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(content.GetString()))
        {
            logger.LogWarning(
                "The LLM did not return a text summary for {SymbolName} at {RelativePath}:{Line}. Continuing without a generated summary.",
                symbol.QualifiedName, symbol.Location.RelativePath, symbol.Location.Line);
            return string.Empty;
        }

        return string.Join("\n", content.GetString()!
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(10));
    }

    private static HttpContent CreateRequestContent(
        SymbolDefinition symbol, string declarationSource, SemanticIndexingConfiguration configuration)
    {
        var settings = configuration.Llm;
        var payload = new Dictionary<string, object>
        {
            ["model"] = settings.Model!,
            ["messages"] = new[]
            {
                new
                {
                    role = "system",
                    content = SummaryPrompt.SystemMessage(configuration.Language, settings.SystemPrompt)
                },
                new
                {
                    role = "user",
                    content = SummaryPrompt.UserMessage(symbol, declarationSource)
                }
            }
        };

        if (settings.ReasoningEffort is { } effort)
        {
            payload["reasoning_effort"] = effort;
        }

        if (settings.ReasoningEffort is not null || settings.MaxCompletionTokens is not null)
        {
            // Reasoning models may reject max_tokens, and their completion budget also
            // includes hidden reasoning. Leave room for the short visible summary.
            var defaultBudget = settings.ReasoningEffort is null or "none" ? 320 : 2048;
            payload["max_completion_tokens"] = settings.MaxCompletionTokens ?? defaultBudget;
        }
        else
        {
            // Preserve the request shape for existing local endpoints unless opted in.
            payload["max_tokens"] = 320;
        }

        return JsonContent.Create(payload);
    }

    private static void ApplyAuthentication(HttpRequestMessage request, LlmSummaryConfiguration settings)
    {
        var apiKey = settings.ApiKey;

        if (apiKey is null && settings.ApiKeyEnvironmentVariable is { } variable)
        {
            apiKey = Environment.GetEnvironmentVariable(variable);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("The configured LLM API key environment variable is empty or missing: " + variable);
            }
        }

        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
