using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Profiles;

/// <summary>One SGK teşvik kanunu kodu (5510, 6111, 7103, 27103 vb.) currently in effect for at least one employee,
/// and how many employees carry it.</summary>
public sealed record IncentiveSummaryItemDto(string Code, int EmployeeCount);

/// <summary>
/// Read-only "aktif teşvik türleri" summary for the Organizasyon/Şirket profil ekranı (senseik-010/F-03). Per
/// docs/handoffs/employee-sicil-uyruk.md §3: no writable teşvik field is added to the Company profile - the data is
/// employee-scoped (EmployeePayrollProfile.IncentiveLawCode is the single source of truth), so the profile screen
/// only shows which teşvik codes are currently active tenant-wide, as of <paramref name="AsOf"/> (defaults to today).
/// </summary>
[RequiresPermission("payroll.profile.manage")]
public sealed record GetActiveIncentiveSummaryQuery(DateOnly? AsOf = null) : IQuery<IReadOnlyList<IncentiveSummaryItemDto>>;

public sealed class GetActiveIncentiveSummaryHandler(IEmployeePayrollProfileRepository profiles)
    : IQueryHandler<GetActiveIncentiveSummaryQuery, IReadOnlyList<IncentiveSummaryItemDto>>
{
    public async Task<Result<IReadOnlyList<IncentiveSummaryItemDto>>> Handle(GetActiveIncentiveSummaryQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var active = await profiles.ListActiveAsOfAsync(asOf, cancellationToken);

        IReadOnlyList<IncentiveSummaryItemDto> summary = active
            .Where(p => !string.IsNullOrWhiteSpace(p.IncentiveLawCode))
            .GroupBy(p => p.IncentiveLawCode!)
            .Select(g => new IncentiveSummaryItemDto(g.Key, g.Select(p => p.EmployeeId).Distinct().Count()))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ToList();

        return Result.Success(summary);
    }
}
