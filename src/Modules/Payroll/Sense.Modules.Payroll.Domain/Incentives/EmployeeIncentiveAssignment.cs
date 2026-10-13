using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Incentives;

/// <summary>
/// One incentive assigned to one employee for a range of months (PAY-INCENTIVE-PLAN "EmployeeIncentiveAssignment").
/// Same shape and closed-month rules as <c>EmployeePayItemAssignment</c>: months are the 1st of the month, null end =
/// open-ended, history is never rewritten (a used assignment is ended by giving it an end month). Extra rules: an
/// employee's assignments of the same <see cref="IncentiveCategory"/> cannot overlap, <c>MaxMonths</c> bounds the total
/// months of one definition per employee, and an SGK-category definition needs a declaration code. Assigning an
/// unapproved definition is allowed (its code is declared, but it does not enter the calculation).
/// </summary>
public sealed class EmployeeIncentiveAssignment : TenantAggregateRoot<Guid>
{
    public const int NoteMaxLength = 500;

    private EmployeeIncentiveAssignment()
    {
    }

    private EmployeeIncentiveAssignment(Guid tenantId, Guid employeeId, Guid incentiveDefinitionId, DateOnly from, DateOnly? to, string? note)
        : base(Guid.NewGuid(), tenantId)
    {
        EmployeeId = employeeId;
        IncentiveDefinitionId = incentiveDefinitionId;
        ValidFromMonth = from;
        ValidToMonth = to;
        Note = note;
    }

    public Guid EmployeeId { get; private set; }

    public Guid IncentiveDefinitionId { get; private set; }

    public DateOnly ValidFromMonth { get; private set; }

    public DateOnly? ValidToMonth { get; private set; }

    public string? Note { get; private set; }

    public static DateOnly FirstOfMonth(int year, int month) => new(year, month, 1);

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <param name="existing">All assignments of the same employee (any definition).</param>
    /// <param name="categoryOf">The category of an existing assignment's definition.</param>
    public static Result<EmployeeIncentiveAssignment> Create(
        Guid tenantId,
        Guid employeeId,
        IncentiveDefinition definition,
        DateOnly from,
        DateOnly? to,
        string? note,
        IReadOnlySet<DateOnly> closedMonths,
        IReadOnlyCollection<EmployeeIncentiveAssignment> existing,
        Func<Guid, IncentiveCategory> categoryOf)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        var fromMonth = FirstOfMonth(from);
        var toMonth = to is { } t ? FirstOfMonth(t) : (DateOnly?)null;

        if (!definition.IsActive)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveInactive);
        }

        if (closedMonths.Any(m => m >= fromMonth && (toMonth is null || m <= toMonth)))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
        }

        var valid = ValidateContent(definition, fromMonth, toMonth, note, existing, null);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        if (definition.Category == IncentiveCategory.Sgk && definition.DeclarationCode is null)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
        }

        if (OverlapsSameCategory(existing, definition.Category, categoryOf, fromMonth, toMonth, null))
        {
            return Error.Conflict(PayrollDomainErrors.IncentiveAssignmentOverlap);
        }

        return new EmployeeIncentiveAssignment(tenantId, employeeId, definition.Id, fromMonth, toMonth, NormalizeNote(note));
    }

    /// <summary>Changes the end month and/or note. A closed month must look exactly as it did: the end month can neither
    /// cut off nor newly cover one. The extended range must still respect the category overlap and max months rules.</summary>
    public Result Update(
        IncentiveDefinition definition,
        DateOnly? to,
        string? note,
        IReadOnlySet<DateOnly> closedMonths,
        IReadOnlyCollection<EmployeeIncentiveAssignment> existing,
        Func<Guid, IncentiveCategory> categoryOf)
    {
        var toMonth = to is { } t ? FirstOfMonth(t) : (DateOnly?)null;
        var valid = ValidateContent(definition, ValidFromMonth, toMonth, note, existing, Id);
        if (valid.IsFailure)
        {
            return valid;
        }

        foreach (var closed in closedMonths)
        {
            var coveredAfter = closed >= ValidFromMonth && (toMonth is null || closed <= toMonth);
            if (IsCovered(closed) != coveredAfter)
            {
                return Error.Validation(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
            }
        }

        if (OverlapsSameCategory(existing, definition.Category, categoryOf, ValidFromMonth, toMonth, Id))
        {
            return Error.Conflict(PayrollDomainErrors.IncentiveAssignmentOverlap);
        }

        ValidToMonth = toMonth;
        Note = NormalizeNote(note);
        return Result.Success();
    }

    /// <summary>Deletion is only for assignments no closed period has used; otherwise end it via <see cref="Update"/>.</summary>
    public Result EnsureDeletable(IReadOnlySet<DateOnly> closedMonths) =>
        closedMonths.Any(IsCovered) ? Error.Validation(PayrollDomainErrors.IncentiveAssignmentPeriodClosed) : Result.Success();

    public bool IsCovered(DateOnly monthStart) => monthStart >= ValidFromMonth && (ValidToMonth is null || monthStart <= ValidToMonth);

    public bool Overlaps(DateOnly from, DateOnly? to) =>
        (to is null || ValidFromMonth <= to) && (ValidToMonth is null || ValidToMonth >= from);

    /// <summary>Months the range covers; null for an open-ended range.</summary>
    public int? MonthCount => ValidToMonth is { } to ? MonthSpan(ValidFromMonth, to) : null;

    /// <summary>Whether another assignment of the same category as <paramref name="category"/> overlaps [from, to].</summary>
    public static bool OverlapsSameCategory(
        IEnumerable<EmployeeIncentiveAssignment> existing,
        IncentiveCategory category,
        Func<Guid, IncentiveCategory> categoryOf,
        DateOnly from,
        DateOnly? to,
        Guid? ignoreId) =>
        existing.Any(a => a.Id != ignoreId && categoryOf(a.IncentiveDefinitionId) == category && a.Overlaps(from, to));

    private static int MonthSpan(DateOnly from, DateOnly to) => ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static Result ValidateContent(
        IncentiveDefinition definition,
        DateOnly fromMonth,
        DateOnly? toMonth,
        string? note,
        IReadOnlyCollection<EmployeeIncentiveAssignment> existing,
        Guid? ignoreId)
    {
        if (toMonth is { } to && to < fromMonth)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveDateRangeInvalid);
        }

        if (NormalizeNote(note) is { Length: > NoteMaxLength })
        {
            return Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", "note"));
        }

        if (definition.MaxMonths is not { } maxMonths)
        {
            return Result.Success();
        }

        if (toMonth is not { } end)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveAssignmentEndRequired);
        }

        // An open-ended sibling (created before MaxMonths was set) has an unbounded duration: it always exceeds the limit.
        var siblings = existing.Where(a => a.IncentiveDefinitionId == definition.Id && a.Id != ignoreId).ToList();
        if (siblings.Any(a => a.MonthCount is null))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveAssignmentExceedsMaxMonths, ("maxMonths", maxMonths));
        }

        var total = siblings.Sum(a => a.MonthCount!.Value) + MonthSpan(fromMonth, end);
        return total > maxMonths
            ? Error.Validation(PayrollDomainErrors.IncentiveAssignmentExceedsMaxMonths, ("maxMonths", maxMonths))
            : Result.Success();
    }
}
