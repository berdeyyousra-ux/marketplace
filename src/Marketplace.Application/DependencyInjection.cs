using FluentValidation;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Application;

public static class DependencyInjection
{
    private static readonly Type[] _handlerInterfaces = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    // Registers every handler and validator in this assembly. Handlers are scoped, like the DbContext they use.
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        foreach (var type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false }))
        {
            foreach (var handlerInterface in type.GetInterfaces()
                .Where(candidate => candidate.IsGenericType && _handlerInterfaces.Contains(candidate.GetGenericTypeDefinition())))
            {
                services.AddScoped(handlerInterface, type);
            }
        }

        foreach (var type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IOutboxHandler).IsAssignableFrom(type)))
        {
            services.AddScoped(typeof(IOutboxHandler), type);
        }

        services.AddScoped<Identity.OneTimeTokenService>();
        services.AddScoped<Identity.SessionIssuer>();
        services.AddScoped<Ops.AuditTrail>();
        services.AddScoped<Catalog.ProductOwnership>();
        services.AddScoped<Payments.OrderPaymentService>();
        services.AddValidatorsFromAssembly(
            assembly,
            ServiceLifetime.Singleton,
            filter: scan => scan.ValidatorType.GetCustomAttributes(typeof(Common.ComponentValidatorAttribute), inherit: false).Length == 0,
            includeInternalTypes: true);
        return services;
    }
}
