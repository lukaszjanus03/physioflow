using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Behaviors;

namespace PhysioFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);
        // AutoMapper celowo pominięty - używamy .Select(...) w handlerach (patrz GetPatientListQuery).
        // Gdy zajdzie potrzeba, dodamy paczkę i re-enable: services.AddAutoMapper(assembly);

        return services;
    }
}
