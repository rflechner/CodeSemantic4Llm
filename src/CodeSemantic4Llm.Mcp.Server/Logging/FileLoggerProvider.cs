using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CodeSemantic4Llm.Mcp.Server.Logging;

/// <summary>
/// Writes flushed JSON lines to an operator-selected file, retaining a bounded set of archives.
/// Each path has one writer; other processes may read it while the server runs.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public const long DefaultMaximumBytes = 10 * 1024 * 1024;
    public const int DefaultArchiveCount = 3;

    private readonly object _gate = new();
    private readonly long _maximumBytes;
    private readonly int _archiveCount;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    private StreamWriter? _writer;
    private long _length;
    private bool _disabled;

    /// <summary>
    /// Resolves paths from the process working directory and creates missing parent directories.
    /// Opening failures abort startup, rather than claiming file logging is active.
    /// </summary>
    public FileLoggerProvider(
        string path, long maximumBytes = DefaultMaximumBytes, int archiveCount = DefaultArchiveCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(archiveCount);
        FilePath = Path.GetFullPath(path);
        _maximumBytes = maximumBytes;
        _archiveCount = archiveCount;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            OpenWriter();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot open log file '{FilePath}': {exception.Message}", exception);
        }
    }

    public string FilePath { get; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
        lock (_gate)
        {
            _disabled = true;
            CloseWriter();
        }
    }

    internal void WriteToolPayload(LogLevel level, string message, ToolCallPayload payload) =>
        Write("CodeSemantic4Llm.Mcp.Server.ToolCalls", level, default, message, null, payload);

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception,
        ToolCallPayload? payload = null)
    {
        var context = new CallContext();
        _scopes.ForEachScope(static (scope, call) => call.ReadScope(scope), context);
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            processId = Environment.ProcessId,
            level = level.ToString(),
            category,
            eventId = eventId.Id,
            requestId = payload?.RequestId ?? context.RequestId,
            toolName = payload is null ? context.ToolName : payload.ToolName,
            message,
            exception = exception?.ToString(),
            parameters = payload?.Parameters,
            response = payload?.Response
        });

        lock (_gate)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetByteCount(line) + 1;

                if (_length > 0 && _length + bytes > _maximumBytes)
                {
                    Rotate();
                }

                _writer!.WriteLine(line);
                _length += bytes;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A failing log destination must not turn a successful code operation into a failure.
                _disabled = true;
                CloseWriter();
                ReportFailure(failure);
            }
        }
    }

    private void OpenWriter()
    {
        var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _length = stream.Length;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    }

    private void Rotate()
    {
        _writer!.Dispose();
        _writer = null;

        for (var archive = _archiveCount; archive > 1; archive--)
        {
            var previous = $"{FilePath}.{archive - 1}";

            if (File.Exists(previous))
            {
                File.Move(previous, $"{FilePath}.{archive}", overwrite: true);
            }
        }

        File.Move(FilePath, $"{FilePath}.1", overwrite: true);
        OpenWriter();
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // A prior write may have failed; disposal must not mask the original operation.
        }

        _writer = null;
    }

    private void ReportFailure(Exception failure)
    {
        try
        {
            Console.Error.WriteLine($"File logging disabled for '{FilePath}': {failure.Message}");
        }
        catch (IOException)
        {
            // The MCP client may also have disconnected its stderr reader.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);

        // Transport trace/debug logging can contain raw request and response payloads.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
            }
        }
    }

    private sealed class CallContext
    {
        public string? RequestId { get; private set; }
        public string? ToolName { get; private set; }

        public void ReadScope(object? scope)
        {
            if (scope is not IEnumerable<KeyValuePair<string, object?>> values)
            {
                return;
            }

            // Payloads arrive explicitly through WriteToolPayload, never through arbitrary scopes.
            foreach (var pair in values)
            {
                if (pair.Key == "RequestId")
                {
                    RequestId = pair.Value?.ToString();
                }

                if (pair.Key == "ToolName")
                {
                    ToolName = pair.Value?.ToString();
                }
            }
        }
    }
}
