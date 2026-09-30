using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Schema;

/// <summary>
/// W0-a migration'ı (AddPunchShiftDefinitionRotationTerminal) gerçek PostgreSQL'de: okutma tekillik indeksi (silinmemişlerde),
/// kiracı bağlamı olmadan çalışan okutma sorguları, JSON sahipli koleksiyonların gidiş-dönüşü ve terminal aramaları.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class SchemaFoundationPersistenceTests : AttendancePostgresFixture
{
    private static readonly DateOnly WorkDate = new(2026, 9, 1);
    private static readonly DateTime In = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Out = new(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Punch_queries_use_explicit_tenant_and_skip_deleted_punches()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var employee = Guid.NewGuid();
        Guid deletedId;

        using (TenantContextSetter!.BeginScope(tenantA))
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.Punches.Add(NewPunch(tenantA, employee, Out, PunchDirection.Out, PunchSource.Device));
            db.Punches.Add(NewPunch(tenantA, employee, In, PunchDirection.In, PunchSource.Device));
            var deleted = NewPunch(tenantA, employee, In.AddHours(1), PunchDirection.In, PunchSource.Manual);
            deleted.SoftDelete(User, "hatalı giriş", Out).IsSuccess.ShouldBeTrue();
            deletedId = deleted.Id;
            db.Punches.Add(deleted);
            await db.SaveChangesAsync(ct);
        }

        using (TenantContextSetter.BeginScope(tenantB))
        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.Punches.Add(NewPunch(tenantB, employee, In, PunchDirection.In, PunchSource.Device));
            await db.SaveChangesAsync(ct);
        }

        // Arka plan yeniden hesaplaması gibi: kiracı bağlamı yok, tenantId açıkça verilir.
        using var read = ServiceProvider.CreateScope();
        var repository = new PunchRepository(read.ServiceProvider.GetRequiredService<AttendanceDbContext>());

        var day = await repository.ListForDayAsync(tenantA, employee, WorkDate, ct);
        day.Select(p => p.Direction).ShouldBe([PunchDirection.In, PunchDirection.Out]);
        day.ShouldAllBe(p => p.TenantId == tenantA && p.Id != deletedId);

        (await repository.ListForRangeAsync(tenantA, WorkDate, WorkDate, null, ct)).Count.ShouldBe(2);
        (await repository.ListForRangeAsync(tenantA, WorkDate, WorkDate, [Guid.NewGuid()], ct)).ShouldBeEmpty();
        (await repository.ListForRangeAsync(tenantA, WorkDate, WorkDate, [], ct)).ShouldBeEmpty();
        (await repository.ExistsAsync(tenantA, employee, In, PunchDirection.In, PunchSource.Device, ct)).ShouldBeTrue();
        (await repository.ExistsAsync(tenantA, employee, In.AddHours(1), PunchDirection.In, PunchSource.Manual, ct)).ShouldBeFalse();
        (await repository.ExistsAsync(tenantA, employee, In, PunchDirection.In, PunchSource.Terminal, ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Unique_index_blocks_duplicate_live_punch_but_allows_reentry_after_soft_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        var first = NewPunch(tenant, employee, In, PunchDirection.In, PunchSource.Terminal);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.Punches.Add(first);
            await db.SaveChangesAsync(ct);
        }

        using (var duplicate = ServiceProvider.CreateScope())
        {
            var db = duplicate.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.Punches.Add(NewPunch(tenant, employee, In, PunchDirection.In, PunchSource.Terminal));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        }

        using (var reentry = ServiceProvider.CreateScope())
        {
            var db = reentry.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var stored = await db.Punches.SingleAsync(p => p.Id == first.Id, ct);
            stored.SoftDelete(User, "cihaz çift gönderdi", Out).IsSuccess.ShouldBeTrue();
            db.Punches.Add(NewPunch(tenant, employee, In, PunchDirection.In, PunchSource.Terminal));
            await db.SaveChangesAsync(ct);
        }

        using var read = ServiceProvider.CreateScope();
        var all = await read.ServiceProvider.GetRequiredService<AttendanceDbContext>().Punches.AsNoTracking().Where(p => p.EmployeeId == employee).ToListAsync(ct);
        all.Count.ShouldBe(2);
        all.Count(p => p.IsDeleted).ShouldBe(1);
        all.Single(p => p.IsDeleted).DeleteReason.ShouldBe("cihaz çift gönderdi");
    }

    [Fact]
    public async Task Plan_entities_round_trip_including_json_collections()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        var definition = ShiftDefinition.Create(tenant, new ShiftDefinitionValues(
            "GEC", "Gece", "#000080", ShiftType.Fixed, 22 * 60, 6 * 60, StartsPreviousDay: true, null, null, null,
            [ShiftBreak.Fixed(2 * 60, 30), ShiftBreak.Flexible(15)], 10, 5, 15, 60, 30, 120, BonusMinutes: 20)).Value;
        var pattern = RotationPattern.Create(
            tenant, "3X8", "Üç vardiya", RotationPreset.ThreeByEight, 3,
            [RotationStep.Of(0, definition.Id, ShiftDayType.Work), RotationStep.Of(2, null, ShiftDayType.WeeklyRest)]).Value;
        var rotation = RotationAssignment.Create(tenant, employee, pattern.Id, WorkDate, null, 1).Value;
        var template = ShiftPlanTemplate.Create(tenant, "Hafta", 7, [ShiftPlanTemplateEntry.Of(employee, 3, definition.Id, ShiftDayType.Work)], User).Value;
        var assignment = ShiftAssignment.Create(tenant, employee, WorkDate, new TimeOnly(22, 0), new TimeOnly(6, 0), 30, true, null).Value;
        assignment.SetPlan(definition.Id, ShiftDayType.PublicHoliday).IsSuccess.ShouldBeTrue();
        var request = PunchRequest.Create(tenant, employee, WorkDate, In, PunchDirection.In, "kart okumadı").Value;

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.ShiftDefinitions.Add(definition);
            db.RotationPatterns.Add(pattern);
            db.RotationAssignments.Add(rotation);
            db.ShiftPlanTemplates.Add(template);
            db.ShiftAssignments.Add(assignment);
            db.PunchRequests.Add(request);
            await db.SaveChangesAsync(ct);
        }

        using var read = ServiceProvider.CreateScope();
        var context = read.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var loadedDefinition = (await new ShiftDefinitionRepository(context).GetByIdAsync(definition.Id, ct)).Value;
        loadedDefinition.Breaks.Select(b => (b.StartMinute, b.DurationMinutes, b.IsFlexible)).ShouldBe([(120, 30, false), (null, 15, true)]);
        loadedDefinition.StartsPreviousDay.ShouldBeTrue();
        loadedDefinition.BonusMinutes.ShouldBe(20);
        (await new ShiftDefinitionRepository(context).CodeExistsAsync(" GEC ", null, ct)).ShouldBeTrue();
        (await new ShiftDefinitionRepository(context).CodeExistsAsync("GEC", definition.Id, ct)).ShouldBeFalse();

        var rotations = new RotationRepository(context);
        (await rotations.GetPatternByIdAsync(pattern.Id, ct)).Value.Steps.Select(s => (s.DayOffset, s.ShiftDefinitionId, s.DayType))
            .ShouldBe([(0, (Guid?)definition.Id, ShiftDayType.Work), (2, null, ShiftDayType.WeeklyRest)]);
        (await rotations.ListAssignmentsOverlappingAsync(WorkDate.AddDays(10), WorkDate.AddDays(20), [employee], ct)).ShouldHaveSingleItem().OffsetDays.ShouldBe(1);
        (await rotations.ListAssignmentsOverlappingAsync(WorkDate.AddDays(-10), WorkDate.AddDays(-1), null, ct)).ShouldBeEmpty();

        (await new ShiftPlanTemplateRepository(context).GetByIdAsync(template.Id, ct)).Value.Entries.ShouldHaveSingleItem().DayOffset.ShouldBe(3);

        var loadedAssignment = await context.ShiftAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id, ct);
        loadedAssignment.ShiftDefinitionId.ShouldBe(definition.Id);
        loadedAssignment.DayType.ShouldBe(ShiftDayType.PublicHoliday);

        var loadedRequest = (await new PunchRequestRepository(context).ListForEmployeeAsync(employee, WorkDate, WorkDate, ct)).ShouldHaveSingleItem();
        loadedRequest.Status.ShouldBe(PunchRequestStatus.Pending);
        loadedRequest.OccurredAtUtc.ShouldBe(In);
    }

    [Fact]
    public async Task Terminal_lookups_resolve_across_tenants_by_prefix_and_credential_per_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var employee = Guid.NewGuid();

        using (TenantContextSetter!.BeginScope(tenantA))
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var group = TerminalGroup.Create(tenantA, "Merkez").Value;
            db.TerminalGroups.Add(group);
            db.Terminals.Add(Terminal.Create(tenantA, "T-01", "Ana giriş", group.Id, null, TerminalDirectionMode.Both, "hash-a", "pfx_a").Value);
            var inactive = Terminal.Create(tenantA, "T-02", "Eski", null, null, TerminalDirectionMode.Both, "hash-old", "pfx_a").Value;
            inactive.Deactivate();
            db.Terminals.Add(inactive);
            db.EmployeeTerminalCredentials.Add(EmployeeTerminalCredential.Create(tenantA, employee, TerminalCredentialKind.Card, "0012345").Value);
            await db.SaveChangesAsync(ct);
        }

        using (TenantContextSetter.BeginScope(tenantB))
        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.EmployeeTerminalCredentials.Add(EmployeeTerminalCredential.Create(tenantB, Guid.NewGuid(), TerminalCredentialKind.Card, "0012345").Value);
            await db.SaveChangesAsync(ct);
        }

        using var read = ServiceProvider.CreateScope();
        var terminals = new TerminalRepository(read.ServiceProvider.GetRequiredService<AttendanceDbContext>());

        var byPrefix = await terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync("pfx_a", ct);
        byPrefix.ShouldHaveSingleItem().TenantId.ShouldBe(tenantA);
        byPrefix[0].ApiKeyHash.ShouldBe("hash-a");

        var credential = await terminals.FindActiveCredentialAsync(tenantA, TerminalCredentialKind.Card, " 0012345 ", ct);
        credential.ShouldNotBeNull().EmployeeId.ShouldBe(employee);
        (await terminals.FindActiveCredentialAsync(tenantA, TerminalCredentialKind.Pin, "0012345", ct)).ShouldBeNull();
    }

    private static Punch NewPunch(Guid tenantId, Guid employeeId, DateTime occurredAtUtc, PunchDirection direction, PunchSource source) =>
        Punch.Create(tenantId, employeeId, occurredAtUtc, WorkDate, direction, source, null, null, null, null, null, source == PunchSource.Manual, User, null).Value;
}
