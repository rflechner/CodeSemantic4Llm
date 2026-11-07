using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Logging;

namespace CodeSemantic4Llm.Mcp.Server.Llm;

/// <summary>
/// Dispatches completion calls to available OpenAI-compatible endpoints. Each endpoint has its own
/// authentication, concurrency limit, retry budget and run-scoped circuit breaker.
/// </summary>
public sealed class OpenAiSymbolSummaryGenerator(
    HttpClient client, ILogger<OpenAiSymbolSummaryGenerator> logger) : ISymbolSummaryGenerator
{
    private readonly ConditionalWeakTable<LlmSummaryConfiguration, EndpointPool> _pools = new();

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

        // Every configuration load gets its own pool. A watch reconfiguration never keeps the
        // previous endpoints or their circuit-breaker state.
        var pool = _pools.GetValue(settings, static item => new EndpointPool(item));
        Exception? lastFailure = null;

        for (var endpointAttempt = 0; endpointAttempt < settings.Endpoints.Count; endpointAttempt++)
        {
            EndpointPool.EndpointState endpoint;

            try
            {
                endpoint = await pool.RentAsync(cancellationToken);
            }
            catch (IOException exception)
            {
                throw new IOException("No available LLM endpoint could generate a summary.", lastFailure ?? exception);
            }

            try
            {
                for (var retry = 0; retry <= settings.RetryCount && !endpoint.IsDisabled; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        return await SendOnceAsync(symbol, declarationSource, configuration,
                            endpoint.Configuration, cancellationToken);
                    }
                    catch (Exception exception) when (IsRetryable(exception, cancellationToken))
                    {
                        lastFailure = exception;

                        if (retry == settings.RetryCount)
                        {
                            Disable(endpoint, pool);
                            break;
                        }

                        await DelayBeforeRetryAsync(settings, retry, cancellationToken);
                    }
                    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                    {
                        // Invalid credentials, missing variables and incompatible API requests are
                        // endpoint failures, not transient transport failures.
                        lastFailure = exception;
                        Disable(endpoint, pool);
                        break;
                    }
                }
            }
            finally
            {
                pool.Return(endpoint);
            }
        }

        // Preserve the legacy single-endpoint exception contract for existing clients.
        if (settings.Endpoints.Count == 1 && lastFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(lastFailure).Throw();
        }

        throw new IOException("All configured LLM endpoints failed to generate a summary.", lastFailure);
    }

    private void Disable(EndpointPool.EndpointState endpoint, EndpointPool pool)
    {
        if (pool.Disable(endpoint))
        {
            logger.LogWarning("LLM endpoint {Endpoint} is unavailable for the current configuration; trying another endpoint.",
                endpoint.Configuration.BaseUrl);
        }
    }

    private async Task<string> SendOnceAsync(
        SymbolDefinition symbol, string declarationSource,
        SemanticIndexingConfiguration configuration, LlmEndpointConfiguration endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.ChatCompletionsEndpoint);
        ApplyAuthentication(request, endpoint);
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

    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException http => http.StatusCode is null
                or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                || (int)http.StatusCode >= 500,
            JsonException or IOException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };

    private static async Task DelayBeforeRetryAsync(
        LlmSummaryConfiguration settings, int retry, CancellationToken cancellationToken)
    {
        if (settings.RetryDelayMilliseconds == 0)
        {
            return;
        }

        var delay = (int)Math.Min(30000L, settings.RetryDelayMilliseconds * (1L << Math.Min(retry, 12)));
        var jitter = Random.Shared.Next(0, Math.Max(1, delay / 4));
        await Task.Delay(delay + jitter, cancellationToken);
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
            var defaultBudget = settings.ReasoningEffort is null or "none" ? 320 : 2048;
            payload["max_completion_tokens"] = settings.MaxCompletionTokens ?? defaultBudget;
        }
        else
        {
            payload["max_tokens"] = 320;
        }

        return JsonContent.Create(payload);
    }

    private static void ApplyAuthentication(HttpRequestMessage request, LlmEndpointConfiguration endpoint)
    {
        var apiKey = endpoint.ApiKey;

        if (apiKey is null && endpoint.ApiKeyEnvironmentVariable is { } variable)
        {
            apiKey = Environment.GetEnvironmentVariable(variable);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "The configured LLM API key environment variable is empty or missing: " + variable);
            }
        }

        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>
    /// A channel of free HTTP slots distributes work by availability, not by a static round-robin.
    /// Disabled endpoints are removed; closing the channel wakes blocked readers when none remain.
    /// </summary>
    private sealed class EndpointPool
    {
        private readonly Channel<EndpointState> _available =
            Channel.CreateUnbounded<EndpointState>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        private int _activeEndpoints;

        public EndpointPool(LlmSummaryConfiguration settings)
        {
            _activeEndpoints = settings.Endpoints.Count;

            foreach (var configuration in settings.Endpoints)
            {
                var endpoint = new EndpointState(configuration);

                for (var slot = 0; slot < settings.MaxConcurrentRequestsPerEndpoint; slot++)
                {
                    _available.Writer.TryWrite(endpoint);
                }
            }
        }

        public async ValueTask<EndpointState> RentAsync(CancellationToken cancellationToken)
        {
            while (await _available.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_available.Reader.TryRead(out var endpoint) && !endpoint.IsDisabled)
                {
                    return endpoint;
                }
            }

            throw new IOException("No available LLM endpoints remain.");
        }

        public void Return(EndpointState endpoint)
        {
            if (!endpoint.IsDisabled)
            {
                _available.Writer.TryWrite(endpoint);
            }
        }

        public bool Disable(EndpointState endpoint)
        {
            if (Interlocked.Exchange(ref endpoint.Disabled, 1) != 0)
            {
                return false;
            }

            if (Interlocked.Decrement(ref _activeEndpoints) == 0)
            {
                _available.Writer.TryComplete();
            }

            return true;
        }

        public sealed class EndpointState(LlmEndpointConfiguration configuration)
        {
            public LlmEndpointConfiguration Configuration { get; } = configuration;
            public int Disabled;
            public bool IsDisabled => Volatile.Read(ref Disabled) != 0;
        }
    }
}
