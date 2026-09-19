using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Unions;

/// <summary>Editable content of an <see cref="EmployeeUnionMembership"/> (11.3.7 "Sendika / TİS").</summary>
public sealed record EmployeeUnionMembershipData(
    Guid UnionId,
    DateOnly MembershipDate,
    DateOnly NotifiedToEmployerAt,
    UnionCoverageType CoverageType,
    UnionDuesRule DuesRule,
    decimal DuesValue,
    DateOnly? EndDate);

/// <summary>
/// One employee's union/TİS coverage (11.3.7 "Sendika / TİS": `EmployeeUnionMembership{EmployeeId, UnionId,
/// MembershipDate, NotifiedToEmployerAt, CoverageType, DuesRule, DuesValue, EndDate?}`). Fields are shown on the
/// Employee card but ownership is Payroll's, per the source doc. Cross-module reference to Employee, so this is its
/// own aggregate (mirrors <c>EmployeePayrollProfile</c>) rather than a child of Employee's aggregate; at most one
/// open (EndDate == null) membership per employee is enforced by the Application layer via the repository.
/// </summary>
public sealed class EmployeeUnionMembership : TenantAggregateRoot<Guid>
{
    private EmployeeUnionMembership()
    {
    }

    private EmployeeUnionMembership(Guid tenantId, Guid employeeId, EmployeeUnionMembershipData data)
        : base(Guid.NewGuid(), tenantId)
    {
        EmployeeId = employeeId;
        Apply(data);
    }

    public Guid EmployeeId { get; private set; }

    public Guid UnionId { get; private set; }

    public DateOnly MembershipDate { get; private set; }

    /// <summary>Date the employer was formally notified; dues deduction starts the first period AFTER this date's month (11.3.7 "kesinti başlangıcı").</summary>
    public DateOnly NotifiedToEmployerAt { get; private set; }

    public UnionCoverageType CoverageType { get; private set; }

    public UnionDuesRule DuesRule { get; private set; }

    public decimal DuesValue { get; private set; }

    /// <summary>Null = still covered.</summary>
    public DateOnly? EndDate { get; private set; }

    public bool IsOpen => EndDate is null;

    public static Result<EmployeeUnionMembership> Create(Guid tenantId, Guid employeeId, EmployeeUnionMembershipData data)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        var validated = Validate(data);
        return validated.IsFailure ? validated.Error : new EmployeeUnionMembership(tenantId, employeeId, data);
    }

    public Result Update(EmployeeUnionMembershipData data)
    {
        var validated = Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        Apply(data);
        return Result.Success();
    }

    /// <summary>Ends coverage (employee left the union, or the TİS/employment ended) as of <paramref name="endDate"/>.</summary>
    public Result End(DateOnly endDate)
    {
        if (endDate < MembershipDate)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipDateRangeInvalid);
        }

        EndDate = endDate;
        return Result.Success();
    }

    /// <summary>
    /// Whether dues should be deducted for the payroll period starting <paramref name="periodMonthStart"/> (the 1st
    /// of the period's month). Only <see cref="UnionCoverageType.Member"/>/<see cref="UnionCoverageType.SolidarityDues"/>
    /// pay anything; <see cref="UnionCoverageType.UnionApproved"/>/<see cref="UnionCoverageType.Excluded"/> never do.
    /// Deduction starts the first period after the month <see cref="NotifiedToEmployerAt"/> falls in, and stops once
    /// the period is after the month <see cref="EndDate"/> falls in (11.3.7 "kesinti başlangıcı = NotifiedToEmployerAt
    /// sonrası ilk dönem").
    /// </summary>
    public bool AppliesDuesForPeriod(DateOnly periodMonthStart)
    {
        if (CoverageType is not (UnionCoverageType.Member or UnionCoverageType.SolidarityDues))
        {
            return false;
        }

        var firstEligibleMonth = FirstOfMonth(NotifiedToEmployerAt).AddMonths(1);
        if (periodMonthStart < firstEligibleMonth)
        {
            return false;
        }

        if (EndDate is { } end && periodMonthStart > FirstOfMonth(end))
        {
            return false;
        }

        return true;
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private void Apply(EmployeeUnionMembershipData data)
    {
        UnionId = data.UnionId;
        MembershipDate = data.MembershipDate;
        NotifiedToEmployerAt = data.NotifiedToEmployerAt;
        CoverageType = data.CoverageType;
        DuesRule = data.DuesRule;
        DuesValue = data.DuesValue;
        EndDate = data.EndDate;
    }

    private static Result Validate(EmployeeUnionMembershipData data)
    {
        if (data.UnionId == Guid.Empty)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipUnionRequired);
        }

        if (data.NotifiedToEmployerAt < data.MembershipDate)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipDateRangeInvalid);
        }

        if (data.EndDate is { } endDate && endDate < data.MembershipDate)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipDateRangeInvalid);
        }

        var duesApplies = data.CoverageType is UnionCoverageType.Member or UnionCoverageType.SolidarityDues;
        if (duesApplies && data.DuesValue <= 0m)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipDuesValueInvalid);
        }

        if (data.DuesValue < 0m)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipDuesValueInvalid);
        }

        if (data.DuesRule == UnionDuesRule.PercentOfGross && data.DuesValue > 1m)
        {
            return Error.Validation(PayrollDomainErrors.UnionMembershipDuesValueInvalid);
        }

        return Result.Success();
    }
}
