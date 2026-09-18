using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Analytics.Application.Kpis;

/// <summary>KPI keys used across the roadmap (11.7 §99) - "hazır" (computable today) ones first, others land as their prerequisites do.</summary>
public static class KpiKeys
{
    public const string CareerPathRatio = "CAREER_PATH_RATIO";
    public const string GenderRatio = "GENDER_RATIO";
    public const string TurnoverRate = "TURNOVER_RATE";
    public const string EarlyTurnoverRate = "EARLY_TURNOVER_RATE";
    public const string GenderRatioByUnit = "GENDER_RATIO_BY_UNIT";
}

/// <summary>
/// One dimension of a KPI's breakdown, e.g. one gender's share of <see cref="KpiKeys.GenderRatio"/>. <see cref="Count"/>/
/// <see cref="Share"/> are null exactly when <see cref="IsSuppressed"/> is true (G-10-21, 11.3.12 "Küçük hücre
/// gizleme") - the dimension label still tells the caller the category exists, just not how many people are in it.
/// </summary>
public sealed record KpiBreakdownDto(string Dimension, int? Count, decimal? Share, bool IsSuppressed = false);

/// <summary>One computed KPI value for the requested period.</summary>
public sealed record KpiResultDto(string Key, string Label, decimal Value, string Unit, DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<KpiBreakdownDto> Breakdown);

/// <summary>
/// Company-wide workforce KPIs computable from data that already exists (roadmap Faz 7 #99). Defaults to the
/// current calendar year when no period is given. Only KPIs whose prerequisite fields exist today are included -
/// see docs/analysis/kolayik-video/faz7-durum-takibi.md for what's still blocked and why.
/// </summary>
[RequiresPermission("analytics.kpi.view")]
public sealed record GetWorkforceKpisQuery(DateOnly? From = null, DateOnly? To = null) : IQuery<IReadOnlyList<KpiResultDto>>;

