using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Bridges Spectre command activation to the application's dependency injection container.</summary>
internal sealed class ServiceCollectionTypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() => new ServiceProviderTypeResolver(services.BuildServiceProvider());

    public void Register(Type service, Type implementation) => services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => services.AddSingleton(service, _ => factory());

    private sealed class ServiceProviderTypeResolver(ServiceProvider provider) : ITypeResolver, IDisposable
    {
        public object? Resolve(Type? type) => type is null ? null : provider.GetService(type);

        public void Dispose() => provider.Dispose();
    }
}
