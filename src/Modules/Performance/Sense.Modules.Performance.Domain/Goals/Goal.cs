using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain.Goals;

/// <summary>Hedef/OKR durumu.</summary>
public enum GoalStatus
{
    Open = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3,

    /// <summary>Frozen because the owning employee left the company (see <see cref="Goal.Archive"/>) - not a user-facing transition.</summary>
    Archived = 4,
}

/// <summary>
/// An employee's goal/OKR with a 0-100 progress percentage. <see cref="EmployeeId"/> is a raw cross-module reference
/// (validated against Employee.Contracts.IEmployeeLookup at creation time, not stored as a navigation - modules talk
/// only through Contracts). <see cref="ReviewCycleId"/> is optional: a goal may exist outside any review cycle.
/// </summary>
public sealed class Goal : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Goal()
    {
    }

    private Goal(Guid id, Guid tenantId, Guid employeeId, string title, string? description, DateOnly targetDate, Guid? reviewCycleId)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Title = title;
        Description = description;
        TargetDate = targetDate;
        ReviewCycleId = reviewCycleId;
        Status = GoalStatus.Open;
        Progress = 0;
    }

    public Guid EmployeeId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateOnly TargetDate { get; private set; }

    public Guid? ReviewCycleId { get; private set; }

    /// <summary>0-100.</summary>
    public int Progress { get; private set; }

    public GoalStatus Status { get; private set; }

    public static Result<Goal> Create(Guid tenantId, Guid employeeId, string title, string? description, DateOnly targetDate, Guid? reviewCycleId, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(PerformanceDomainErrors.GoalTitleRequired);
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : Guard.MaxLength(description.Trim(), PerformanceLimits.DescriptionMaxLength);

        return new Goal(
            id ?? Guid.CreateVersion7(), tenantId, employeeId, Guard.MaxLength(title.Trim(), PerformanceLimits.NameMaxLength), trimmedDescription, targetDate, reviewCycleId);
    }

    /// <summary>0 -> Open, 1-99 -> InProgress, 100 -> Completed (terminal). Rejected once the goal is terminal.</summary>
    public Result UpdateProgress(int percent)
    {
        if (Status is GoalStatus.Cancelled or GoalStatus.Archived or GoalStatus.Completed)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotUpdatable);
        }

        if (percent is < 0 or > 100)
        {
            return Error.Validation(PerformanceDomainErrors.GoalProgressInvalid);
        }

        Progress = percent;
        Status = percent switch
        {
            100 => GoalStatus.Completed,
            0 => GoalStatus.Open,
            _ => GoalStatus.InProgress,
        };

        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is GoalStatus.Completed or GoalStatus.Cancelled or GoalStatus.Archived)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotCancellable);
        }

        Status = GoalStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>
    /// EmployeeTerminated tüketildiğinde çağrılır (bkz. Employees/EmployeeTerminatedHandler.cs): açık hedefi
    /// silmek/iptal etmek yerine dondurur - denetim izini (KVKK + 4857 md.18 fesih delili) korur. Zaten
    /// tamamlanmış/iptal edilmiş/arşivlenmiş bir hedef için no-op (idempotent, outbox redelivery güvenli).
    /// </summary>
    public void Archive()
    {
        if (Status is GoalStatus.Cancelled or GoalStatus.Completed or GoalStatus.Archived)
        {
            return;
        }

        Status = GoalStatus.Archived;
    }
}
