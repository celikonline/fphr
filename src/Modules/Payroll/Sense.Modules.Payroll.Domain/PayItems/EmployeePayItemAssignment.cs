using Sense.Modules.Payroll.Domain.PayItems.Formulas;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems;

/// <summary>
/// One pay item assigned to one employee for a range of months (PAY-FORMULA-PLAN "EmployeePayItemAssignment"). Own
/// aggregate because it references Employee across modules (mirrors <c>EmployeeUnionMembership</c>). Months are the
/// 1st of the month; a one-off assignment has <see cref="ValidToMonth"/> == <see cref="ValidFromMonth"/>; null end = open-ended.
/// "Closed" months (periods that are no longer Open) are passed in by the Application layer: history is never rewritten,
/// a used assignment is ended by giving it an end month.
/// </summary>
public sealed class EmployeePayItemAssignment : TenantAggregateRoot<Guid>
{
    private EmployeePayItemAssignment()
    {
    }

    private EmployeePayItemAssignment(Guid tenantId, Guid employeeId, Guid payItemDefinitionId, decimal? value, DateOnly from, DateOnly? to)
        : base(Guid.NewGuid(), tenantId)
    {
        EmployeeId = employeeId;
        PayItemDefinitionId = payItemDefinitionId;
        Value = value;
        ValidFromMonth = from;
        ValidToMonth = to;
    }

    public Guid EmployeeId { get; private set; }

    public Guid PayItemDefinitionId { get; private set; }

    /// <summary>The formula's <c>deger</c> parameter; null falls back to the definition's default value.</summary>
    public decimal? Value { get; private set; }

    public DateOnly ValidFromMonth { get; private set; }

    public DateOnly? ValidToMonth { get; private set; }

    public static DateOnly FirstOfMonth(int year, int month) => new(year, month, 1);

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    public static Result<EmployeePayItemAssignment> Create(
        Guid tenantId,
        Guid employeeId,
        PayItemDefinition definition,
        decimal? value,
        DateOnly from,
        DateOnly? to,
        IReadOnlySet<DateOnly> closedMonths)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        var fromMonth = FirstOfMonth(from);
        var toMonth = to is { } t ? FirstOfMonth(t) : (DateOnly?)null;

        if (!definition.IsActive)
        {
            return Error.Validation(PayrollDomainErrors.PayItemInactive);
        }

        if (closedMonths.Any(m => m >= fromMonth && (toMonth is null || m <= toMonth)))
        {
            return Error.Validation(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        }

        var valid = ValidateContent(definition, value, fromMonth, toMonth);
        return valid.IsFailure ? valid.Error : new EmployeePayItemAssignment(tenantId, employeeId, definition.Id, value, fromMonth, toMonth);
    }

    /// <summary>Changes the value and/or end month. Closed months are immutable: the value cannot change while one is
    /// covered, and the end month can neither cut off nor newly cover a closed month.</summary>
    public Result Update(PayItemDefinition definition, decimal? value, DateOnly? to, IReadOnlySet<DateOnly> closedMonths)
    {
        var toMonth = to is { } t ? FirstOfMonth(t) : (DateOnly?)null;
        var valid = ValidateContent(definition, value, ValidFromMonth, toMonth);
        if (valid.IsFailure)
        {
            return valid;
        }

        // A closed month must look exactly as it did: still covered, and with the same value.
        foreach (var closed in closedMonths)
        {
            var coveredNow = IsCovered(closed);
            var coveredAfter = closed >= ValidFromMonth && (toMonth is null || closed <= toMonth);
            if (coveredNow != coveredAfter || (coveredNow && value != Value))
            {
                return Error.Validation(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
            }
        }

        Value = value;
        ValidToMonth = toMonth;
        return Result.Success();
    }

    /// <summary>Deletion is only for assignments no closed period has used; otherwise end it via <see cref="Update"/>.</summary>
    public Result EnsureDeletable(IReadOnlySet<DateOnly> closedMonths) =>
        closedMonths.Any(IsCovered) ? Error.Validation(PayrollDomainErrors.PayItemAssignmentPeriodClosed) : Result.Success();

    public bool IsCovered(DateOnly monthStart) => monthStart >= ValidFromMonth && (ValidToMonth is null || monthStart <= ValidToMonth);

    public bool Overlaps(DateOnly from, DateOnly? to) =>
        (to is null || ValidFromMonth <= to) && (ValidToMonth is null || ValidToMonth >= from);

    /// <summary>Whether another assignment of the same pay item overlaps [from, to] (double-payment guard); <paramref name="ignoreId"/> excludes the assignment being edited.</summary>
    public static bool OverlapsSameItem(IEnumerable<EmployeePayItemAssignment> existing, Guid definitionId, DateOnly from, DateOnly? to, Guid? ignoreId = null) =>
        existing.Any(a => a.PayItemDefinitionId == definitionId && a.Id != ignoreId && a.Overlaps(from, to));

    /// <summary>
    /// Whether adding a range [from, to] on top of <paramref name="existing"/> would push any single month past
    /// <see cref="PayrollLimits.MaxPayItemsPerEmployeeMonth"/>. The concurrent count only changes where a range starts,
    /// so it is enough to test the new range's start and every existing start inside it.
    /// </summary>
    public static bool ExceedsMonthlyLimit(IReadOnlyCollection<EmployeePayItemAssignment> existing, DateOnly from, DateOnly? to)
    {
        var overlapping = existing.Where(a => a.Overlaps(from, to)).ToList();
        var checkpoints = overlapping.Select(a => a.ValidFromMonth).Where(m => m > from).Append(from);
        return checkpoints.Any(point => overlapping.Count(a => a.IsCovered(point)) + 1 > PayrollLimits.MaxPayItemsPerEmployeeMonth);
    }

    private static Result ValidateContent(PayItemDefinition definition, decimal? value, DateOnly fromMonth, DateOnly? toMonth)
    {
        if (toMonth is { } to && to < fromMonth)
        {
            return Error.Validation(PayrollDomainErrors.PayItemDateRangeInvalid);
        }

        if (value is { } v && Math.Abs(v) > FormulaLimits.MaxMagnitude)
        {
            return Error.Validation(PayrollDomainErrors.FormulaOverflow, ("position", 1));
        }

        if (definition.UsesValue && value is null && definition.DefaultValue is null)
        {
            return Error.Validation(PayrollDomainErrors.PayItemAssignmentValueRequired);
        }

        return Result.Success();
    }
}
