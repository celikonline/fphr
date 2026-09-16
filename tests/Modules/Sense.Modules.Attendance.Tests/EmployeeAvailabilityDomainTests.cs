using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Availability;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class EmployeeAvailabilityDomainTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);

    [Fact]
    public void Create_WholeDay_NullStartAndEnd_Succeeds()
    {
        var result = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, null, null, AvailabilityKind.Unavailable, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Start.ShouldBeNull();
        result.Value.End.ShouldBeNull();
        result.Value.Status.ShouldBe(AvailabilityStatus.Active);
    }

    [Fact]
    public void Create_PartialWindow_Succeeds()
    {
        var result = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), new TimeOnly(12, 0), AvailabilityKind.WeeklyRestRequest, "not");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_OnlyStartProvided_Fails()
    {
        var result = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), null, AvailabilityKind.Unavailable, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.AvailabilityTimeRangeInvalid);
    }

    [Fact]
    public void Create_StartAfterEnd_Fails()
    {
        var result = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(12, 0), new TimeOnly(9, 0), AvailabilityKind.Unavailable, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.AvailabilityTimeRangeInvalid);
    }

    [Fact]
    public void ConflictsWith_WholeDayBlock_AlwaysConflicts()
    {
        var availability = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, null, null, AvailabilityKind.Unavailable, null).Value;

        availability.ConflictsWith(new TimeOnly(0, 0), new TimeOnly(23, 59)).ShouldBeTrue();
    }

    [Fact]
    public void ConflictsWith_PartialWindow_OverlapsOnly()
    {
        var availability = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), new TimeOnly(12, 0), AvailabilityKind.Unavailable, null).Value;

        availability.ConflictsWith(new TimeOnly(11, 0), new TimeOnly(13, 0)).ShouldBeTrue();
        availability.ConflictsWith(new TimeOnly(13, 0), new TimeOnly(15, 0)).ShouldBeFalse();
    }

    [Fact]
    public void ConflictsWith_CancelledEntry_NeverConflicts()
    {
        var availability = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, null, null, AvailabilityKind.Unavailable, null).Value;
        availability.Cancel();

        availability.ConflictsWith(new TimeOnly(9, 0), new TimeOnly(10, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Fails()
    {
        var availability = EmployeeAvailability.Create(Guid.NewGuid(), Guid.NewGuid(), Date, null, null, AvailabilityKind.Unavailable, null).Value;
        availability.Cancel();

        var result = availability.Cancel();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.AvailabilityAlreadyCancelled);
    }
}
