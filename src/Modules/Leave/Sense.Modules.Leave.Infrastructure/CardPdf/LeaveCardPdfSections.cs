using System.Globalization;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Leave.Infrastructure.CardPdf;

/// <summary>
/// Card PDF "İzinler": current balances (always current) + leave requests within the period.
/// Leave queries take no date filter (at most <see cref="PagingDefaults.MaxPageSize"/> rows, filtered in memory); if
/// the employee has more requests than that the PDF only shows the newest <see cref="PagingDefaults.MaxPageSize"/>.
/// </summary>
public sealed class LeaveCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Leave;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Leave;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var balanceResult = request.IsOwnCard
            ? await dispatcher.Query(new GetMyLeaveBalancesQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeLeaveBalancesQuery(request.EmployeeId), cancellationToken);
        var balances = EmployeeCardPdfSectionResult.Part(balanceResult, dto => Table(
            "İzin bakiyeleri",
            ["İzin türü", "Dönem", "Hak edilen", "Kullanılan", "Bekleyen", "Kalan"],
            dto.Balances.Select(b => (IReadOnlyList<string>)
            [
                b.LeaveTypeName,
                $"{Date(b.Balance.CurrentPeriodFrom)} – {Date(b.Balance.CurrentPeriodTo)}",
                Amount(b.Balance.TotalAccrued),
                Amount(b.Balance.TotalUsed),
                Amount(b.PendingDays),
                Amount(b.Balance.AvailableBalance),
            ]),
            [2, 3, 4, 5]));

        var requestResult = request.IsOwnCard
            ? await dispatcher.Query(new ListMyLeaveRequestsQuery(PagingDefaults.MaxPageSize), cancellationToken)
            : await dispatcher.Query(new ListEmployeeLeaveRequestsQuery(request.EmployeeId, PagingDefaults.MaxPageSize), cancellationToken);
        var requests = EmployeeCardPdfSectionResult.Part(requestResult, rows => Table(
            "İzin talepleri",
            ["İzin türü", "Başlangıç", "Bitiş", "Gün", "Durum", "Açıklama"],
            rows.Where(r => InPeriod(r.StartDate, r.EndDate, request.From, request.To))
                .OrderByDescending(r => r.StartDate)
                .Select(r => (IReadOnlyList<string>)
                [
                    TypeName(r), Date(r.StartDate), Date(r.EndDate), Amount(r.CalculatedDays),
                    localizer.EnumText<LeaveRequestStatus>(r.Status) ?? Empty, Text(r.Description),
                ]),
            [3]));

        return EmployeeCardPdfSectionResult.Combine(balances, requests);
    }

    private string TypeName(LeaveRequestDto request) =>
        string.IsNullOrWhiteSpace(request.LeaveTypeName) ? localizer.EnumText<LeaveTypeCode>(request.LeaveTypeCode) ?? Empty : request.LeaveTypeName;
}

/// <summary>
/// Card PDF "Fazla Mesai": this year's legal-limit usage + overtime requests within the period.
/// Overtime requests take no date filter (at most <see cref="PagingDefaults.MaxPageSize"/> rows, filtered in
/// memory); if the employee has more requests than that the PDF only shows the newest ones.
/// </summary>
public sealed class OvertimeCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Overtime;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Leave;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var usageResult = request.IsOwnCard
            ? await dispatcher.Query(new GetMyAnnualOvertimeUsageQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeAnnualOvertimeUsageQuery(request.EmployeeId), cancellationToken);
        var usage = EmployeeCardPdfSectionResult.Part(usageResult, u =>
        [
            KeyValues(
                $"{u.Year} yılı kullanım",
                ("Kullanılan (saat)", Amount(u.UsedMinutes / 60m)),
                ("Yasal sınır (saat)", Amount(u.LimitMinutes / 60m)),
                ("Sınır aşıldı", YesNo(u.Exceeded))),
        ]);

        var listResult = request.IsOwnCard
            ? await dispatcher.Query(new ListMyOvertimeRequestsQuery(PagingDefaults.MaxPageSize), cancellationToken)
            : await dispatcher.Query(new ListEmployeeOvertimeRequestsQuery(request.EmployeeId, PagingDefaults.MaxPageSize), cancellationToken);
        var list = EmployeeCardPdfSectionResult.Part(listResult, rows => Table(
            "Fazla mesai kayıtları",
            ["Tarih", "Başlangıç", "Süre (saat)", "Gün türü", "Durum", "Açıklama"],
            rows.Where(r => InPeriod(r.WorkDate, null, request.From, request.To))
                .OrderByDescending(r => r.WorkDate)
                .Select(r => (IReadOnlyList<string>)
                [
                    Date(r.WorkDate), r.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture), Amount(r.DurationMinutes / 60m),
                    localizer.EnumText<OvertimeDayKind>(r.DayKind) ?? Empty, localizer.EnumText<OvertimeRequestStatus>(r.Status) ?? Empty, Text(r.Description),
                ]),
            [2]));

        return EmployeeCardPdfSectionResult.Combine(usage, list);
    }
}
