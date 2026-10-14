using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Infrastructure.Data;
using StudyPlanner.Infrastructure.Repositories;
using StudyPlanner.Application.Authentication;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Infrastructure.Authentication;
using StudyPlanner.Infrastructure.Administration;

namespace StudyPlanner.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PasswordHasherOptions>(options =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = 220_000;
        });
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IPasswordHashService, PasswordHashService>();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.GetConnectionString("DefaultConnection")));
        services.AddScoped<IDatabaseSchemaCheck, DatabaseSchemaCheck>();
        services.AddScoped<IReleaseAccountCheck, DemoAccountRelease>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IStudyItemRepository, StudyItemRepository>();
        services.AddScoped<IStudySessionRepository, StudySessionRepository>();
        services.AddScoped<IProgressRecordRepository, ProgressRecordRepository>();
        return services;
    }
}
