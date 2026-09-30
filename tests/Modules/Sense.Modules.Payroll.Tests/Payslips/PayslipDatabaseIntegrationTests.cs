using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Payroll.Application.Payslips;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using static Sense.Modules.Payroll.Tests.Payslips.PayslipTestKit;

namespace Sense.Modules.Payroll.Tests.Payslips;

/// <summary>
/// E2 against real PostgreSQL (Testcontainers, see TestFixture): the xmin optimistic-concurrency token makes the K4
/// "once" rule hold across requests, and the acknowledgement_history jsonb / 'None' / '[]' defaults round-trip.
/// Goes through PayrollDbContext + the real repositories and saver; two DI scopes stand for two concurrent requests
/// that both loaded the payslip before either saved.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PayslipDatabaseIntegrationTests : TestFixture
{
    private static readonly DateTimeOffset FirstAt = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondAt = new(2026, 9, 10, 9, 0, 1, TimeSpan.Zero);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<PayrollDbContext>(configuration, PayrollDbContext.SchemaName);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PayrollDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Concurrent_acknowledgements_only_one_wins_and_evidence_is_the_winners()
    {
        var (tenantId, employeeId, payslipId) = await SeedPublishedAsync();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        using var first = ServiceProvider!.CreateScope();
        using var second = ServiceProvider!.CreateScope();
        var firstDb = Db(first);
        var secondDb = Db(second);
        await Load(firstDb, payslipId);
        await Load(secondDb, payslipId);

        var won = await Acknowledge(firstDb, employeeId, "198.51.100.1", FirstAt).Handle(new AcknowledgeMyPayslipCommand(payslipId, "First/1.0"), default);
        var lost = await Acknowledge(secondDb, employeeId, "198.51.100.2", SecondAt).Handle(new AcknowledgeMyPayslipCommand(payslipId, "Second/1.0"), default);

        won.IsSuccess.ShouldBeTrue();
        lost.Error.Code.ShouldBe(PayrollDomainErrors.PayslipConcurrentUpdate);
        lost.Error.Type.ShouldBe(ErrorType.Conflict);
        var stored = await ReadFreshAsync(payslipId);
        stored.AcknowledgedAt.ShouldBe(FirstAt);
        stored.AcknowledgedFromIp.ShouldBe("198.51.100.1");
        stored.AcknowledgedUserAgent.ShouldBe("First/1.0");
    }

    [Fact]
    public async Task Parallel_acknowledgements_leave_exactly_one_success()
    {
        var (tenantId, employeeId, payslipId) = await SeedPublishedAsync();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        using var first = ServiceProvider!.CreateScope();
        using var second = ServiceProvider!.CreateScope();
        var firstDb = Db(first);
        var secondDb = Db(second);
        await Load(firstDb, payslipId);
        await Load(secondDb, payslipId);

        var results = await Task.WhenAll(
            Acknowledge(firstDb, employeeId, "198.51.100.1", FirstAt).Handle(new AcknowledgeMyPayslipCommand(payslipId, "A"), default),
            Acknowledge(secondDb, employeeId, "198.51.100.2", SecondAt).Handle(new AcknowledgeMyPayslipCommand(payslipId, "B"), default));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Single(r => r.IsFailure).Error.Code.ShouldBe(PayrollDomainErrors.PayslipConcurrentUpdate);
        var winner = results.Single(r => r.IsSuccess).Value;
        var stored = await ReadFreshAsync(payslipId);
        stored.AcknowledgedAt.ShouldBe(winner.AcknowledgedAt);
        stored.AcknowledgedUserAgent.ShouldBe(winner.AcknowledgedAt == FirstAt ? "A" : "B");
    }

    [Fact]
    public async Task Acknowledge_racing_an_objection_ends_acknowledged_without_a_workflow()
    {
        var (tenantId, employeeId, payslipId) = await SeedPublishedAsync();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        using var ackScope = ServiceProvider!.CreateScope();
        using var objectScope = ServiceProvider!.CreateScope();
        var ackDb = Db(ackScope);
        var objectDb = Db(objectScope);
        await Load(ackDb, payslipId);
        await Load(objectDb, payslipId);
        var workflow = Substitute.For<IWorkflowService>();

        var acknowledged = await Acknowledge(ackDb, employeeId, "198.51.100.1", FirstAt).Handle(new AcknowledgeMyPayslipCommand(payslipId, null), default);
        var objected = await Object(objectDb, employeeId, workflow).Handle(new ObjectToMyPayslipCommand(payslipId, "Prim eksik", null), default);

        acknowledged.IsSuccess.ShouldBeTrue();
        objected.Error.Code.ShouldBe(PayrollDomainErrors.PayslipConcurrentUpdate);
        await workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
        var stored = await ReadFreshAsync(payslipId);
        stored.AcknowledgedAt.ShouldBe(FirstAt);
        stored.ObjectionStatus.ShouldBe(PayslipObjectionStatus.None);
        stored.ObjectionReason.ShouldBeNull();
    }

    [Fact]
    public async Task Objection_racing_an_acknowledgement_ends_pending_and_unacknowledged()
    {
        var (tenantId, employeeId, payslipId) = await SeedPublishedAsync();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        using var ackScope = ServiceProvider!.CreateScope();
        using var objectScope = ServiceProvider!.CreateScope();
        var ackDb = Db(ackScope);
        var objectDb = Db(objectScope);
        await Load(ackDb, payslipId);
        await Load(objectDb, payslipId);
        var approvalId = Guid.NewGuid();
        var workflow = Substitute.For<IWorkflowService>();
        workflow.StartAsync(default!, default, default, default, default, default, default).ReturnsForAnyArgs(Task.FromResult(Result.Success(approvalId)));

        var objected = await Object(objectDb, employeeId, workflow).Handle(new ObjectToMyPayslipCommand(payslipId, "Prim eksik", null), default);
        await objectDb.SaveChangesAsync(); // what UnitOfWorkBehaviour does after a successful command
        var acknowledged = await Acknowledge(ackDb, employeeId, "198.51.100.1", SecondAt).Handle(new AcknowledgeMyPayslipCommand(payslipId, null), default);

        objected.IsSuccess.ShouldBeTrue();
        acknowledged.Error.Code.ShouldBe(PayrollDomainErrors.PayslipConcurrentUpdate);
        var stored = await ReadFreshAsync(payslipId);
        stored.AcknowledgedAt.ShouldBeNull();
        stored.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Pending);
        stored.ObjectionApprovalRequestId.ShouldBe(approvalId);
    }

    [Fact]
    public async Task Acknowledgement_history_round_trips_through_jsonb()
    {
        var (tenantId, employeeId, payslipId) = await SeedPublishedAsync();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        var approvalId = Guid.NewGuid();

        await MutateAsync(payslipId, p => p.Acknowledge(FirstAt, "203.0.113.7", "Mozilla/5.0 (Test)"));
        await MutateAsync(payslipId, p => p.Recalculate("{}", "hash2", 51_000m, 39_000m, 61_000m));
        await MutateAsync(payslipId, p =>
        {
            p.Publish(SecondAt);
            p.FileObjection("Prim eksik", SecondAt);
            p.AttachObjectionApproval(approvalId);
            p.RejectObjection(approvalId, SecondAt.AddDays(1));
        });
        await MutateAsync(payslipId, p => p.Recalculate("{}", "hash3", 52_000m, 40_000m, 62_000m));

        var stored = await ReadFreshAsync(payslipId);
        stored.AcknowledgementHistory.Count.ShouldBe(2);
        stored.AcknowledgementHistory[0].ShouldBe(new PayslipAcknowledgementRecord(
            1, PaidAt, FirstAt, "203.0.113.7", "Mozilla/5.0 (Test)", null, null, null, null, null));
        stored.AcknowledgementHistory[1].ShouldBe(new PayslipAcknowledgementRecord(
            2, SecondAt, null, null, null, PayslipObjectionStatus.Rejected, "Prim eksik", SecondAt, approvalId, SecondAt.AddDays(1)));
        stored.Version.ShouldBe(3);
        stored.PublishedAt.ShouldBeNull();
        stored.AcknowledgedAt.ShouldBeNull();
        stored.AcknowledgedFromIp.ShouldBeNull();
        stored.ObjectionStatus.ShouldBe(PayslipObjectionStatus.None);
        stored.NetAmount.ShouldBe(40_000m);
        (await ScalarAsync($"select acknowledgement_history::text as \"Value\" from payroll.payslips where id = '{payslipId}'"))
            .ShouldContain("\"objectionStatus\": \"Rejected\""); // jsonb text form: enum as text
    }

    [Fact]
    public async Task Objection_status_is_stored_as_text_and_read_back()
    {
        var (tenantId, _, payslipId) = await SeedPublishedAsync();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);

        await MutateAsync(payslipId, p => p.FileObjection("Prim eksik", FirstAt));

        (await ScalarAsync($"select objection_status as \"Value\" from payroll.payslips where id = '{payslipId}'")).ShouldBe("Pending");
        (await ReadFreshAsync(payslipId)).ObjectionStatus.ShouldBe(PayslipObjectionStatus.Pending);
    }

    [Fact]
    public async Task Legacy_row_without_the_new_columns_reads_with_defaults_and_can_be_recalculated()
    {
        var tenantId = Guid.NewGuid();
        var payslipId = Guid.NewGuid();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        using (var scope = ServiceProvider!.CreateScope())
        {
            await Db(scope).Database.ExecuteSqlInterpolatedAsync($$"""
                insert into payroll.payslips (id, tenant_id, employee_id, payroll_period_id, input_snapshot, input_hash, version,
                    gross_amount, net_amount, employer_cost, cumulative_gv_base, created_at)
                values ({{payslipId}}, {{tenantId}}, {{Guid.NewGuid()}}, {{Guid.NewGuid()}}, '{}'::jsonb, 'h', 1, 100, 80, 130, 0, now())
                """);
        }

        var legacy = await ReadFreshAsync(payslipId);
        legacy.ObjectionStatus.ShouldBe(PayslipObjectionStatus.None);
        legacy.AcknowledgementHistory.ShouldBeEmpty();
        legacy.PublishedAt.ShouldBeNull();

        await MutateAsync(payslipId, p => p.Recalculate("{}", "hash2", 110m, 88m, 140m));
        (await ReadFreshAsync(payslipId)).Version.ShouldBe(2);
    }

    private static PayrollDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<PayrollDbContext>();

    private static Task<Payslip?> Load(PayrollDbContext db, Guid payslipId) => new PayslipRepository(db).FindByIdAsync(payslipId);

    private static AcknowledgeMyPayslipHandler Acknowledge(PayrollDbContext db, Guid employeeId, string ip, DateTimeOffset at) =>
        new(CurrentEmployee(employeeId, ip), new PayslipRepository(db), new PayrollPeriodRepository(db), new PayslipChangeSaver(db), ClockAt(at));

    private static ObjectToMyPayslipHandler Object(PayrollDbContext db, Guid employeeId, IWorkflowService workflow) =>
        new(CurrentEmployee(employeeId), new PayslipRepository(db), new PayrollPeriodRepository(db), workflow, new PayslipChangeSaver(db), ClockAt(FirstAt));

    /// <summary>A paid period of a fresh tenant with one published payslip.</summary>
    private async Task<(Guid TenantId, Guid EmployeeId, Guid PayslipId)> SeedPublishedAsync()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = Db(scope);
        var period = PayrollPeriod.Create(tenantId, 2026, 8).Value;
        period.Approve(null);
        period.MarkPaid();
        var payslip = Payslip.CreateFirstVersion(tenantId, employeeId, period.Id, "{}", "hash", 50_000m, 38_000m, 60_000m);
        payslip.Publish(PaidAt);
        db.Periods.Add(period);
        db.Payslips.Add(payslip);
        await db.SaveChangesAsync();
        return (tenantId, employeeId, payslip.Id);
    }

    /// <summary>Load in a fresh context, change in place, save - so change tracking of the jsonb list is exercised.</summary>
    private async Task MutateAsync(Guid payslipId, Action<Payslip> change)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = Db(scope);
        var payslip = await db.Payslips.SingleAsync(p => p.Id == payslipId);
        change(payslip);
        await db.SaveChangesAsync();
    }

    private async Task<Payslip> ReadFreshAsync(Guid payslipId)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await Db(scope).Payslips.AsNoTracking().SingleAsync(p => p.Id == payslipId);
    }

    private async Task<string> ScalarAsync(string sql)
    {
        using var scope = ServiceProvider!.CreateScope();
        return await Db(scope).Database.SqlQueryRaw<string>(sql).SingleAsync();
    }
}
