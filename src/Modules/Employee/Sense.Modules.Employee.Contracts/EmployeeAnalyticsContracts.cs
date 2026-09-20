namespace Sense.Modules.Employee.Contracts;

/// <summary>Career records started in a period, grouped by the "career path" change types (roadmap #36/#99 CAREER_PATH_RATIO).</summary>
public sealed record CareerPathChangeCounts(int Promotion, int LateralMove, int Demotion, int TitleChange)
{
    public int Total => Promotion + LateralMove + Demotion + TitleChange;
}

/// <summary>Terminations in a period (roadmap #99 turnover KPI); <see cref="WithinFirstSixMonths"/> is the early-attrition slice (tenure &lt; 182 days at termination).</summary>
public sealed record TerminationCounts(int Total, int WithinFirstSixMonths);

/// <summary>Active-employee headcount for one (org unit, gender) pair (roadmap #99c "cinsiyet × pozisyon grubu"); null OrgUnitId groups employees with no unit assigned.</summary>
public sealed record GenderByOrgUnitCount(Guid? OrgUnitId, string Gender, int Count);

/// <summary>
/// Read-only cross-module workforce KPI inputs: lets Analytics compute KPIs without depending on Employee's
/// Domain or Infrastructure. Implemented by Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeAnalyticsQueries
{
    /// <summary>Count of employees with <c>Status == Active</c> for the tenant, right now.</summary>
    Task<int> GetActiveHeadcountAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Career records whose ValidFrom falls in [from, to], grouped by change type.</summary>
    Task<CareerPathChangeCounts> GetCareerPathChangeCountsAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Active-employee headcount by gender (key = Gender.ToString(), e.g. "Male"/"Female"/"Unspecified"/"Other").</summary>
    Task<IReadOnlyDictionary<string, int>> GetGenderHeadcountAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Terminations (any <c>TerminationType</c>) whose TerminationDate falls in [from, to].</summary>
    Task<TerminationCounts> GetTerminationCountsAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Active-employee headcount grouped by (OrgUnitId, Gender). The caller resolves OrgUnitId to a display name via Organization.Contracts.</summary>
    Task<IReadOnlyList<GenderByOrgUnitCount>> GetGenderByOrgUnitAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active-employee headcount grouped by (Department-level org unit id, Gender) - unlike
    /// <see cref="GetGenderByOrgUnitAsync"/>, which groups by each employee's raw (usually deepest, e.g. Title-level)
    /// OrgUnitId, this walks the org placement chain (senseik-046, same resolution as the employee card's
    /// DepartmentName and GetDepartmentDistributionHandler) so employees under different titles in the same
    /// department are counted together. Null groups employees with no Department-level ancestor (unassigned or
    /// placed above Department, e.g. directly on the company).
    /// </summary>
    Task<IReadOnlyList<GenderByOrgUnitCount>> GetGenderByDepartmentAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
