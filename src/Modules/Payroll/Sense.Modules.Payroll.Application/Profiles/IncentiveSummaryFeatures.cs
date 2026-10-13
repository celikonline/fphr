using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Profiles;

/// <summary>
/// One SGK teşvik kanunu kodu (5510, 6111, 7103, 27103 vb.) currently in effect for at least one employee, and how many employees
/// carry it. <see cref="Source"/> is <c>profile</c> (the payroll profile's free-text law code, for employees without an SGK
/// assignment) or <c>assignment</c> (a PAY-INCENTIVE assignment; then <see cref="Name"/> and <see cref="IsSmmmApproved"/> describe
/// the definition and <see cref="Code"/> is its declaration code, or its own code in the tax category).
/// </summary>
public sealed record IncentiveSummaryItemDto(
    string Code,
    int EmployeeCount,
    string? Name = null,
    string Source = IncentiveSummarySources.Profile,
    bool? IsSmmmApproved = null);

public static class IncentiveSummarySources
{
    public const string Profile = "profile";
    public const string Assignment = "assignment";
}

/// <summary>
/// Read-only "aktif teşvik türleri" summary for the Organizasyon/Şirket profil ekranı (senseik-010/F-03). Per
/// docs/handoffs/employee-sicil-uyruk.md §3: no writable teşvik field is added to the Company profile - the profile screen
/// only shows which teşvik codes are currently active tenant-wide, as of <paramref name="AsOf"/> (defaults to today). Two sources,
/// never counting an employee twice for the same incentive: employees WITHOUT an effective SGK assignment count under their profile
/// code (the profile code is ignored once an SGK assignment is in force), assigned employees count under their definition.
/// </summary>
[RequiresPermission("payroll.profile.manage")]
public sealed record GetActiveIncentiveSummaryQuery(DateOnly? AsOf = null) : IQuery<IReadOnlyList<IncentiveSummaryItemDto>>;

public sealed class GetActiveIncentiveSummaryHandler(
    IEmployeePayrollProfileRepository profiles,
    IEmployeeIncentiveAssignmentRepository incentiveAssignments,
    IIncentiveDefinitionRepository incentiveDefinitions)
    : IQueryHandler<GetActiveIncentiveSummaryQuery, IReadOnlyList<IncentiveSummaryItemDto>>
{
    public async Task<Result<IReadOnlyList<IncentiveSummaryItemDto>>> Handle(GetActiveIncentiveSummaryQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var active = await profiles.ListActiveAsOfAsync(asOf, cancellationToken);
        var plan = await IncentiveMonthPlan.LoadAsync(asOf, incentiveAssignments, incentiveDefinitions, cancellationToken);

        var profileItems = active
            .Where(p => !string.IsNullOrWhiteSpace(p.IncentiveLawCode) && plan.EffectiveOfCategory(p.EmployeeId, IncentiveCategory.Sgk) is null)
            .GroupBy(p => p.IncentiveLawCode!)
            .Select(g => new IncentiveSummaryItemDto(g.Key, g.Select(p => p.EmployeeId).Distinct().Count()));

        var assignmentItems = plan.EmployeeIds
            .SelectMany(id => new[] { IncentiveCategory.Sgk, IncentiveCategory.Tax }.Select(category => plan.EffectiveOfCategory(id, category)).OfType<IncentiveAssignmentInMonth>())
            .GroupBy(p => p.Definition.Id)
            .Select(g =>
            {
                var definition = g.First().Definition;
                return new IncentiveSummaryItemDto(
                    definition.DeclarationCode ?? definition.Code, g.Select(p => p.Assignment.EmployeeId).Distinct().Count(),
                    definition.Name, IncentiveSummarySources.Assignment, definition.IsSmmmApproved);
            });

        IReadOnlyList<IncentiveSummaryItemDto> summary = profileItems
            .Concat(assignmentItems)
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        return Result.Success(summary);
    }
}
