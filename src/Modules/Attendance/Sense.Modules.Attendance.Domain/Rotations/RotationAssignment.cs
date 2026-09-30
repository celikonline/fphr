using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Rotations;

/// <summary>
/// Çalışanın bir <see cref="RotationPattern"/>'a bağlanması (Z3.3, spec §4): <see cref="StartDate"/> gününde döngü
/// <see cref="OffsetDays"/>. adımdan başlar (aynı kalıptaki ekiplerin kaydırılması); <see cref="EndDate"/> null = süresiz.
/// </summary>
public sealed class RotationAssignment : TenantAggregateRoot<Guid>, IAuditLogged
{
    private RotationAssignment()
    {
    }

    private RotationAssignment(Guid id, Guid tenantId, Guid employeeId, Guid rotationPatternId, DateOnly startDate, DateOnly? endDate, int offsetDays)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        RotationPatternId = rotationPatternId;
        StartDate = startDate;
        EndDate = endDate;
        OffsetDays = offsetDays;
    }

    public Guid EmployeeId { get; private set; }

    public Guid RotationPatternId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public int OffsetDays { get; private set; }

    public static Result<RotationAssignment> Create(
        Guid tenantId, Guid employeeId, Guid rotationPatternId, DateOnly startDate, DateOnly? endDate, int offsetDays)
    {
        if (employeeId == Guid.Empty || rotationPatternId == Guid.Empty || offsetDays < 0 || offsetDays >= RotationPattern.MaxCycleLengthDays
            || (endDate is not null && endDate < startDate))
        {
            return Error.Validation(AttendanceDomainErrors.RotationAssignmentRangeInvalid);
        }

        return new RotationAssignment(Guid.CreateVersion7(), tenantId, employeeId, rotationPatternId, startDate, endDate, offsetDays);
    }

    /// <summary>Atamayı <paramref name="endDate"/> günü sonunda bitirir (başlangıçtan önce olamaz).</summary>
    public Result End(DateOnly endDate)
    {
        if (endDate < StartDate)
        {
            return Error.Validation(AttendanceDomainErrors.RotationAssignmentRangeInvalid);
        }

        EndDate = endDate;
        return Result.Success();
    }

    public bool Covers(DateOnly date) => date >= StartDate && (EndDate is null || date <= EndDate);
}
