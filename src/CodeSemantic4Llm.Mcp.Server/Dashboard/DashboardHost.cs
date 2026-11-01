using System.Globalization;
using System.Reflection;
using CodeSemantic4Llm.Application.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace CodeSemantic4Llm.Mcp.Server.Dashboard;

/// <summary>
/// Runs the embedded, dependency-free UI and read-only JSON endpoints on IPv4 loopback.
/// This is intentionally separate from the MCP stdio transport and never exposes editing tools.
/// </summary>
internal static class DashboardHost
{
    private static readonly Lazy<string> Html = new(ReadHtml);

    public static async Task<int> RunAsync(
        IIndexDashboardReader reader, int port, IAnsiConsole console, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        await using var app = builder.Build();

        app.Use(async (context, next) =>
        {
            // Prevent DNS rebinding through browser requests to an arbitrary Host.
            if (context.Request.Host.Host is not ("localhost" or "127.0.0.1"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
                "connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
            await next(context);
        });

        app.MapGet("/", () => Results.Content(Html.Value, "text/html; charset=utf-8"));
        app.MapGet("/api/projects", (HttpContext context) =>
            Respond(() => reader.ListProjects(context.RequestAborted)));
        app.MapGet("/api/summary", (HttpContext context) =>
        {
            if (!TryScope(context.Request, out var project, out var branch))
            {
                return BadRequest("Select a project and branch.");
            }

            return Respond(() => reader.GetSummary(project, branch, context.RequestAborted));
        });

        app.MapGet("/api/files", (HttpContext context) =>
            QueryPage(context, (project, branch, search, offset, limit) =>
                reader.GetFiles(project, branch, search, offset, limit, context.RequestAborted)));

        app.MapGet("/api/symbols", (HttpContext context) =>
            QueryPage(context, (project, branch, search, offset, limit) =>
            {
                var kind = context.Request.Query["kind"].ToString();
                var file = context.Request.Query["file"].ToString();

                if (kind.Length > 64 || file.Length > 4096)
                {
                    throw new ArgumentException("Filter is too long.");
                }

                var page = reader.GetSymbols(project, branch, search, kind, file,
                    offset, limit, context.RequestAborted);
                return new DashboardPage<DashboardSymbolResponse>(
                    page.Items.Select(DashboardSymbolResponse.From).ToArray(), page.Total, page.Offset, page.Limit);
            }));

        app.MapGet("/api/search", (HttpContext context) => SearchCode(context, reader));

        app.MapGet("/api/references", (HttpContext context) =>
            QueryPage(context, (project, branch, search, offset, limit) =>
            {
                var query = context.Request.Query;
                var targetPath = query["targetPath"].ToString();
                int? line = null;
                int? column = null;

                if (targetPath.Length > 4096)
                {
                    throw new ArgumentException("Target path is too long.");
                }

                if (targetPath.Length != 0)
                {
                    if (!int.TryParse(query["line"], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLine)
                        || !int.TryParse(query["column"], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedColumn)
                        || parsedLine < 1 || parsedColumn < 1)
                    {
                        throw new ArgumentException("Target path requires positive line and column values.");
                    }

                    line = parsedLine;
                    column = parsedColumn;
                }
                else if (query.ContainsKey("line") || query.ContainsKey("column"))
                {
                    throw new ArgumentException("Target coordinates require a target path.");
                }

                return reader.GetReferences(project, branch, search,
                    targetPath.Length == 0 ? null : targetPath, line, column,
                    offset, limit, context.RequestAborted);
            }));

        await app.StartAsync(cancellationToken);
        console.WriteLine($"Dashboard: http://127.0.0.1:{port}/");
        console.WriteLine("Read-only index explorer. Press Ctrl+C to stop.");

        try
        {
            await app.WaitForShutdownAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An interactive Ctrl+C should exit normally without logging a failure.
        }

        return 0;
    }

    private static IResult SearchCode(HttpContext context, IIndexDashboardReader reader)
    {
        if (!TryScope(context.Request, out var project, out var branch))
        {
            return BadRequest("Select a project and branch.");
        }

        var values = context.Request.Query;
        var query = values["q"].ToString();
        var kind = values["kind"].ToString();
        var limit = 10;

        if (kind.Length > 64)
        {
            return BadRequest("Symbol kind must contain at most 64 characters.");
        }

        if (values.ContainsKey("limit")
            && (!int.TryParse(values["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > 200))
        {
            return BadRequest("Limit must be between 1 and 200.");
        }

        return Respond(() =>
        {
            var result = reader.SearchCode(project, branch, query, kind, limit, context.RequestAborted);
            return new
            {
                result.Snapshot,
                Symbols = result.Symbols.Select(DashboardSymbolResponse.From).ToArray(),
                result.HasMore
            };
        });
    }

    private static IResult QueryPage(
        HttpContext context, Func<string, string, string?, int, int, object> query)
    {
        if (!TryScope(context.Request, out var project, out var branch))
        {
            return BadRequest("Select a project and branch.");
        }

        var values = context.Request.Query;
        var search = values["q"].ToString();

        if (search.Length > 200)
        {
            return BadRequest("Search terms must contain at most 200 characters.");
        }

        var offset = 0;
        var limit = 50;

        if ((values.ContainsKey("offset")
             && (!int.TryParse(values["offset"], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                 || offset < 0))
            || (values.ContainsKey("limit")
                && (!int.TryParse(values["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                    || limit is < 1 or > 200)))
        {
            return BadRequest("Offset must be non-negative and limit must be between 1 and 200.");
        }

        return Respond(() => query(project, branch, search, offset, limit));
    }

    private static bool TryScope(HttpRequest request, out string project, out string branch)
    {
        project = request.Query["project"].ToString();
        branch = request.Query["branch"].ToString();

        return project.Length == 64 && branch is { Length: > 0 and <= 255 };
    }

    private static IResult Respond<T>(Func<T> operation)
    {
        try
        {
            return Results.Ok(operation());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "The selected project or branch has no published index." });
        }
        catch (ArgumentException error)
        {
            return BadRequest(error.Message);
        }
        catch (InvalidOperationException)
        {
            return Results.Problem("The selected index cannot be read. It may need reindexing.",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    private static string ReadHtml()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CodeSemantic4Llm.Dashboard.Index")
            ?? throw new InvalidOperationException("Embedded dashboard page is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
