using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Equalization;

public enum EqualizationPeriodStatus
{
    Open = 0,
    Closed = 1,
}

/// <summary>
/// Denkleştirme dönemi (m.63, kart senseik-014 §4): bir çalışanın ardışık <c>ShiftSettings.EqualizationPeriodMonths</c>
/// aylık döneminde toplam çalışma süresi haftalık 45 saat ortalamasını aşmadığı sürece günlük/haftalık taşkınlar kesin
/// fazla mesai SAYILMAZ - yalnız dönem kapandığında ortalamayı aşan kısım kesinleşir (<see cref="TrueUpOvertimeMinutes"/>).
/// Açıkken <see cref="TotalWorkedMinutes"/> ve türetilen fazla mesai PROVİZYONDUR; bordroya bu haliyle kesin
/// yansıtılmamalıdır (bkz. handoff notu "Implementasyon Notu"). <see cref="Compensatory.CompensatoryWorkLedger"/> (telafi
/// çalışması, m.64, kısa çalışma haftalarının borcu) ile KARIŞTIRILMAMALIDIR - farklı bir mevzuat maddesi ve farklı bir
/// kavram: bu tip fazla çalışma taşkınlarının dönemsel ortalamasını, o ise eksik çalışılan haftaların borcunu izler.
/// </summary>
public sealed class EqualizationPeriod : TenantAggregateRoot<Guid>
{
    /// <summary>4857 md.63: haftalık 45 saat.</summary>
    public const int WeeklyBudgetMinutes = 45 * 60;

    private EqualizationPeriod()
    {
    }

    private EqualizationPeriod(Guid id, Guid tenantId, Guid employeeId, DateOnly periodStart, DateOnly periodEnd, int budgetMinutes)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        BudgetMinutes = budgetMinutes;
        Status = EqualizationPeriodStatus.Open;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    /// <summary>Dönem içinde (bugüne kadar, açıkken) toplanan net çalışma dakikası - her yeniden hesaplamada baştan yazılır (bkz. Application feature), artımlı biriktirilmez.</summary>
    public int TotalWorkedMinutes { get; private set; }

    /// <summary>45s × <see cref="EqualizationPeriodCalculator.CountWeeksByMonday"/> sonucu hafta sayısı.</summary>
    public int BudgetMinutes { get; private set; }

    /// <summary>Yalnız <see cref="Close"/> sonrası anlamlı: TotalWorkedMinutes − BudgetMinutes (0'da taban).</summary>
    public int TrueUpOvertimeMinutes { get; private set; }

    public EqualizationPeriodStatus Status { get; private set; }

    public static EqualizationPeriod Open(Guid tenantId, Guid employeeId, DateOnly periodStart, DateOnly periodEnd, int weeksInPeriod) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, periodStart, periodEnd, weeksInPeriod * WeeklyBudgetMinutes);

    /// <summary>Recomputes the running (provisional) total while the period is still open - idempotent, always replaces rather than accumulates.</summary>
    public Result SetProvisionalTotal(int totalWorkedMinutes)
    {
        if (Status != EqualizationPeriodStatus.Open)
        {
            return Error.Conflict(AttendanceDomainErrors.EqualizationPeriodNotOpen);
        }

        if (totalWorkedMinutes < 0)
        {
            return Error.Validation(AttendanceDomainErrors.EqualizationMinutesInvalid);
        }

        TotalWorkedMinutes = totalWorkedMinutes;
        return Result.Success();
    }

    /// <summary>Dönem kapanışı (yalnız <paramref name="today"/> &gt; PeriodEnd olduğunda): bütçeyi aşan kısım kesin fazla mesai olarak sabitlenir.</summary>
    public Result Close(DateOnly today)
    {
        if (Status != EqualizationPeriodStatus.Open)
        {
            return Error.Conflict(AttendanceDomainErrors.EqualizationPeriodNotOpen);
        }

        if (today <= PeriodEnd)
        {
            return Error.Conflict(AttendanceDomainErrors.EqualizationPeriodNotYetEnded);
        }

        TrueUpOvertimeMinutes = Math.Max(TotalWorkedMinutes - BudgetMinutes, 0);
        Status = EqualizationPeriodStatus.Closed;
        return Result.Success();
    }
}
