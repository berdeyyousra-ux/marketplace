using FluentValidation;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Application;

public static class DependencyInjection
{
    private static readonly Type[] _handlerInterfaces = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    // The Api: every request handler and validator, plus everything the Worker uses. Handlers are scoped, like the
    // DbContext they use.
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

        services.AddScoped<Identity.SessionIssuer>();
        services.AddScoped<Catalog.ProductOwnership>();
        services.AddValidatorsFromAssembly(
            assembly,
            ServiceLifetime.Singleton,
            filter: scan => scan.ValidatorType.GetCustomAttributes(typeof(Common.ComponentValidatorAttribute), inherit: false).Length == 0,
            includeInternalTypes: true);
        return services.AddApplicationBackground();
    }

    // The Worker: outbox handlers and the services its jobs use. Request handlers need the caller and the token
    // issuer, which exist only in the Api, so they are left out.
    public static IServiceCollection AddApplicationBackground(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        foreach (var type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IOutboxHandler).IsAssignableFrom(type)))
        {
            services.AddScoped(typeof(IOutboxHandler), type);
        }

        services.AddScoped<Identity.OneTimeTokenService>();
        services.AddScoped<Ops.AuditTrail>();
        services.AddScoped<Payments.OrderPaymentService>();
        services.AddSingleton<Payments.PaymentReconciler>();
        return services;
    }
}
