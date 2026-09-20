using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>Ürün kararı (denetim A10): self-servis talebin başlangıcı en fazla 7 gün geriye gidebilir; İK adına ekleme muaf.</summary>
public sealed partial class LeaveRequestIntegrationTests
{
    private static DateOnly NextWeekdayOnOrAfter(DateOnly day)
    {
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private async Task<(Guid TenantId, Guid EmployeeId, DateOnly Today)> ArrangeEmployeeAsync()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Guid.NewGuid()));
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));
        return (tenantId, employeeId, today);
    }

    private async Task<Guid> AnnualTypeIdAsync()
    {
        using var read = ServiceProvider!.CreateScope();
        return (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
    }

    [Fact]
    public async Task Submit_StartMoreThanSevenDaysInThePast_IsRejected()
    {
        var (tenantId, _, today) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var annualId = await AnnualTypeIdAsync();

        // Rejected before any day counting, so the weekday of the date does not matter.
        var tooOld = await SubmitAsync(tenantId, annualId, today.AddDays(-8), today.AddDays(-8));
        tooOld.IsFailure.ShouldBeTrue();
        tooOld.Error.Code.ShouldBe(LeaveRequestErrors.StartDateTooFarInPast);
        tooOld.Error.Args!["max"].ShouldBe(LeaveBackdatePolicy.SelfServiceMaxBackdateDays);
        (await SubmitAsync(tenantId, annualId, today.AddDays(-30), today.AddDays(-25))).Error.Code.ShouldBe(LeaveRequestErrors.StartDateTooFarInPast);
    }

    [Fact]
    public async Task Submit_StartWithinTheLastSevenDays_IsAccepted()
    {
        var (tenantId, _, today) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var annualId = await AnnualTypeIdAsync();

        var day = NextWeekdayOnOrAfter(today.AddDays(-7));
        var submitted = await SubmitAsync(tenantId, annualId, day, day);

        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
    }

    [Fact]
    public async Task Preview_FlagsAStartMoreThanSevenDaysBack_ButNotExactlySevenDaysBack()
    {
        var (tenantId, _, today) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var annualId = await AnnualTypeIdAsync();
        using var scope = ServiceProvider!.CreateScope();
        var preview = ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser);

        var tooOld = await preview.Handle(new PreviewMyLeaveRequestQuery(annualId, today.AddDays(-8), today.AddDays(-8), null), CancellationToken.None);
        tooOld.Value.Warnings.ShouldContain(w => w.Code == LeaveRequestErrors.StartDateTooFarInPast);

        var boundary = await preview.Handle(new PreviewMyLeaveRequestQuery(annualId, today.AddDays(-7), today.AddDays(-7), null), CancellationToken.None);
        boundary.Value.Warnings.ShouldNotContain(w => w.Code == LeaveRequestErrors.StartDateTooFarInPast);
    }

    [Fact]
    public async Task UpdatePending_MovingTheStartMoreThanSevenDaysBack_IsRejected()
    {
        var (tenantId, _, today) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var annualId = await AnnualTypeIdAsync();
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        var submitted = await SubmitAsync(tenantId, annualId, monday, monday);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        using var scope = ServiceProvider!.CreateScope();
        var update = ActivatorUtilities.CreateInstance<UpdatePendingLeaveRequestHandler>(scope.ServiceProvider, _currentUser, _workflow);
        var result = await update.Handle(
            new UpdatePendingLeaveRequestCommand(submitted.Value.Id, annualId, today.AddDays(-30), today.AddDays(-30), null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveRequestErrors.StartDateTooFarInPast);
    }

    [Fact]
    public async Task OnBehalf_IsExemptFromTheBackdateLimit_IncludingBypass()
    {
        var (tenantId, employeeId, today) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var annualId = await AnnualTypeIdAsync();
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCreateOnBehalf.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsBypassApproval.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        using var scope = ServiceProvider!.CreateScope();

        var old = NextWeekdayOnOrAfter(today.AddDays(-30));
        var onBehalf = ActivatorUtilities.CreateInstance<CreateLeaveRequestOnBehalfHandler>(scope.ServiceProvider, _currentUser, dataScope, _workflow);
        var created = await onBehalf.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, old, old, null, true), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);

        var preview = await ActivatorUtilities.CreateInstance<PreviewEmployeeLeaveRequestHandler>(scope.ServiceProvider, dataScope)
            .Handle(new PreviewEmployeeLeaveRequestQuery(employeeId, annualId, today.AddDays(-60), today.AddDays(-60)), CancellationToken.None);
        preview.Value.Warnings.ShouldNotContain(w => w.Code == LeaveRequestErrors.StartDateTooFarInPast);
    }
}
