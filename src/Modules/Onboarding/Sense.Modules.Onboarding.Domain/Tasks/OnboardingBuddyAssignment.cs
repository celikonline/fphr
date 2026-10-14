using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain.Tasks;

/// <summary>A named peer buddy who supports a new hire during their onboarding period.</summary>
public sealed class OnboardingBuddyAssignment : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OnboardingBuddyAssignment() { }

    private OnboardingBuddyAssignment(Guid id, Guid tenantId, Guid employeeId, Guid buddyEmployeeId,
        DateOnly startDate, DateOnly? endDate) : base(id, tenantId)
    {
        EmployeeId = employeeId;
        BuddyEmployeeId = buddyEmployeeId;
        StartDate = startDate;
        EndDate = endDate;
    }

    public Guid EmployeeId { get; private set; }
    public Guid BuddyEmployeeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    public static Result<OnboardingBuddyAssignment> Create(Guid tenantId, Guid employeeId, Guid buddyEmployeeId,
        DateOnly startDate, DateOnly? endDate)
    {
        if (employeeId == Guid.Empty || buddyEmployeeId == Guid.Empty || employeeId == buddyEmployeeId)
            return Error.Validation("onboarding.buddy.invalid_employee");
        if (endDate is { } end && end < startDate)
            return Error.Validation("onboarding.buddy.invalid_date_range");
        return new OnboardingBuddyAssignment(Guid.CreateVersion7(), tenantId, employeeId, buddyEmployeeId, startDate, endDate);
    }

    public Result Update(Guid buddyEmployeeId, DateOnly startDate, DateOnly? endDate)
    {
        if (buddyEmployeeId == Guid.Empty || buddyEmployeeId == EmployeeId)
            return Error.Validation("onboarding.buddy.invalid_employee");
        if (endDate is { } end && end < startDate)
            return Error.Validation("onboarding.buddy.invalid_date_range");
        BuddyEmployeeId = buddyEmployeeId;
        StartDate = startDate;
        EndDate = endDate;
        return Result.Success();
    }
}
