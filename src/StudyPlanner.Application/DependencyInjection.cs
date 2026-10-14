using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using StudyPlanner.Application.Behaviors;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Domain.Services;
using StudyPlanner.Application.Authorization;

namespace StudyPlanner.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddSingleton<IStudySessionPlanner, StudySessionPlanner>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICategoryPermission, CategoryPermission>();
        services.TryAddSingleton<ICurrentUser, AnonymousCurrentUser>();
        return services;
    }
}
