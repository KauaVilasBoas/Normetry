using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Normetry.BuildingBlocks.Cqrs;

public static class DispatcherServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher, the built-in behaviors and every handler and validator found in
    /// <paramref name="assemblies"/>. Scans once, at startup. Behaviors registered after this call
    /// run inside logging and validation, in the order they are registered.
    /// </summary>
    public static IServiceCollection AddDispatcher(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        var implementations = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericType: false });

        foreach (var implementation in implementations)
        {
            foreach (var contract in implementation.GetInterfaces())
            {
                if (IsHandlerOrValidator(contract))
                {
                    services.AddScoped(contract, implementation);
                }
            }
        }

        return services;
    }

    private static bool IsHandlerOrValidator(Type contract) =>
        contract.IsGenericType
        && contract.GetGenericTypeDefinition() is var definition
        && (definition == typeof(IRequestHandler<,>) || definition == typeof(IValidator<>));
}
