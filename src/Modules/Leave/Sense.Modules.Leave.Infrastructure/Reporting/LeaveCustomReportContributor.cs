using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>Leave-owned fields for custom reports; queries only Leave's schema and the shared employee report port.</summary>
public sealed class LeaveCustomReportContributor(
    LeaveDbContext context,
    IEmployeeReportReader employees,
    IOrgUnitLookup orgUnits,
    TimeProvider clock,
    IStringLocalizer<SharedResource> localizer) : ICustomReportFieldContributor
{
    private static readonly IReadOnlyList<CustomReportFieldDescriptor> Fields =
    [
        new("leave.request.type", CustomReportKeys.LeaveRequests, "reports:fields.leave.request.type", CustomReportValueType.Text, CustomReportGrain.Detail, 10, ScopePermission: LeavePermissions.RequestsRead.Key),
        new("leave.request.startDate", CustomReportKeys.LeaveRequests, "reports:fields.leave.request.startDate", CustomReportValueType.Date, CustomReportGrain.Detail, 20, ScopePermission: LeavePermissions.RequestsRead.Key),
        new("leave.request.endDate", CustomReportKeys.LeaveRequests, "reports:fields.leave.request.endDate", CustomReportValueType.Date, CustomReportGrain.Detail, 30, ScopePermission: LeavePermissions.RequestsRead.Key),
        new("leave.request.days", CustomReportKeys.LeaveRequests, "reports:fields.leave.request.days", CustomReportValueType.Number, CustomReportGrain.Detail, 40, ScopePermission: LeavePermissions.RequestsRead.Key),
        new("leave.request.status", CustomReportKeys.LeaveRequests, "reports:fields.leave.request.status", CustomReportValueType.Text, CustomReportGrain.Detail, 50, ScopePermission: LeavePermissions.RequestsRead.Key),
        new("leave.overtime.workDate", CustomReportKeys.LeaveOvertime, "reports:fields.leave.overtime.workDate", CustomReportValueType.Date, CustomReportGrain.Detail, 10, ScopePermission: LeavePermissions.OvertimesRead.Key),
        new("leave.overtime.durationMinutes", CustomReportKeys.LeaveOvertime, "reports:fields.leave.overtime.durationMinutes", CustomReportValueType.Number, CustomReportGrain.Detail, 20, ScopePermission: LeavePermissions.OvertimesRead.Key),
        new("leave.overtime.payableMinutes", CustomReportKeys.LeaveOvertime, "reports:fields.leave.overtime.payableMinutes", CustomReportValueType.Number, CustomReportGrain.Detail, 30, ScopePermission: LeavePermissions.OvertimesRead.Key),
        new("leave.overtime.status", CustomReportKeys.LeaveOvertime, "reports:fields.leave.overtime.status", CustomReportValueType.Text, CustomReportGrain.Detail, 40, ScopePermission: LeavePermissions.OvertimesRead.Key),
        new("leave.overtime.converted", CustomReportKeys.LeaveOvertime, "reports:fields.leave.overtime.converted", CustomReportValueType.Boolean, CustomReportGrain.Detail, 50, ScopePermission: LeavePermissions.OvertimesRead.Key),
        new("leave.balance.current", CustomReportKeys.LeaveBalance, "reports:fields.leave.balance.current", CustomReportValueType.Number, CustomReportGrain.Employee, 10, ScopePermission: LeavePermissions.BalancesRead.Key),
    ];

    public string ModuleKey => "leave";

    public Task<IReadOnlyList<CustomReportFieldDescriptor>> GetFieldsAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Fields);

    public async Task<IReadOnlyList<CustomReportDataRow>> GetRowsAsync(CustomReportDataRequest request, CancellationToken cancellationToken)
    {
        var unitIds = request.OrgUnitIds?.ToArray() ?? [];
        var expandedUnitIds = unitIds.Length == 0 ? null : await orgUnits.GetSelfAndDescendantIdsAsync(request.TenantId, unitIds, cancellationToken);
        var scopedEmployees = expandedUnitIds is { Count: 0 }
            ? []
            : await employees.ListAsync(request.TenantId, request.EmployeeIds?.ToArray(), expandedUnitIds, includeTerminated: true, cancellationToken);
        var employeeIds = scopedEmployees.Select(employee => employee.EmployeeId).ToArray();
        var rows = new List<CustomReportDataRow>();

        if (request.FieldKeys.Any(key => key.StartsWith("leave.request.", StringComparison.Ordinal)) && employeeIds.Length > 0)
        {
            var from = request.From!.Value;
            var to = request.To!.Value;
            var requests = await context.Requests.AsNoTracking()
                .Where(item => item.TenantId == request.TenantId && employeeIds.Contains(item.EmployeeId) && item.StartDate <= to && item.EndDate >= from)
                .OrderBy(item => item.StartDate).ThenBy(item => item.SubmittedAt)
                .ToListAsync(cancellationToken);
            var typeNames = await context.LeaveTypes.AsNoTracking()
                .Where(type => type.TenantId == request.TenantId)
                .Select(type => new { type.Id, type.Name })
                .ToDictionaryAsync(type => type.Id, type => type.Name, cancellationToken);

            foreach (var item in requests)
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                AddIfSelected(values, request.FieldKeys, "leave.request.type", typeNames.GetValueOrDefault(item.LeaveTypeId) ?? item.LeaveTypeCodeSnapshot.ToString());
                AddIfSelected(values, request.FieldKeys, "leave.request.startDate", item.StartDate);
                AddIfSelected(values, request.FieldKeys, "leave.request.endDate", item.EndDate);
                AddIfSelected(values, request.FieldKeys, "leave.request.days", item.CalculatedDays);
                AddIfSelected(values, request.FieldKeys, "leave.request.status", localizer.EnumText<LeaveRequestStatus>(item.Status));
                rows.Add(new(CustomReportKeys.LeaveRequests, item.EmployeeId, item.Id.ToString("D"), null, values));
            }
        }

        if (request.FieldKeys.Any(key => key.StartsWith("leave.overtime.", StringComparison.Ordinal)) && employeeIds.Length > 0)
        {
            var from = request.From!.Value;
            var to = request.To!.Value;
            var requests = await context.OvertimeRequests.AsNoTracking()
                .Where(item => item.TenantId == request.TenantId && employeeIds.Contains(item.EmployeeId) && item.WorkDate >= from && item.WorkDate <= to)
                .OrderBy(item => item.WorkDate).ThenBy(item => item.SubmittedAt)
                .ToListAsync(cancellationToken);
            foreach (var item in requests)
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                AddIfSelected(values, request.FieldKeys, "leave.overtime.workDate", item.WorkDate);
                AddIfSelected(values, request.FieldKeys, "leave.overtime.durationMinutes", item.DurationMinutes);
                AddIfSelected(values, request.FieldKeys, "leave.overtime.payableMinutes", item.PayableMinutes);
                AddIfSelected(values, request.FieldKeys, "leave.overtime.status", localizer.EnumText<OvertimeRequestStatus>(item.Status));
                AddIfSelected(values, request.FieldKeys, "leave.overtime.converted", item.ConversionId is not null);
                rows.Add(new(CustomReportKeys.LeaveOvertime, item.EmployeeId, item.Id.ToString("D"), item.Id, values));
            }
        }

        if (request.FieldKeys.Contains("leave.balance.current") && employeeIds.Length > 0)
        {
            var profiles = await context.Profiles.AsNoTracking()
                .Where(profile => profile.TenantId == request.TenantId && employeeIds.Contains(profile.EmployeeId))
                .ToListAsync(cancellationToken);
            var ruleSets = await context.RuleSets.AsNoTracking().Where(ruleSet => ruleSet.TenantId == request.TenantId).ToListAsync(cancellationToken);
            var annualByRuleSet = ruleSets.ToDictionary(
                ruleSet => ruleSet.Id,
                ruleSet => ruleSet.Types.FirstOrDefault(type => type.Code == LeaveTypeCode.Annual && type.HasBalance && !type.IsDisabled));
            var annualIds = profiles.Select(profile => annualByRuleSet.GetValueOrDefault(profile.RuleSetId)?.Id).Where(id => id is not null).Select(id => id!.Value).ToArray();
            var ledger = annualIds.Length == 0
                ? []
                : await context.LedgerEntries.AsNoTracking()
                    .Where(entry => entry.TenantId == request.TenantId && employeeIds.Contains(entry.EmployeeId) && annualIds.Contains(entry.LeaveTypeId))
                    .ToListAsync(cancellationToken);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            foreach (var profile in profiles)
            {
                var annual = annualByRuleSet.GetValueOrDefault(profile.RuleSetId);
                decimal? balance = annual is null ? null : LeaveBalanceReportProjection.Project(
                    annual, profile.LeaveAccrualStartDate, today,
                    ledger.Where(entry => entry.EmployeeId == profile.EmployeeId && entry.LeaveTypeId == annual.Id).ToArray()).Balance.CurrentBalance;
                rows.Add(new(CustomReportKeys.LeaveBalance, profile.EmployeeId, null, null,
                    new Dictionary<string, object?> { ["leave.balance.current"] = balance }));
            }
        }

        return rows;
    }

    private static void AddIfSelected(Dictionary<string, object?> values, IReadOnlySet<string> selected, string key, object? value)
    {
        if (selected.Contains(key)) values.Add(key, value);
    }
}
