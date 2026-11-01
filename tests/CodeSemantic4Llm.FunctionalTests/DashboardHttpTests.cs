using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Mcp.Server.Commands;
using CodeSemantic4Llm.Persistence.Sqlite;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Runs the real dashboard executable to cover HTTP startup, embedded UI and endpoint boundaries.</summary>
public sealed class DashboardHttpTests
{
    [Fact]
    public async Task DashboardHostsAnOfflineReadOnlyUiOnTheSelectedLoopbackPort()
    {
        var port = FindFreeLoopbackPort();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "dashboard-http-" + Guid.NewGuid().ToString("N"));
        var paths = new IndexStoragePaths(Path.Combine(temporaryRoot, "cache"));
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath()
        };

        startInfo.ArgumentList.Add(typeof(StartMcpServerCommand).Assembly.Location);
        startInfo.ArgumentList.Add("dashboard");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        startInfo.Environment[IndexStoragePaths.EnvironmentVariable] = paths.Root;

        using var process = Process.Start(startInfo)!;
        // Drain host logs while requests run so redirected pipe buffers cannot block HTTP responses.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(2)
        };

        try
        {
            var ready = false;

            for (var attempt = 0; attempt < 80 && !process.HasExited; attempt++)
            {
                timeout.Token.ThrowIfCancellationRequested();

                try
                {
                    using var response = await client.GetAsync("/", timeout.Token);
                    ready = response.StatusCode == HttpStatusCode.OK;

                    if (ready)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // Server may still be starting.
                }
                catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
                {
                    // A transient connection timeout may occur during startup.
                }

                await Task.Delay(100, timeout.Token);
            }

            Assert.True(ready, "The dashboard did not start on the requested loopback port.");

            var html = await client.GetStringAsync("/", timeout.Token);
            Assert.Contains("INDEX EXPLORER", html);
            Assert.Contains("Declaration breakdown", html);
            Assert.Contains("Intent search", html);

            using var projects = await client.GetAsync("/api/projects", timeout.Token);
            Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
            Assert.Equal("[]", await projects.Content.ReadAsStringAsync(timeout.Token));

            await AssertRenderedSummariesAsync(client, paths, temporaryRoot, timeout.Token);

            using var missingScope = await client.GetAsync("/api/symbols", timeout.Token);
            Assert.Equal(HttpStatusCode.BadRequest, missingScope.StatusCode);

            using var hostileHost = new HttpRequestMessage(HttpMethod.Get, "/");
            hostileHost.Headers.Host = "untrusted.example";
            using var denied = await client.SendAsync(hostileHost, timeout.Token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutput, standardError);
            TemporaryDirectory.TryRemove(temporaryRoot);
        }
    }

    private static async Task AssertRenderedSummariesAsync(
        HttpClient client, IndexStoragePaths paths, string temporaryRoot, CancellationToken token)
    {
        const string markdown = "**Indexes** `Worker`.\n\n- Reads metadata\n- Publishes a snapshot\n\n<script>alert(1)</script>";
        var root = Path.Combine(temporaryRoot, "repository");
        Directory.CreateDirectory(root);
        var scope = new RepositoryScope(root, "main");
        using (var update = new SqliteIndexStore(paths).BeginUpdate(new RepositoryState(scope, "test-head"), "config", "en", token))
        {
            update.AddFile(new IndexedSourceFile("Worker.cs", "hash", "dotnet-csharp", "test"),
            [
                new SymbolDefinition("csharp", "class", "Worker", "Demo.Worker", new SourceLocation("Worker.cs", 1, 1),
                    Summary: markdown),
                new SymbolDefinition("csharp", "method", "Run", "Demo.Worker.Run", new SourceLocation("Worker.cs", 3, 5))
            ], token);
            update.Publish(token);
        }

        using var projects = JsonDocument.Parse(await client.GetStringAsync("/api/projects", token));
        var projectId = Assert.Single(projects.RootElement.EnumerateArray()).GetProperty("id").GetString();
        using var response = JsonDocument.Parse(await client.GetStringAsync($"/api/symbols?project={projectId}&branch=main", token));
        var page = response.RootElement;
        Assert.Equal(2, page.GetProperty("total").GetInt64());
        var symbols = page.GetProperty("items").EnumerateArray().ToArray();
        var worker = Assert.Single(symbols, symbol => symbol.GetProperty("name").GetString() == "Worker");
        Assert.Equal(markdown, worker.GetProperty("summary").GetString());
        var rendered = worker.GetProperty("summaryHtml").GetString();
        Assert.Contains("<strong>Indexes</strong>", rendered);
        Assert.Contains("<code>Worker</code>", rendered);
        Assert.Contains("<li>Reads metadata</li>", rendered);
        Assert.DoesNotContain("<script", rendered);
        var run = Assert.Single(symbols, symbol => symbol.GetProperty("name").GetString() == "Run");
        Assert.Equal(JsonValueKind.Null, run.GetProperty("summaryHtml").ValueKind);
        Assert.Equal(markdown, Assert.Single(new SqliteIndexStore(paths).FindSymbols(scope, "Worker", 10, token).Symbols).Summary);

        await AssertIntentSearchAsync(client, projectId!, markdown, token);
        await AssertIntentSearchValidationAsync(client, projectId!, token);
    }

    private static async Task AssertIntentSearchAsync(
        HttpClient client, string projectId, string markdown, CancellationToken token)
    {
        var endpoint = $"/api/search?project={projectId}&branch=main";
        using var response = JsonDocument.Parse(await client.GetStringAsync(endpoint + "&q=publishes", token));
        var result = response.RootElement;
        var symbol = Assert.Single(result.GetProperty("symbols").EnumerateArray());
        Assert.Equal("Worker", symbol.GetProperty("name").GetString());
        Assert.Equal(markdown, symbol.GetProperty("summary").GetString());
        Assert.Contains("<strong>Indexes</strong>", symbol.GetProperty("summaryHtml").GetString());
        Assert.DoesNotContain("<script", symbol.GetProperty("summaryHtml").GetString());
        Assert.False(result.GetProperty("hasMore").GetBoolean());
        var snapshot = result.GetProperty("snapshot");
        Assert.Equal("main", snapshot.GetProperty("scope").GetProperty("branchName").GetString());
        Assert.Equal("test-head", snapshot.GetProperty("recordedRevision").GetString());

        using var bounded = JsonDocument.Parse(await client.GetStringAsync(endpoint + "&q=Worker&limit=1", token));
        Assert.Single(bounded.RootElement.GetProperty("symbols").EnumerateArray());
        Assert.True(bounded.RootElement.GetProperty("hasMore").GetBoolean());

        using var filtered = JsonDocument.Parse(await client.GetStringAsync(endpoint + "&q=Worker&kind=method", token));
        var method = Assert.Single(filtered.RootElement.GetProperty("symbols").EnumerateArray());
        Assert.Equal("Run", method.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, method.GetProperty("summaryHtml").ValueKind);

        using var empty = JsonDocument.Parse(await client.GetStringAsync(endpoint + "&q=astronomy", token));
        Assert.Empty(empty.RootElement.GetProperty("symbols").EnumerateArray());
        Assert.False(empty.RootElement.GetProperty("hasMore").GetBoolean());

        var longestQuery = Uri.EscapeDataString("snapshot" + new string(' ', 504));
        using var longest = await client.GetAsync(endpoint + "&q=" + longestQuery, token);
        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
    }

    private static async Task AssertIntentSearchValidationAsync(HttpClient client, string projectId, CancellationToken token)
    {
        var endpoint = $"/api/search?project={projectId}&branch=main";
        string[] invalidQueries =
        [
            "", "&q=where%20is%20the", "&q=" + new string('a', 513),
            "&q=snapshot&limit=0", "&q=snapshot&limit=201", "&q=snapshot&limit=invalid",
            "&q=snapshot&kind=" + new string('a', 65)
        ];

        foreach (var query in invalidQueries)
        {
            using var response = await client.GetAsync(endpoint + query, token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var noScope = await client.GetAsync("/api/search?q=snapshot", token);
        Assert.Equal(HttpStatusCode.BadRequest, noScope.StatusCode);
        using var noBranch = await client.GetAsync($"/api/search?project={projectId}&branch=missing&q=snapshot", token);
        Assert.Equal(HttpStatusCode.NotFound, noBranch.StatusCode);
        using var noProject = await client.GetAsync($"/api/search?project={new string('0', 64)}&branch=main&q=snapshot", token);
        Assert.Equal(HttpStatusCode.NotFound, noProject.StatusCode);
    }

    private static int FindFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
