using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application.Ledger;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>
/// senseik-048 "İzinlerim"/"Fazla mesailerim" yanındaki şirket geneli ekran: <see cref="CompanyLeaveLedgerData"/>'yı
/// (Excel sağlayıcısıyla paylaşılan) tek bir OrgUnitId + Ay/Yıl filtresine göre çalıştırıp web tablosu için düz bir
/// "Birim yolu" metnine indirger. Infrastructure'da (Application değil) - LeaveBalanceSummaryReportProvider gibi
/// LeaveDbContext'e doğrudan erişip birden çok çalışanı tek seferde okuyor; AddModuleHandlers Infrastructure
/// derlemesini de taradığı için ayrı bir DI kaydı gerekmiyor.
/// </summary>
internal sealed class GetCompanyLeaveLedgerHandler(
    LeaveDbContext context,
    ITenantContext tenant,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    ILegislationParameterReader legislation,
    TimeProvider clock)
    : IQueryHandler<GetCompanyLeaveLedgerQuery, CompanyLeaveLedgerDto>
{
    public async Task<Result<CompanyLeaveLedgerDto>> Handle(GetCompanyLeaveLedgerQuery query, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(query.Year, query.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var parameters = new Dictionary<string, string> { [ReportParameterKeys.IncludeTerminated] = query.IncludeTerminated ? "true" : "false" };
        if (query.OrgUnitId is { } orgUnitId)
        {
            parameters[ReportParameterKeys.OrgUnitIds] = orgUnitId.ToString();
        }

        var result = await CompanyLeaveLedgerData.BuildAsync(
            context, dataScope, orgUnits, employees, legislation, clock,
            tenant.TenantId, LeavePermissions.CompanyLedgerRead.Key, parameters, monthStart, monthEnd, query.Year, cancellationToken);

        var rows = result.Rows
            .Select(r => new CompanyLeaveLedgerRowDto(
                r.EmployeeId, r.FirstName, r.LastName, OrgPath(result.Hierarchy, r),
                r.IsOnLeaveInPeriod, r.LeaveStartDate, r.LeaveEndDate, r.LeaveTypeName,
                r.AnnualLeaveBalance, r.OvertimeHoursYtd, r.OvertimeLimitExceeded))
            .OrderBy(r => r.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CompanyLeaveLedgerDto(query.Year, query.Month, result.OvertimeLimitHours, rows);
    }

    private static string? OrgPath(OrgUnitHierarchy hierarchy, CompanyLeaveLedgerRow row)
    {
        var values = hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId).Where(v => !string.IsNullOrEmpty(v));
        var path = string.Join(" / ", values);
        return path.Length == 0 ? null : path;
    }
}
