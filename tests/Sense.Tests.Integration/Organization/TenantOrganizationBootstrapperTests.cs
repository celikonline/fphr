using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Organization.Application.Companies;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Integration.Identity;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Organization;

/// <summary>
/// Yeni (süper admin ile açılan) kiracının Organization varsayılanları: sistem birim tipleri, kök Company birimi + Company satırı
/// ve kiracının kendi TR resmi tatil takvimi. Bootstrap idempotent ve kiracıya özeldir; olay işleyicisi ve sihirbazın komutu
/// aynı uygulamayı kullanır.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TenantOrganizationBootstrapperTests : IdentityStackTestBase
{
    [Fact]
    public async Task Ensure_CreatesUnitTypesRootCompanyAndSystemHolidayCalendar()
    {
        var tenantId = await SeedTenantAsync("acme-org");

        await Get<ITenantOrganizationBootstrapper>().EnsureAsync(tenantId, "Acme A.Ş.", Ct);

        var snapshot = await ReadAsync(tenantId);
        snapshot.UnitTypeCodes.ShouldContain(OrgUnitTypeCodes.Company);
        snapshot.UnitTypeCodes.ShouldContain(OrgUnitTypeCodes.Department);
        snapshot.CompanyCount.ShouldBe(1);
        snapshot.RootUnitNames.ShouldBe(["Acme A.Ş."]);
        snapshot.SystemCalendarCount.ShouldBe(1);
        snapshot.SystemCalendarEntryCount.ShouldBeGreaterThan(0);
        snapshot.CompanyCalendarIsSystemCalendar.ShouldBeTrue();
    }

    [Fact]
    public async Task Ensure_IsIdempotent()
    {
        var tenantId = await SeedTenantAsync("acme-org");
        var bootstrapper = Get<ITenantOrganizationBootstrapper>();

        await bootstrapper.EnsureAsync(tenantId, "Acme A.Ş.", Ct);
        var first = await ReadAsync(tenantId);
        await bootstrapper.EnsureAsync(tenantId, "Baska Ad", Ct);
        var second = await ReadAsync(tenantId);

        second.ShouldBe(first);
        second.RootUnitNames.ShouldBe(["Acme A.Ş."]);
    }

    [Fact]
    public async Task Ensure_IsPerTenant()
    {
        var acme = await SeedTenantAsync("acme-org");
        var globex = await SeedTenantAsync("globex-org");
        var bootstrapper = Get<ITenantOrganizationBootstrapper>();

        await bootstrapper.EnsureAsync(acme, "Acme A.Ş.", Ct);
        await bootstrapper.EnsureAsync(globex, "Globex Ltd.", Ct);

        (await ReadAsync(acme)).RootUnitNames.ShouldBe(["Acme A.Ş."]);
        (await ReadAsync(globex)).RootUnitNames.ShouldBe(["Globex Ltd."]);
        (await ReadAsync(globex)).CompanyCount.ShouldBe(1);
    }

    [Fact]
    public async Task TenantProvisionedEventHandler_PreparesTheOrganization()
    {
        var tenantId = await SeedTenantAsync("acme-org");

        await new TenantProvisionedHandler(Get<ITenantOrganizationBootstrapper>()).Handle(new TenantProvisioned(tenantId, "Acme A.Ş."), Ct);

        (await ReadAsync(tenantId)).CompanyCount.ShouldBe(1);
    }

    [Fact]
    public async Task OwnerCanEnsureFromTheWizard_AndThenReadTheCompanyProfile_ButAnEmployeeCannot()
    {
        var tenantId = await SeedTenantAsync("acme-org");
        var owner = await SeedUserAsync(tenantId, "boss@acme.test", SystemRoleCodes.Owner);
        var employee = await SeedUserAsync(tenantId, "worker@acme.test", SystemRoleCodes.Employee);

        ActAs(tenantId, employee);
        ShouldFailWith(await ExecuteCommandAsync(new EnsureTenantOrganizationCommand("Acme A.Ş.")), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(tenantId, owner);
        (await ExecuteCommandAsync(new EnsureTenantOrganizationCommand("Acme A.Ş."))).IsSuccess.ShouldBeTrue();

        var profile = await ExecuteQueryAsync(new GetCompanyProfileQuery());
        profile.IsSuccess.ShouldBeTrue(profile.IsFailure ? profile.Error.Code : null);
        profile.Value.Name.ShouldBe("Acme A.Ş.");
    }

    private async Task<Snapshot> ReadAsync(Guid tenantId)
    {
        var db = Get<OrganizationDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var types = await db.OrgUnitTypes.AsNoTracking().Select(t => t.Code).ToListAsync(Ct);
            var companies = await db.Companies.AsNoTracking().ToListAsync(Ct);
            var roots = await db.OrgUnits.AsNoTracking().Where(u => u.ParentId == null).Select(u => u.Name).ToListAsync(Ct);
            var systemCalendars = await db.HolidayCalendars.AsNoTracking().Where(c => c.IsSystemDefined).ToListAsync(Ct);
            var entries = await db.HolidayCalendarEntries.AsNoTracking().CountAsync(Ct);

            return new Snapshot(
                types.Order().ToList(),
                companies.Count,
                roots,
                systemCalendars.Count,
                entries,
                companies.Count == 1 && systemCalendars.Count == 1 && companies[0].HolidayCalendarId == systemCalendars[0].Id);
        }
    }

    private sealed record Snapshot(
        IReadOnlyList<string> UnitTypeCodes,
        int CompanyCount,
        IReadOnlyList<string> RootUnitNames,
        int SystemCalendarCount,
        int SystemCalendarEntryCount,
        bool CompanyCalendarIsSystemCalendar)
    {
        public bool Equals(Snapshot? other) =>
            other is not null
            && UnitTypeCodes.SequenceEqual(other.UnitTypeCodes)
            && CompanyCount == other.CompanyCount
            && RootUnitNames.SequenceEqual(other.RootUnitNames)
            && SystemCalendarCount == other.SystemCalendarCount
            && SystemCalendarEntryCount == other.SystemCalendarEntryCount
            && CompanyCalendarIsSystemCalendar == other.CompanyCalendarIsSystemCalendar;

        public override int GetHashCode() => HashCode.Combine(UnitTypeCodes.Count, CompanyCount, SystemCalendarCount, SystemCalendarEntryCount);
    }
}