public sealed class GetWorkforceKpisHandler(
    ITenantContext tenant, IEmployeeAnalyticsQueries employeeAnalytics, IOrgUnitLookup orgUnits, IDataScopeService dataScope, TimeProvider clock)
    : IQueryHandler<GetWorkforceKpisQuery, IReadOnlyList<KpiResultDto>>
{
    public async Task<Result<IReadOnlyList<KpiResultDto>>> Handle(GetWorkforceKpisQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = query.From ?? new DateOnly(to.Year, 1, 1);

        // "Company" scope (Owner/HRAdmin/... - the roles this permission ships with by default) sees every KPI.
        // Anything narrower (a unit manager's Department/Team/Location scope) sees only the unit breakdown, filtered
        // to their accessible units - the other four KPIs are tenant-wide aggregates that Employee.Contracts doesn't
        // expose pre-filtered by employee/unit yet, so excluding them (rather than showing a wrong/unscoped number)
        // is the safe choice; widening them needs an Employee.Contracts query shape change (cross-module, not ours).
        var accessibleUnitIds = await dataScope.GetAccessibleOrgUnitIdsAsync(AnalyticsPermissions.KpiView.Key, cancellationToken);
        var unitNames = (await orgUnits.ListActiveUnitsAsync(tenant.TenantId, cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        var genderByUnit = await employeeAnalytics.GetGenderByOrgUnitAsync(tenant.TenantId, cancellationToken);
        if (accessibleUnitIds is not null)
        {
            genderByUnit = genderByUnit.Where(g => g.OrgUnitId is { } id && accessibleUnitIds.Contains(id)).ToList();
        }

        var results = new List<KpiResultDto> { BuildGenderRatioByUnit(from, to, genderByUnit, unitNames) };

        if (accessibleUnitIds is null)
        {
            var activeHeadcount = await employeeAnalytics.GetActiveHeadcountAsync(tenant.TenantId, cancellationToken);
            var careerChanges = await employeeAnalytics.GetCareerPathChangeCountsAsync(tenant.TenantId, from, to, cancellationToken);
            var genderHeadcount = await employeeAnalytics.GetGenderHeadcountAsync(tenant.TenantId, cancellationToken);
            var terminations = await employeeAnalytics.GetTerminationCountsAsync(tenant.TenantId, from, to, cancellationToken);

            results.Insert(0, BuildCareerPathRatio(from, to, careerChanges));
            results.Insert(1, BuildGenderRatio(from, to, genderHeadcount));
            results.Insert(2, BuildTurnoverRate(from, to, terminations, activeHeadcount));
            results.Insert(3, BuildEarlyTurnoverRate(from, to, terminations));
        }

        return Result.Success<IReadOnlyList<KpiResultDto>>(results);
    }

    /// <summary>
    /// G-10-20: 11.3.12'nin tanımı terfi / (terfi + yatay geçiş) - kademe düşürme ve unvan değişikliği paya
    /// katılmaz (TitleChange'in yatay sayılması `Analytics.CareerPath.CountTitleChangeAsLateral` tenant ayarına
    /// bağlıydı; o ayar hiç eklenmedi, varsayılan false ile tutarlı kalınıp dışarıda bırakıldı). Önceki sürüm yanlışlıkla
    /// dört değişiklik türünün toplamını payda alıyordu - kırılım (hâlâ dört türün de dönem içi payını gösterir) değişmedi.
    /// </summary>
    private static KpiResultDto BuildCareerPathRatio(DateOnly from, DateOnly to, CareerPathChangeCounts changes)
    {
        var promotionAndLateral = changes.Promotion + changes.LateralMove;
        var value = promotionAndLateral == 0 ? 0m : Math.Round((decimal)changes.Promotion / promotionAndLateral, 4);
        var breakdown = new List<KpiBreakdownDto>
        {
            ShareOf(nameof(changes.Promotion), changes.Promotion, changes.Total),
            ShareOf(nameof(changes.LateralMove), changes.LateralMove, changes.Total),
            ShareOf(nameof(changes.Demotion), changes.Demotion, changes.Total),
            ShareOf(nameof(changes.TitleChange), changes.TitleChange, changes.Total),
        };

        return new KpiResultDto(KpiKeys.CareerPathRatio, "Career Path Ratio", value, "ratio", from, to, breakdown);
    }

    private static KpiResultDto BuildGenderRatio(DateOnly from, DateOnly to, IReadOnlyDictionary<string, int> genderHeadcount)
    {
        var total = genderHeadcount.Values.Sum();
        var breakdown = genderHeadcount
            .Select(g => ShareOf(g.Key, g.Value, total))
            .OrderByDescending(b => b.Count)
            .ToList();

        // No single "value" makes sense for a multi-category distribution; Value carries the total headcount instead.
        return new KpiResultDto(KpiKeys.GenderRatio, "Gender Ratio", total, "headcount", from, to, breakdown);
    }

    private static KpiResultDto BuildTurnoverRate(DateOnly from, DateOnly to, TerminationCounts terminations, int activeHeadcount)
    {
        // Approximation, consistent with CareerPathRatio: current active headcount stands in for the period's
        // average headcount (no point-in-time historical headcount query yet).
        var value = activeHeadcount == 0 ? 0m : Math.Round((decimal)terminations.Total / activeHeadcount, 4);
        return new KpiResultDto(KpiKeys.TurnoverRate, "Turnover Rate", value, "ratio", from, to, []);
    }

    private static KpiResultDto BuildEarlyTurnoverRate(DateOnly from, DateOnly to, TerminationCounts terminations)
    {
        // Share of the period's terminations whose tenure was under 6 months - not a cohort-based new-hire
        // attrition rate (roadmap's "turnover ilk 6 ay"), which would need a hire cohort + follow-up window.
        var value = terminations.Total == 0 ? 0m : Math.Round((decimal)terminations.WithinFirstSixMonths / terminations.Total, 4);
        var breakdown = new List<KpiBreakdownDto> { ShareOf(nameof(terminations.WithinFirstSixMonths), terminations.WithinFirstSixMonths, terminations.Total) };
        return new KpiResultDto(KpiKeys.EarlyTurnoverRate, "Early Turnover Rate", value, "ratio", from, to, breakdown);
    }

    private static KpiResultDto BuildGenderRatioByUnit(
        DateOnly from, DateOnly to, IReadOnlyList<GenderByOrgUnitCount> genderByUnit, IReadOnlyDictionary<Guid, string> unitNames)
    {
        var total = genderByUnit.Sum(g => g.Count);
        var breakdown = genderByUnit
            .Select(g => ShareOf($"{UnitLabel(g.OrgUnitId, unitNames)} / {g.Gender}", g.Count, total))
            .OrderByDescending(b => b.Count)
            .ToList();

        return new KpiResultDto(KpiKeys.GenderRatioByUnit, "Gender Ratio by Unit", total, "headcount", from, to, breakdown);
    }

    private static string UnitLabel(Guid? orgUnitId, IReadOnlyDictionary<Guid, string> unitNames) =>
        orgUnitId is { } id && unitNames.TryGetValue(id, out var name) ? name : "Unassigned";

    /// <summary>
    /// G-10-21 (11.3.12 "Küçük hücre gizleme"): a non-zero count under <see cref="MinCellSize"/> is precise enough to
    /// point at a specific person (e.g. "Demotion: 1", "Muğla Şubesi / Kadın: 2") - its exact count/share never
    /// leaves the API, only that the dimension exists and was suppressed. Zero stays visible (identifies no one).
    /// Every KPI breakdown goes through this, not just gender-by-unit - career/turnover cells are just as
    /// identifying in a small team. Hard-coded default until a tenant-configurable
    /// <c>TenantSettings.Analytics.MinCellSize</c> exists.
    /// </summary>
    private const int MinCellSize = 5;

    private static KpiBreakdownDto ShareOf(string dimension, int count, int total)
    {
        if (count > 0 && count < MinCellSize)
        {
            return new KpiBreakdownDto(dimension, Count: null, Share: null, IsSuppressed: true);
        }

        return new KpiBreakdownDto(dimension, count, total == 0 ? 0m : Math.Round((decimal)count / total, 4));
    }
}
