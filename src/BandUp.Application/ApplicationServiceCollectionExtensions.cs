using System.Reflection;
using BandUp.Application.Abstractions;
using BandUp.Application.Behaviors;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Application.Features.Identity.ResetPassword;
using BandUp.Application.Features.Identity.RequestOtpLogin;
using BandUp.Application.Features.Identity.VerifyOtpLogin;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        Assembly assembly = typeof(ApplicationServiceCollectionExtensions).Assembly;

        RegisterHandlers(services, assembly);
        RegisterValidators(services, assembly);
        services.AddScoped<IPipelineBehavior<ForgotPasswordCommand, ForgotPasswordOutcome>, ForgotPasswordTimingBehavior>();
        services.AddScoped<IPipelineBehavior<ResetPasswordCommand, ResetPasswordOutcome>, ResetPasswordTimingBehavior>();
        services.AddScoped<IPipelineBehavior<RequestOtpLoginCommand, RequestOtpLoginOutcome>, RequestOtpLoginTimingBehavior>();
        services.AddScoped<IPipelineBehavior<VerifyOtpLoginCommand, VerifyOtpLoginOutcome>, VerifyOtpLoginTimingBehavior>();
        services.AddPipelineBehaviors();
        services.AddScoped<IDispatcher, Dispatcher>();

        return services;
    }

    /// <summary>
    /// Registers the pipeline behaviors. Order is significant: the first registration is the
    /// outermost behavior, so requests pass through logging, then validation, then the transaction.
    /// </summary>
    public static IServiceCollection AddPipelineBehaviors(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        Dictionary<Type, Type> registeredRequests = [];

        foreach (Type implementation in ConcreteTypes(assembly))
        {
            foreach (Type handlerInterface in implementation
                         .GetInterfaces()
                         .Where(IsHandlerInterface))
            {
                Type requestType = handlerInterface.GetGenericArguments()[0];

                if (!registeredRequests.TryAdd(requestType, implementation))
                {
                    throw new InvalidOperationException(
                        $"Two handlers handle '{requestType.FullName}': " +
                        $"'{registeredRequests[requestType].FullName}' and '{implementation.FullName}'. " +
                        "Exactly one handler per request type is allowed.");
                }

                services.AddScoped(handlerInterface, implementation);
            }
        }
    }

    private static void RegisterValidators(IServiceCollection services, Assembly assembly)
    {
        foreach (Type implementation in ConcreteTypes(assembly))
        {
            foreach (Type validatorInterface in implementation
                         .GetInterfaces()
                         .Where(IsClosedValidatorInterface))
            {
                services.AddScoped(validatorInterface, implementation);
            }
        }
    }

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType &&
        type.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ICommandHandler<,>) || definition == typeof(IQueryHandler<,>));

    private static bool IsClosedValidatorInterface(Type type) =>
        type.IsGenericType &&
        !type.IsGenericTypeDefinition &&
        type.GetGenericTypeDefinition() == typeof(IValidator<>);
}
