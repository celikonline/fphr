using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Recruitment.Api;

/// <summary>Recruitment (ATS/İşe Alım) module composition root. R0: job postings, candidates, applications (see faz6-durum-takibi.md).</summary>
public sealed class RecruitmentModule : IModule
{
    public string Name => RecruitmentDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(RecruitmentModule).Assembly,
        typeof(RecruitmentPermissions).Assembly,
        typeof(RecruitmentDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => RecruitmentPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<RecruitmentDbContext>(configuration, RecruitmentDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see CLAUDE.md's AddModuleHandlers note - Contracts-assembly-only registration silently registers nothing).
        services.AddModuleHandlers(
            RecruitmentDbContext.SchemaName,
            typeof(RecruitmentErrors).Assembly,
            typeof(RecruitmentDbContext).Assembly,
            typeof(IJobPostingRepository).Assembly,
            typeof(RecruitmentPermissions).Assembly);

        services.AddScoped<IJobPostingRepository, JobPostingRepository>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
    }
}
