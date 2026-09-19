using FluentValidation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Leave.Application.Ledger;

/// <summary>
/// senseik-048: "İzinlerim"/"Fazla mesailerim" yanına eklenen, İK Müdürü/Hesap Sahibi için şirket geneli görünüm -
/// "bu ay kimler izinde", "yıllık izin birikimi en yüksek 10 kişi", "bu yıl 270 saati aşan fazla mesai yapan var mı"
/// sorularını tek, filtrelenebilir bir satır kümesiyle cevaplar. Takvim (leave-calendar.tsx) ile tamamlayıcı: o gün
/// bazlı bir takvim görünümü, bu ise çalışan başına bir satır olan bir liste/tablo. <see cref="OrgPath"/> tenant'a
/// özel birim hiyerarşisini (Şirket / Departman / Lokasyon ...) " / " ile ayrılmış tek bir metne indirger - Excel
/// çıktısı (bkz. Infrastructure/Reporting/CompanyLeaveLedgerReportProvider) dinamik sütunlarla tam hiyerarşiyi verir.
/// Balans anlık (AsOf = bugün, LeaveBalanceSummaryReportProvider ile aynı yaklaşım); "bu ay izinde" ve "yıllık fazla
/// mesai" filtreleri Ay/Yıl parametreleriyle değişir.
/// </summary>
public sealed record CompanyLeaveLedgerRowDto(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? OrgPath,
    bool IsOnLeaveInPeriod,
    DateOnly? LeaveStartDate,
    DateOnly? LeaveEndDate,
    string? LeaveTypeName,
    decimal? AnnualLeaveBalance,
    decimal OvertimeHoursYtd,
    bool OvertimeLimitExceeded);

/// <summary><see cref="OvertimeLimitHours"/> yasal (veya tenant'a özel Legislation Parameter) sınırı ekrana taşır; web etiketi buna göre kurar ("270 saati aşanlar").</summary>
public sealed record CompanyLeaveLedgerDto(int Year, int Month, decimal OvertimeLimitHours, IReadOnlyList<CompanyLeaveLedgerRowDto> Rows);

/// <summary>
/// <paramref name="OrgUnitId"/>: seçilen birim + alt birimleri kapsanır (null = tüm şirket). Bakiye her zaman bugüne
/// göre projekte edilir; <paramref name="Year"/>/<paramref name="Month"/> yalnızca "bu ay izinde" penceresini ve
/// fazla mesai yıl toplamını belirler.
/// </summary>
[RequiresPermission("leave.company_ledger.read")]
public sealed record GetCompanyLeaveLedgerQuery(int Year, int Month, Guid? OrgUnitId, bool IncludeTerminated) : IQuery<CompanyLeaveLedgerDto>;

public sealed class GetCompanyLeaveLedgerValidator : AbstractValidator<GetCompanyLeaveLedgerQuery>
{
    public GetCompanyLeaveLedgerValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(1900, 9999);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
