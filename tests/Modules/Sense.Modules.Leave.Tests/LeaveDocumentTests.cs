using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>Denetim raporu A5-belge: RequiresDocument türünde belgesiz talep reddedilir, belge eki talebe bağlanır.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveDocumentTests : TestFixture
{
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IDocumentAttachmentLookup _documents = Substitute.For<IDocumentAttachmentLookup>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly, typeof(ILeaveRuleSetRepository).Assembly, typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);

        var holidays = Substitute.For<IHolidayCalendarLookup>();
        holidays.ResolveCalendarAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((HolidayCalendarRef?)null);
        services.AddSingleton(holidays);
        services.AddSingleton(Substitute.For<IEmployeeLookup>());

        // Varsayılan: istenen her belge Active (katı senaryolar test içinde yeniden yapılandırır).
        _documents.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
                .ToDictionary(id => id, id => new AttachedDocumentInfo(id, "rapor.pdf", "application/pdf", 1024)));
        services.AddSingleton(_documents);

        // Detay derleyici: kişi adları boş, onay durumu yok (ek listesi etkilenmez).
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact>());
        services.AddSingleton(directory);
        services.AddSingleton(Substitute.For<IApprovalRequestLookup>());
        services.AddSingleton(Substitute.For<IOrgUnitLookup>());

        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
        services.AddScoped<IEmployeeBirthDateLookup, EmployeeBirthDateLookup>();
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private async Task<Result<T>> RunAsync<THandler, T>(Func<THandler, Task<Result<T>>> run, params object[] args)
        where THandler : notnull
    {
        using var scope = ServiceProvider!.CreateScope();
        var result = await run(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider, args));
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    private async Task<Result> RunAsync<THandler>(Func<THandler, Task<Result>> run, params object[] args)
        where THandler : notnull
    {
        using var scope = ServiceProvider!.CreateScope();
        var result = await run(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider, args));
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    private static DateOnly NextWeekMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
    }

    private async Task<(Guid TenantId, Guid EmployeeId, Guid SickId, Guid AnnualId)> HireAsync()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var accrualStart = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), Arg.Any<Guid>(), null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Guid.NewGuid()));
        _workflow.CancelAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), employeeId, Arg.Any<CancellationToken>()).Returns(true);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var read = ServiceProvider.CreateScope();
        var types = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types;
        return (tenantId, employeeId, types.Single(t => t.Code == LeaveTypeCode.Sick).Id, types.Single(t => t.Code == LeaveTypeCode.Annual).Id);
    }

    [Fact]
    public async Task Submit_SickWithoutDocument_IsRejected()
    {
        var (tenantId, _, sickId, _) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var result = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(sickId, monday, monday, null), CancellationToken.None),
            _currentUser, _workflow);

        result.Error.Code.ShouldBe(LeaveRequestErrors.DocumentRequired);
    }

    [Fact]
    public async Task Submit_SickWithUnknownDocument_IsRejected()
    {
        var (tenantId, _, sickId, _) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        // Katı: yalnız docId Active, yabancı kimlik yok.
        var docId = Guid.NewGuid();
        _documents.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
                .Where(id => id == docId)
                .ToDictionary(id => id, id => new AttachedDocumentInfo(id, "rapor.pdf", "application/pdf", 1024)));
        var monday = NextWeekMonday();

        var result = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(sickId, monday, monday, null, DocumentIds: [Guid.NewGuid()]), CancellationToken.None),
            _currentUser, _workflow);

        result.Error.Code.ShouldBe(LeaveRequestErrors.DocumentNotFound);
    }

    [Fact]
    public async Task Submit_SickWithDocument_Succeeds_AndDetailShowsAttachment()
    {
        var (tenantId, _, sickId, _) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();
        var docId = Guid.NewGuid();

        var submitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(sickId, monday, monday, null, DocumentIds: [docId]), CancellationToken.None),
            _currentUser, _workflow);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        using (var verify = ServiceProvider!.CreateScope())
        {
            var row = await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().RequestAttachments.SingleAsync();
            row.LeaveRequestId.ShouldBe(submitted.Value.Id);
            row.DocumentId.ShouldBe(docId);
            row.FileName.ShouldBe("rapor.pdf");
        }

        var detail = await RunAsync<GetMyLeaveRequestHandler, LeaveRequestDetailDto>(
            h => h.Handle(new GetMyLeaveRequestQuery(submitted.Value.Id), CancellationToken.None),
            _currentUser);
        detail.Value.Attachments.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.DocumentId.ShouldBe(docId),
            a => a.FileName.ShouldBe("rapor.pdf"));
    }

    [Fact]
    public async Task Submit_AnnualWithoutDocument_Succeeds()
    {
        var (tenantId, _, _, annualId) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var submitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null), CancellationToken.None),
            _currentUser, _workflow);

        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
    }

    [Fact]
    public async Task UpdatePending_ChangeToSickWithoutAttachment_IsRejected()
    {
        var (tenantId, _, sickId, annualId) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var submitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null), CancellationToken.None),
            _currentUser, _workflow);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        var edited = await RunAsync<UpdatePendingLeaveRequestHandler>(
            h => h.Handle(new UpdatePendingLeaveRequestCommand(submitted.Value.Id, sickId, monday, monday, null), CancellationToken.None),
            _currentUser, _workflow);

        edited.Error.Code.ShouldBe(LeaveRequestErrors.DocumentRequired);
    }

    [Fact]
    public async Task OnBehalf_BypassSkipsDocumentCheck_ButApprovalPathRequiresIt()
    {
        var (tenantId, employeeId, sickId, _) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var bypassed = await RunAsync<CreateLeaveRequestOnBehalfHandler, LeaveRequestDto>(
            h => h.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, sickId, monday, monday, null, true), CancellationToken.None),
            _currentUser, _dataScope, _workflow);
        bypassed.IsSuccess.ShouldBeTrue(bypassed.IsFailure ? bypassed.Error.Code : null);

        var viaApproval = await RunAsync<CreateLeaveRequestOnBehalfHandler, LeaveRequestDto>(
            h => h.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, sickId, monday.AddDays(7), monday.AddDays(7), null, false), CancellationToken.None),
            _currentUser, _dataScope, _workflow);
        viaApproval.Error.Code.ShouldBe(LeaveRequestErrors.DocumentRequired);
    }

    [Fact]
    public async Task AttachDetach_OwnerPending_Works_StrangerAndDetachedStatesFail()
    {
        var (tenantId, employeeId, _, annualId) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();
        var docId = Guid.NewGuid();

        var submitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null), CancellationToken.None),
            _currentUser, _workflow);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
        var requestId = submitted.Value.Id;

        (await RunAsync<AttachMyLeaveDocumentHandler>(
            h => h.Handle(new AttachMyLeaveDocumentCommand(requestId, docId), CancellationToken.None),
            _currentUser)).IsSuccess.ShouldBeTrue();
        // Yinelenen ek idempotent.
        (await RunAsync<AttachMyLeaveDocumentHandler>(
            h => h.Handle(new AttachMyLeaveDocumentCommand(requestId, docId), CancellationToken.None),
            _currentUser)).IsSuccess.ShouldBeTrue();

        using (var verify = ServiceProvider!.CreateScope())
        {
            (await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().RequestAttachments.CountAsync()).ShouldBe(1);
        }

        // Başkasının talebi "yok" hükmünde.
        _currentUser.EmployeeId.Returns(Guid.NewGuid());
        (await RunAsync<AttachMyLeaveDocumentHandler>(
            h => h.Handle(new AttachMyLeaveDocumentCommand(requestId, Guid.NewGuid()), CancellationToken.None),
            _currentUser)).Error.Code.ShouldBe(LeaveDomainErrors.RequestNotFound);
        _currentUser.EmployeeId.Returns(employeeId);

        (await RunAsync<DetachMyLeaveDocumentHandler>(
            h => h.Handle(new DetachMyLeaveDocumentCommand(requestId, docId), CancellationToken.None),
            _currentUser)).IsSuccess.ShouldBeTrue();
        (await RunAsync<DetachMyLeaveDocumentHandler>(
            h => h.Handle(new DetachMyLeaveDocumentCommand(requestId, docId), CancellationToken.None),
            _currentUser)).Error.Code.ShouldBe(LeaveRequestErrors.AttachmentNotFound);
    }

    [Fact]
    public async Task Attach_AfterWithdraw_IsRejected()
    {
        var (tenantId, _, _, annualId) = await HireAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var submitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null), CancellationToken.None),
            _currentUser, _workflow);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        (await RunAsync<WithdrawLeaveRequestHandler>(
            h => h.Handle(new WithdrawLeaveRequestCommand(submitted.Value.Id), CancellationToken.None),
            _currentUser, _workflow)).IsSuccess.ShouldBeTrue();

        (await RunAsync<AttachMyLeaveDocumentHandler>(
            h => h.Handle(new AttachMyLeaveDocumentCommand(submitted.Value.Id, Guid.NewGuid()), CancellationToken.None),
            _currentUser)).Error.Code.ShouldBe(LeaveDomainErrors.RequestNotPending);
    }

    [Fact]
    public void Attachment_RejectsEmptyIdsAndBlankOrTooLongFileName()
    {
        LeaveRequestAttachment.Attach(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "a.pdf", null, DateTime.UtcNow)
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidAttachment);
        LeaveRequestAttachment.Attach(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "a.pdf", null, DateTime.UtcNow)
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidAttachment);
        LeaveRequestAttachment.Attach(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  ", null, DateTime.UtcNow)
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidAttachment);
        LeaveRequestAttachment.Attach(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', LeaveLimits.AttachmentFileNameMaxLength + 1), null, DateTime.UtcNow)
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidAttachment);
        LeaveRequestAttachment.Attach(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  rapor.pdf ", null, DateTime.UtcNow)
            .Value.FileName.ShouldBe("rapor.pdf");
    }
}
