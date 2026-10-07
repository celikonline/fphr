using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Reporting.Api;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Modules.Reporting.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

/// <summary>
/// Integration tests (real PostgreSQL via Testcontainers, see TestFixture) for the tenant/user izolasyonu of
/// downloading a report execution's file - the same behaviour <see cref="ReportingHandlerTests"/> already covers
/// with a mocked <see cref="IReportExecutionRepository"/>; here the global tenant query filter (see
/// ReportExecutionRepository.GetByIdAsync's XML doc) and the handler's ownership check run together against a
/// real database, the way they do in production.
/// </summary>
/// <remarks>
/// ReportingDbContext's "InitialReporting" EF migration landed 2026-09-16 (ADR-0011/MIGRATION-ISTEGI.md,
/// commit 3211e05) - <see cref="OnDatabaseReadyAsync"/>'s Database.MigrateAsync() now creates report_executions
/// for real, so these run against a real PostgreSQL container (Testcontainers, see TestFixture/[Collection]).
/// </remarks>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ReportExecutionIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new ReportingModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton<IDataScopeService, UnrestrictedDataScope>();
        services.AddSingleton(Substitute.For<IBackgroundJobService>());
        // AccessSettingsAuthorizationBehaviour ctor'u IAccessSettingsService ister; bu sorgulardaki Forbidden/NotFound
        // sahiplik/tenant-filtre kararlarıdır, erişim ayarından değil - o yüzden her şeyi açan fake güvenli.
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();

    [Fact]
    public void ReportGenerationRequested_HasExactlyOneHandlerInModuleComposition()
    {
        using var scope = ServiceProvider!.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<ReportGenerationRequested>>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task GetReportExecutionFileQuery_WhenRequestedByAnotherUser_FailsForbidden_ThroughRealTenantFilterAndOwnershipCheck()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        Guid executionId;
        using (var writeScope = ServiceProvider!.CreateScope())
        {
            using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
            var db = writeScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var execution = ReportExecution.Create(tenantId, "documents.missingDocuments", null, ownerId, DateTime.UtcNow).Value;
            execution.Start(DateTime.UtcNow);
            execution.Complete("report.csv", "text/csv", [1, 2, 3], DateTime.UtcNow);
            db.ReportExecutions.Add(execution);
            await db.SaveChangesAsync();
            executionId = execution.Id;
        }

        SetCurrentUser(tenantId, otherUserId, "other@acme.test", new HashSet<string> { "Employee" });

        var result = await ExecuteQueryAsync(new GetReportExecutionFileQuery(executionId));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.NotOwner);
    }

    [Fact]
    public async Task GetReportExecutionFileQuery_WhenRequestedByAnotherTenant_FailsNotFound_ThroughGlobalTenantFilter()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        Guid executionId;
        using (var writeScope = ServiceProvider!.CreateScope())
        {
            using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
            var db = writeScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var execution = ReportExecution.Create(tenantId, "documents.missingDocuments", null, ownerId, DateTime.UtcNow).Value;
            execution.Start(DateTime.UtcNow);
            execution.Complete("report.csv", "text/csv", [1, 2, 3], DateTime.UtcNow);
            db.ReportExecutions.Add(execution);
            await db.SaveChangesAsync();
            executionId = execution.Id;
        }

        // Same owner user id, but a different tenant - the global tenant query filter must hide it entirely
        // (NotFound), not fall through to the ownership check (which would otherwise wrongly succeed).
        SetCurrentUser(otherTenantId, ownerId, "owner@other-tenant.test", new HashSet<string> { "Employee" });

        var result = await ExecuteQueryAsync(new GetReportExecutionFileQuery(executionId));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ReportExecutionNotFound);
    }

    [Fact]
    public async Task GetReportExecutionFileQuery_WhenOwner_ReturnsFile()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        Guid executionId;
        using (var writeScope = ServiceProvider!.CreateScope())
        {
            using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
            var db = writeScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var execution = ReportExecution.Create(tenantId, "documents.missingDocuments", null, ownerId, DateTime.UtcNow).Value;
            execution.Start(DateTime.UtcNow);
            execution.Complete("report.csv", "text/csv", [1, 2, 3], DateTime.UtcNow);
            db.ReportExecutions.Add(execution);
            await db.SaveChangesAsync();
            executionId = execution.Id;
        }

        SetCurrentUser(tenantId, ownerId, "owner@acme.test", new HashSet<string> { "Employee" });

        var result = await ExecuteQueryAsync(new GetReportExecutionFileQuery(executionId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("report.csv");
        result.Value.Content.ShouldBe(new byte[] { 1, 2, 3 });
    }

    private sealed class GrantAllPermissions : IPermissionService
    {
        public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(DataScope.Company);
    }

    private sealed class AllAccessSettingsEnabled : IAccessSettingsService
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    /// <summary>
    /// GetReportExecutionFileQuery/GetReportExecutionQuery/ListMyReportExecutionsQuery don't implement IScopedRequest,
    /// so AuthorizationBehaviour never calls this - it only needs to exist so the behaviour's ctor resolves.
    /// </summary>
    private sealed class UnrestrictedDataScope : IDataScopeService
    {
        public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);

        public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);
    }
}
