using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Rejects any arguments that Spectre did not bind, before a command can access the repository.</summary>
internal sealed class UnmappedArgumentsInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        var option = context.Remaining.Parsed.FirstOrDefault();

        if (option is not null)
        {
            throw new ArgumentException($"Unknown option '{option.Key}'.");
        }

        if (context.Remaining.Raw.Count > 0)
        {
            throw new ArgumentException($"Unexpected argument '{context.Remaining.Raw[0]}'.");
        }
    }
}
