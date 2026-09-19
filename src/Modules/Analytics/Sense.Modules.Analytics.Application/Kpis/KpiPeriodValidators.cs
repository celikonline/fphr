using FluentValidation;

namespace Sense.Modules.Analytics.Application.Kpis;

/// <summary>KPI dönem sınırları: kabul edilebilir yıl aralığı (DateOnly/takvim hesabı bu aralık dışında anlamsız veya hata verir).</summary>
internal static class KpiPeriodLimits
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const int MinMonth = 1;
    public const int MaxMonth = 12;
}

/// <summary>year/month eksik (0) veya aralık dışı ise handler'a inmeden 400 döner (önceden 500 üretiyordu).</summary>
public sealed class GetOvertimeCostKpiValidator : AbstractValidator<GetOvertimeCostKpiQuery>
{
    public GetOvertimeCostKpiValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(KpiPeriodLimits.MinYear, KpiPeriodLimits.MaxYear);
        RuleFor(x => x.Month).InclusiveBetween(KpiPeriodLimits.MinMonth, KpiPeriodLimits.MaxMonth);
    }
}

public sealed class GetLeaveUsageKpiValidator : AbstractValidator<GetLeaveUsageKpiQuery>
{
    public GetLeaveUsageKpiValidator() =>
        RuleFor(x => x.Year!.Value).InclusiveBetween(KpiPeriodLimits.MinYear, KpiPeriodLimits.MaxYear).When(x => x.Year.HasValue);
}
