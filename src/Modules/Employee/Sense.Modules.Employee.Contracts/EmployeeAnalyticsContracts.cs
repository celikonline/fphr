namespace Sense.Modules.Employee.Contracts;

/// <summary>Career records started in a period, grouped by the "career path" change types (roadmap #36/#99 CAREER_PATH_RATIO).</summary>
public sealed record CareerPathChangeCounts(int Promotion, int LateralMove, int Demotion, int TitleChange)
{
    public int Total => Promotion + LateralMove + Demotion + TitleChange;
}

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
}
