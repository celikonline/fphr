using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// S-18 ILeaveUsageReader edge cases (S-18 sözleşme notu yapısı: TotalEntitledDays = yıllık izin + devir,
/// UsedDays = yıllık onaylı, ByLeaveType = tüm türler). Testcontainers/Postgres ile migrate + DI çözüm.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveUsageReaderTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeavePermissions).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly,
            typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
    }

    /// <summary>
    /// S-18 kenar vakaları (a): önceki yıldan CarryOver satırı olan çalışan → TotalEntitledDays = o yılın Accrual + CarryOver.
    /// </summary>
    [Fact]
    public async Task GetAnnualUsageAsync_WithCarryOverFromPreviousYear_IncludesCarryOverInTotalEntitledDays()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            await db.SaveChangesAsync();

            var annualType = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
            var profile = EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1));
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();

            // Manually add Accrual for 2025 (10 days)
            var accrual2025 = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Accrual, 10m,
                new DateOnly(2025, 1, 15), "Manual 2025 accrual", null);
            db.LedgerEntries.Add(accrual2025);

            // Manually add Accrual for 2026 (15 days)
            var accrual2026 = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Accrual, 15m,
                new DateOnly(2026, 1, 15), "Manual 2026 accrual", null);
            db.LedgerEntries.Add(accrual2026);

            // Add CarryOver from 2025 to 2026 (5 days carried over)
            var carryOver2026 = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.CarryOver, 5m,
                new DateOnly(2026, 1, 1), "CarryOver from 2025", null);
            db.LedgerEntries.Add(carryOver2026);

            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<ILeaveUsageReader>();
        var summary = await reader.GetAnnualUsageAsync(tenantId, 2026, null, TestContext.Current.CancellationToken);

        // TotalEntitledDays should be Accrual(15) + CarryOver(5) = 20
        summary.TotalEntitledDays.ShouldBe(20m);
        // UsedDays should be 0 (no Usage/Reversal entries)
        summary.UsedDays.ShouldBe(0m);
    }

    /// <summary>
    /// S-18 kenar vakaları (b): onaylanıp iptal edilen (Reversal) yıllık izin → UsedDays netlenmiş (iptal edilen gün sayılmaz), ByLeaveType'ta sıfırlanan tür listede yok.
    /// </summary>
    [Fact]
    public async Task GetAnnualUsageAsync_WithReversedUsage_NetsUsageAndExcludesZeroTypesFromByLeaveType()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid annualTypeId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            await db.SaveChangesAsync();

            var annualType = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
            annualTypeId = annualType.Id;
            var profile = EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1));
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();

            // Add Accrual (20 days)
            var accrual = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Accrual, 20m,
                new DateOnly(2026, 1, 15), "Annual accrual", null);
            db.LedgerEntries.Add(accrual);

            // Add Usage (5 days used)
            var usage = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Usage, -5m,
                new DateOnly(2026, 3, 15), "Usage", null);
            db.LedgerEntries.Add(usage);

            // Add Reversal (cancel that 5-day usage)
            var reversal = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Reversal, 5m,
                new DateOnly(2026, 3, 20), "Reversal", null);
            db.LedgerEntries.Add(reversal);

            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<ILeaveUsageReader>();
        var summary = await reader.GetAnnualUsageAsync(tenantId, 2026, null, TestContext.Current.CancellationToken);

        // TotalEntitledDays = 20 (accrual only)
        summary.TotalEntitledDays.ShouldBe(20m);
        // UsedDays should be 0 (usage -5 + reversal +5 = 0)
        summary.UsedDays.ShouldBe(0m);
        // ByLeaveType should exclude the annual type since its used days = 0
        summary.ByLeaveType.ShouldNotContain(u => u.LeaveTypeId == annualTypeId);
    }

    /// <summary>
    /// S-18 kenar vakaları (c): Aralık→Ocak yayılan talep varsa mevcut davranışı yalnız raporla.
    /// Bu test, request'in başlangıç yılına yazıldığını doğrular (ilk sürümde kabul gören davranış).
    /// </summary>
    [Fact]
    public async Task GetAnnualUsageAsync_WithSpanningRequestDecemberToJanuary_CountsAllDaysInStartYear()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            await db.SaveChangesAsync();

            var annualType = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
            var profile = EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1));
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();

            // Add Accrual for 2025 (20 days)
            var accrual2025 = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Accrual, 20m,
                new DateOnly(2025, 1, 15), "Accrual 2025", null);
            db.LedgerEntries.Add(accrual2025);

            // Usage entry dated 2025-12-27 (spanning request Dec 27 - Jan 5 = 7 days)
            // In the initial version, all days are counted in the start year (2025)
            var usage = new LeaveLedgerEntry(
                Guid.NewGuid(), tenantId, employeeId, annualType.Id,
                LeaveLedgerEntryType.Usage, -7m,
                new DateOnly(2025, 12, 27), "Spanning leave Dec-Jan", null);
            db.LedgerEntries.Add(usage);

            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<ILeaveUsageReader>();

        // Query 2025: should include the spanning leave usage
        var summary2025 = await reader.GetAnnualUsageAsync(tenantId, 2025, null, TestContext.Current.CancellationToken);
        summary2025.TotalEntitledDays.ShouldBe(20m);
        summary2025.UsedDays.ShouldBe(7m);

        // Query 2026: should have no usage (spanning leave was all dated in 2025)
        var summary2026 = await reader.GetAnnualUsageAsync(tenantId, 2026, null, TestContext.Current.CancellationToken);
        summary2026.TotalEntitledDays.ShouldBe(0m);
        summary2026.UsedDays.ShouldBe(0m);
    }
}
