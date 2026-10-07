using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Application.Enrollments;
using Sense.Modules.Training.Application.Programs;
using Sense.Modules.Training.Domain;
using Sense.Modules.Training.Infrastructure;
using Sense.Modules.Training.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Sense.Tests.Shared.Fixtures;
using Xunit;

namespace Sense.Modules.Training.Tests;

/// <summary>
/// Lifecycle commands (cancel/complete enrollment, activate/close program) against real PostgreSQL with the REAL
/// AuditTenantInterceptor and AuditLogInterceptor wired exactly like TrainingModule.AddModule does. The unit tests
/// mock the repositories, so they cannot see the interceptor refusing a "TenantId was modified" save (senseik-066).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TrainingLifecycleIntegrationTests : TestFixture
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<TrainingDbContext>(
            configuration, TrainingDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddTrainingCoreServices();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<TrainingDbContext>().Database.MigrateAsync();

    /// <summary>Runs <paramref name="action"/> in a fresh DI scope (own DbContext, like one HTTP request) as <paramref name="tenantId"/>.</summary>
    private async Task<T> InScopeAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>The command pipeline's UnitOfWorkBehaviour: run the handler, SaveChanges only when it succeeded.</summary>
    private static async Task<Sense.Shared.Kernel.Results.Result> SendAsync(
        IServiceProvider sp, Func<Task<Sense.Shared.Kernel.Results.Result>> handle)
    {
        var result = await handle();
        if (result.IsSuccess)
        {
            await sp.GetRequiredService<TrainingDbContext>().SaveChangesAsync();
        }

        return result;
    }

    private async Task<Guid> SeedProgramAsync(Guid tenantId, bool activate = false) =>
        await InScopeAsync(tenantId, async sp =>
        {
            var program = TrainingProgram.Create(tenantId, "İSG Eğitimi", null, TrainingCategory.Compliance, DateTime.UtcNow, null, null).Value;
            if (activate)
            {
                program.Activate();
            }

            await sp.GetRequiredService<ITrainingProgramRepository>().AddAsync(program);
            await sp.GetRequiredService<TrainingDbContext>().SaveChangesAsync();
            return program.Id;
        });

    private async Task<Guid> SeedEnrollmentAsync(Guid tenantId, Guid programId) =>
        await InScopeAsync(tenantId, async sp =>
        {
            var enrollment = Enrollment.Create(programId, tenantId, Guid.NewGuid()).Value;
            await sp.GetRequiredService<IEnrollmentRepository>().AddAsync(enrollment);
            await sp.GetRequiredService<TrainingDbContext>().SaveChangesAsync();
            return enrollment.Id;
        });

    [Fact]
    public async Task CancelEnrollment_PersistsCancelledStatusReasonAndDate_WithoutTenantMismatch()
    {
        var programId = await SeedProgramAsync(TenantA, activate: true);
        var enrollmentId = await SeedEnrollmentAsync(TenantA, programId);

        var result = await InScopeAsync(TenantA, sp => SendAsync(sp, () =>
            new CancelEnrollmentHandler(sp.GetRequiredService<IEnrollmentRepository>(), sp.GetRequiredService<ITenantContext>())
                .Handle(new CancelEnrollmentCommand(enrollmentId, "Katılamıyor"), CancellationToken.None)));

        Assert.True(result.IsSuccess);
        var stored = await InScopeAsync(TenantA, sp => sp.GetRequiredService<IEnrollmentRepository>().GetByIdAsync(TenantA, enrollmentId));
        Assert.NotNull(stored);
        Assert.Equal(EnrollmentStatus.Cancelled, stored.Status);
        Assert.Equal("Katılamıyor", stored.CancellationReason);
        Assert.NotNull(stored.CancelledAt);
        Assert.Equal(TenantA, stored.TenantId);
    }

    [Fact]
    public async Task CompleteEnrollment_PersistsCompletedStatusAndCertificate_WithoutTenantMismatch()
    {
        var programId = await SeedProgramAsync(TenantA, activate: true);
        var enrollmentId = await SeedEnrollmentAsync(TenantA, programId);

        var result = await InScopeAsync(TenantA, sp => SendAsync(sp, () =>
            new CompleteEnrollmentHandler(sp.GetRequiredService<IEnrollmentRepository>(), sp.GetRequiredService<ITenantContext>())
                .Handle(new CompleteEnrollmentCommand(enrollmentId, "CERT-1"), CancellationToken.None)));

        Assert.True(result.IsSuccess);
        var stored = await InScopeAsync(TenantA, sp => sp.GetRequiredService<IEnrollmentRepository>().GetByIdAsync(TenantA, enrollmentId));
        Assert.Equal(EnrollmentStatus.Completed, stored!.Status);
        Assert.Equal("CERT-1", stored.CertificateNumber);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task ActivateTrainingProgram_MovesDraftToActive_WithoutTenantMismatch()
    {
        var programId = await SeedProgramAsync(TenantA);

        var result = await InScopeAsync(TenantA, sp => SendAsync(sp, () =>
            new ActivateTrainingProgramHandler(sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ITrainingProgramRepository>())
                .Handle(new ActivateTrainingProgramCommand(programId), CancellationToken.None)));

        Assert.True(result.IsSuccess);
        var stored = await InScopeAsync(TenantA, sp => sp.GetRequiredService<ITrainingProgramRepository>().GetByIdAsync(TenantA, programId));
        Assert.Equal(TrainingStatus.Active, stored!.Status);
        Assert.NotNull(stored.ModifiedDate);
    }

    [Fact]
    public async Task CloseTrainingProgram_MovesActiveToClosed_WithoutTenantMismatch()
    {
        var programId = await SeedProgramAsync(TenantA, activate: true);

        var result = await InScopeAsync(TenantA, sp => SendAsync(sp, () =>
            new CloseTrainingProgramHandler(sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ITrainingProgramRepository>())
                .Handle(new CloseTrainingProgramCommand(programId), CancellationToken.None)));

        Assert.True(result.IsSuccess);
        var stored = await InScopeAsync(TenantA, sp => sp.GetRequiredService<ITrainingProgramRepository>().GetByIdAsync(TenantA, programId));
        Assert.Equal(TrainingStatus.Closed, stored!.Status);
    }

    [Fact]
    public async Task TenantB_CannotCancelCompleteActivateOrClose_TenantAsRecords()
    {
        var programId = await SeedProgramAsync(TenantA);
        var enrollmentId = await SeedEnrollmentAsync(TenantA, programId);

        var results = await InScopeAsync(TenantB, async sp =>
        {
            var enrollments = sp.GetRequiredService<IEnrollmentRepository>();
            var programs = sp.GetRequiredService<ITrainingProgramRepository>();
            var tenant = sp.GetRequiredService<ITenantContext>();
            return new[]
            {
                await new CancelEnrollmentHandler(enrollments, tenant).Handle(new CancelEnrollmentCommand(enrollmentId), CancellationToken.None),
                await new CompleteEnrollmentHandler(enrollments, tenant).Handle(new CompleteEnrollmentCommand(enrollmentId), CancellationToken.None),
                await new ActivateTrainingProgramHandler(tenant, programs).Handle(new ActivateTrainingProgramCommand(programId), CancellationToken.None),
                await new CloseTrainingProgramHandler(tenant, programs).Handle(new CloseTrainingProgramCommand(programId), CancellationToken.None),
            };
        });

        Assert.All(results, r => Assert.True(r.IsFailure));
        var enrollment = await InScopeAsync(TenantA, sp => sp.GetRequiredService<IEnrollmentRepository>().GetByIdAsync(TenantA, enrollmentId));
        var program = await InScopeAsync(TenantA, sp => sp.GetRequiredService<ITrainingProgramRepository>().GetByIdAsync(TenantA, programId));
        Assert.Equal(EnrollmentStatus.Enrolled, enrollment!.Status);
        Assert.Equal(TrainingStatus.Draft, program!.Status);
    }

    [Fact]
    public async Task UpdateAsync_OnADetachedEntityFromTheSameTenant_StillPersists()
    {
        var programId = await SeedProgramAsync(TenantA);
        var enrollmentId = await SeedEnrollmentAsync(TenantA, programId);

        // Loaded in one scope, mutated and passed to a repository of ANOTHER scope (detached for that context).
        var detached = await InScopeAsync(TenantA, sp => sp.GetRequiredService<IEnrollmentRepository>().GetByIdAsync(TenantA, enrollmentId));
        Assert.True(detached!.Cancel("başka kapsam").IsSuccess);

        await InScopeAsync(TenantA, async sp =>
        {
            await sp.GetRequiredService<IEnrollmentRepository>().UpdateAsync(detached);
            await sp.GetRequiredService<TrainingDbContext>().SaveChangesAsync();
            return 0;
        });

        var stored = await InScopeAsync(TenantA, sp => sp.GetRequiredService<IEnrollmentRepository>().GetByIdAsync(TenantA, enrollmentId));
        Assert.Equal(EnrollmentStatus.Cancelled, stored!.Status);
        Assert.Equal("başka kapsam", stored.CancellationReason);
    }
}
