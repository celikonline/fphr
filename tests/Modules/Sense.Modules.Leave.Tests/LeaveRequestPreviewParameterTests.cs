using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// A12: the preview endpoints bind non-nullable query values, so a missing/misspelled parameter used to arrive as
/// 0001-01-01 / Guid.Empty and return 200. The handlers must reject it before touching any repository (the collaborators
/// below are never reached, hence the null placeholders).
/// </summary>
public sealed class LeaveRequestPreviewParameterTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 5);

    private static PreviewMyLeaveRequestHandler MyHandler()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(EmployeeId);
        return new PreviewMyLeaveRequestHandler(
            Substitute.For<ITenantContext>(), currentUser, null!, Substitute.For<IEmployeeLeaveProfileRepository>(), Substitute.For<ILeaveRuleSetRepository>(),
            Substitute.For<ILeaveDayCalculator>(), Substitute.For<IHolidayCalendarLookup>(), Substitute.For<IEmployeeLookup>(), Substitute.For<IEmployeeDirectory>(),
            Substitute.For<ILeaveRequestRepository>());
    }

    private static PreviewEmployeeLeaveRequestHandler EmployeeHandler()
    {
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return new PreviewEmployeeLeaveRequestHandler(
            Substitute.For<ITenantContext>(), dataScope, null!, Substitute.For<IEmployeeLeaveProfileRepository>(), Substitute.For<ILeaveRuleSetRepository>(),
            Substitute.For<ILeaveDayCalculator>(), Substitute.For<IHolidayCalendarLookup>());
    }

    public static TheoryData<Guid, DateOnly, DateOnly, string> InvalidParameters => new()
    {
        { Guid.Empty, Day, Day, LeaveErrors.PreviewParametersRequired },
        { TypeId, default, Day, LeaveErrors.PreviewParametersRequired },
        { TypeId, Day, default, LeaveErrors.PreviewParametersRequired },
        { TypeId, default, default, LeaveErrors.PreviewParametersRequired },
        { TypeId, Day.AddDays(1), Day, LeaveErrors.InvalidRange },
        { TypeId, Day, Day.AddDays(LeaveLimits.PreviewMaxRangeDays), LeaveErrors.PreviewRangeTooLarge },
    };

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public async Task MyPreview_RejectsInvalidParameters_AsValidationError(Guid leaveTypeId, DateOnly start, DateOnly end, string expectedCode)
    {
        var result = await MyHandler().Handle(new PreviewMyLeaveRequestQuery(leaveTypeId, start, end, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public async Task EmployeePreview_RejectsInvalidParameters_AsValidationError(Guid leaveTypeId, DateOnly start, DateOnly end, string expectedCode)
    {
        var result = await EmployeeHandler().Handle(new PreviewEmployeeLeaveRequestQuery(EmployeeId, leaveTypeId, start, end), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public async Task Preview_MaxAllowedRange_PassesTheParameterGuard()
    {
        var result = await MyHandler().Handle(
            new PreviewMyLeaveRequestQuery(TypeId, Day, Day.AddDays(LeaveLimits.PreviewMaxRangeDays - 1), null), CancellationToken.None);

        // Profile lookup is a substitute returning null: the guard let it through and the request failed later, not on the parameters.
        result.Error.Code.ShouldBe(LeaveErrors.ProfileNotFound);
    }
}
