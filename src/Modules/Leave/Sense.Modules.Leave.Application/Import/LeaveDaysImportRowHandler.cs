using System.Globalization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Leave.Application.Import;

/// <summary>
/// Applies "leave.days" rows: each valid row posts one historical/opening <see cref="LeaveRequest"/>
/// (Source=Import, approved without a Workflow step, same as İşlemler → İzin Ekle with "onaysız") plus its
/// Usage ledger entry, the way 11.3.15 describes Kolay İK's import ("tarih aralıklarına duyarlı olmadan aktarım
/// yapar; kullanılan gün sayısı kolonundaki değer alınır"). Unlike employee.master/attendance.pdks there is no
/// natural "update" target - every valid row always inserts a new usage record, so only Upsert is offered and it
/// behaves like an insert-only journal. Every row is validated before anything is touched; AllOrNothing applies
/// nothing when any row fails. Affected employees' balances are recalculated once, in the same save (Faz 1b's
/// "aktarım sonrası RecalculateFrom").
/// </summary>
public sealed class LeaveDaysImportRowHandler(
    ITenantContext tenant,
    IEmployeeLookup employeeLookup,
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger,
    LeaveBalanceService balances,
    ILeaveUnitOfWork unitOfWork,
    TimeProvider clock) : IImportRowHandler
{
    public string TemplateCode => LeaveDaysImportColumns.TemplateCode;

    public async Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        var employeeCache = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var contextCache = new Dictionary<Guid, EmployeeLeaveContextResult?>();
        var results = new Dictionary<int, ImportRowResult>();
        var plans = new List<LeaveDaysImportPlan>();

        foreach (var row in batch.Rows)
        {
            var errors = new List<ImportRowError>();
            var nationalId = row.Get(LeaveDaysImportColumns.NationalId)?.Text?.Trim();
            if (string.IsNullOrEmpty(nationalId))
            {
                errors.Add(new ImportRowError(LeaveDaysImportColumns.NationalId, ImportErrorCodes.RequiredMissing));
            }

            Guid? employeeId = null;
            if (nationalId is not null)
            {
                if (!employeeCache.TryGetValue(nationalId, out employeeId))
                {
                    employeeId = await employeeLookup.FindActiveByNationalIdAsync(tenant.TenantId, nationalId, cancellationToken).ConfigureAwait(false);
                    employeeCache[nationalId] = employeeId;
                }

                if (employeeId is null)
                {
                    errors.Add(new ImportRowError(LeaveDaysImportColumns.NationalId, ImportErrorCodes.NoMatch));
                }
            }

            EmployeeLeaveContextResult? context = null;
            if (employeeId is { } id)
            {
                if (!contextCache.TryGetValue(id, out context))
                {
                    context = await LoadContextAsync(id, cancellationToken).ConfigureAwait(false);
                    contextCache[id] = context;
                }

                if (context is null)
                {
                    errors.Add(new ImportRowError(null, LeaveDaysImportErrorCodes.NoProfile));
                }
            }

            LeaveType? type = null;
            var typeName = row.Get(LeaveDaysImportColumns.LeaveTypeName)?.Text;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                errors.Add(new ImportRowError(LeaveDaysImportColumns.LeaveTypeName, ImportErrorCodes.RequiredMissing));
            }
            else if (context is not null)
            {
                type = context.RuleSet.Types.FirstOrDefault(t => !t.IsDisabled && ImportValueParser.TextEquals(LeaveImportTypeNames.DisplayName(t), typeName));
                if (type is null)
                {
                    errors.Add(new ImportRowError(LeaveDaysImportColumns.LeaveTypeName, ImportErrorCodes.LookupNotFound, new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = typeName }));
                }
                else if (type.Unit != LeaveUnit.FullDay)
                {
                    errors.Add(new ImportRowError(LeaveDaysImportColumns.LeaveTypeName, LeaveDaysImportErrorCodes.UnsupportedUnit));
                }
            }

            var start = ParseDate(row, LeaveDaysImportColumns.StartDate, errors);
            var end = ParseDate(row, LeaveDaysImportColumns.EndDate, errors);
            var days = ParseDays(row, errors);
            var description = row.Get(LeaveDaysImportColumns.Description)?.Text?.Trim();
            if (description is { Length: > LeaveLimits.DescriptionMaxLength })
            {
                errors.Add(new ImportRowError(LeaveDaysImportColumns.Description, ImportErrorCodes.ValueTooLong));
            }

            if (errors.Count > 0 || employeeId is null || context is null || type is null || start is null || end is null || days is null)
            {
                results[row.RowNumber] = new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, nationalId, employeeId, errors);
                continue;
            }

            plans.Add(new LeaveDaysImportPlan(row.RowNumber, nationalId!, employeeId.Value, context, type, start.Value, end.Value, days.Value, description));
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Count > 0)
        {
            foreach (var plan in plans)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, null, []);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var newEntriesByEmployee = new Dictionary<Guid, List<LeaveLedgerEntry>>();
        var contextByEmployee = new Dictionary<Guid, EmployeeLeaveContextResult>();
        foreach (var plan in plans)
        {
            var submitted = LeaveRequest.Submit(
                tenant.TenantId, plan.EmployeeId, plan.Type.Id, plan.Type.Code, plan.Start, plan.End, plan.Days, plan.Description,
                LeaveRequestSource.Import, now, ip: null, userAgent: null);
            if (submitted.IsFailure)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null, [new ImportRowError(null, submitted.Error.Code)]);
                continue;
            }

            var request = submitted.Value;
            var approved = request.ApproveWithoutWorkflow(now);
            if (approved.IsFailure)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null, [new ImportRowError(null, approved.Error.Code)]);
                continue;
            }

            requests.Add(request);
            var entry = LeaveLedgerEntry.Usage(tenant.TenantId, plan.EmployeeId, plan.Type.Id, plan.Days, plan.Start, LeaveLedgerSource.Import, request.Id);
            ledger.AddRange([entry]);
            newEntriesByEmployee.TryAdd(plan.EmployeeId, []);
            newEntriesByEmployee[plan.EmployeeId].Add(entry);
            contextByEmployee[plan.EmployeeId] = plan.Context;
            results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Inserted, plan.NationalId, request.Id, []);
        }

        foreach (var (employeeId, context) in contextByEmployee)
        {
            await balances.RecalculateAsync(context.Profile, context.RuleSet, cancellationToken, newEntriesByEmployee[employeeId]).ConfigureAwait(false);
        }

        if (contextByEmployee.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: true);
    }

    private async Task<EmployeeLeaveContextResult?> LoadContextAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByEmployeeIdAsync(employeeId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        var ruleSet = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken).ConfigureAwait(false);
        return ruleSet.IsFailure ? null : new EmployeeLeaveContextResult(profile, ruleSet.Value);
    }

    private static DateOnly? ParseDate(ImportRow row, string key, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            errors.Add(new ImportRowError(key, ImportErrorCodes.RequiredMissing));
            return null;
        }

        if (ImportValueParser.TryParseDate(cell, out var date))
        {
            return date;
        }

        errors.Add(new ImportRowError(key, ImportErrorCodes.InvalidDate));
        return null;
    }

    private static decimal? ParseDays(ImportRow row, List<ImportRowError> errors)
    {
        var cell = row.Get(LeaveDaysImportColumns.Days);
        if (cell?.Text is not { } text)
        {
            errors.Add(new ImportRowError(LeaveDaysImportColumns.Days, ImportErrorCodes.RequiredMissing));
            return null;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0m)
        {
            return value;
        }

        errors.Add(new ImportRowError(LeaveDaysImportColumns.Days, LeaveDaysImportErrorCodes.InvalidDays, new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = text }));
        return null;
    }

    private static IReadOnlyList<ImportRowResult> Ordered(ImportBatch batch, Dictionary<int, ImportRowResult> results) =>
        batch.Rows.Where(r => results.ContainsKey(r.RowNumber)).Select(r => results[r.RowNumber]).ToList();
}

/// <summary>The employee's leave profile + assigned rule set, loaded once per employee per batch.</summary>
internal sealed record EmployeeLeaveContextResult(EmployeeLeaveProfile Profile, LeaveRuleSet RuleSet);

/// <summary>A valid row, ready to post a <see cref="LeaveRequest"/> + its usage ledger entry.</summary>
internal sealed record LeaveDaysImportPlan(
    int RowNumber, string NationalId, Guid EmployeeId, EmployeeLeaveContextResult Context, LeaveType Type,
    DateOnly Start, DateOnly End, decimal Days, string? Description);
