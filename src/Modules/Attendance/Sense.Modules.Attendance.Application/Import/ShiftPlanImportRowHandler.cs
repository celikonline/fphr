using Sense.Modules.Attendance.Application.ShiftTemplates;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Attendance.Application.Import;

/// <summary>
/// Applies "shift-plan" rows. Every row is validated first (employee match, date, shift code, locked period); each row is then written with the same
/// cell writer as template apply (definition times, legal limits, availability), one row failing never blocks the others under
/// SkipInvalidRows. Published shifts are not overwritten (row error). AllOrNothing persists nothing when any row fails.
/// </summary>
public sealed class ShiftPlanImportRowHandler(
    ITenantContext tenant, IEmployeeLookup employeeLookup, IShiftDefinitionRepository definitions, IShiftAssignmentRepository shifts,
    IShiftSettingsRepository settingsRepository, IShiftPlanLockRepository locks, IEmployeeAvailabilityRepository availability, IAttendanceUnitOfWork unitOfWork)
    : IImportRowHandler
{
    public string TemplateCode => ShiftPlanImportColumns.TemplateCode;

    public async Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        var byCode = (await definitions.ListAsync(includeInactive: false, cancellationToken)).ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        var lockCache = new ShiftLockCache(locks);
        var employeeCache = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var results = new Dictionary<int, ImportRowResult>();
        var plans = new List<ShiftPlanRowPlan>();

        foreach (var row in batch.Rows)
        {
            var errors = new List<ImportRowError>();
            var nationalId = row.Get(ShiftPlanImportColumns.NationalId)?.Text?.Trim();
            if (string.IsNullOrEmpty(nationalId))
            {
                errors.Add(new ImportRowError(ShiftPlanImportColumns.NationalId, ImportErrorCodes.RequiredMissing));
            }

            var date = ParseDate(row, errors);
            var (definition, dayType) = ParseShiftCode(row, byCode, errors);
            var employeeId = await ResolveEmployeeAsync(nationalId, employeeCache, errors, cancellationToken);
            if (date is { } day && await lockCache.IsLockedAsync(day, cancellationToken))
            {
                errors.Add(new ImportRowError(ShiftPlanImportColumns.Date, AttendanceDomainErrors.ShiftPeriodLocked));
            }

            if (errors.Count > 0 || employeeId is null || date is null || dayType is null)
            {
                results[row.RowNumber] = new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, nationalId, employeeId, errors);
                continue;
            }

            plans.Add(new ShiftPlanRowPlan(row.RowNumber, nationalId!, employeeId.Value, date.Value, definition, dayType.Value));
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Count > 0)
        {
            foreach (var plan in plans)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, null, []);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        var anyChange = await WritePlansAsync(plans, results, cancellationToken);
        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Values.Any(r => r.Outcome == ImportRowOutcome.Error))
        {
            // Writes are only tracked, never saved: nothing is persisted; successful rows are reported as skipped.
            foreach (var done in results.Values.Where(r => r.Outcome is ImportRowOutcome.Inserted or ImportRowOutcome.Updated).ToList())
            {
                results[done.RowNumber] = done with { Outcome = ImportRowOutcome.Skipped, TargetEntityId = null };
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        if (anyChange)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: true);
    }

    private async Task<bool> WritePlansAsync(
        List<ShiftPlanRowPlan> plans, Dictionary<int, ImportRowResult> results, CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
        {
            return false;
        }

        var settings = await ShiftSettingsProvisioning.EnsureAsync(settingsRepository, tenant, unitOfWork, cancellationToken);
        var writer = new ShiftPlanCellWriter(tenant, shifts, availability, settings);
        var anyChange = false;
        foreach (var plan in plans)
        {
            var existing = await writer.FindAsync(plan.EmployeeId, plan.Date, cancellationToken);
            if (existing is { Status: ShiftAssignmentStatus.Published })
            {
                results[plan.RowNumber] = Failed(plan, ShiftPlanImportColumns.Date, AttendanceDomainErrors.ShiftAlreadyPublished);
                continue;
            }

            var written = await writer.WriteAsync(plan.EmployeeId, plan.Date, plan.Definition, plan.DayType, cancellationToken);
            if (written.IsFailure)
            {
                results[plan.RowNumber] = Failed(plan, ShiftPlanImportColumns.ShiftCode, written.Error.Code);
                continue;
            }

            results[plan.RowNumber] = new ImportRowResult(
                plan.RowNumber, written.Value.Existed ? ImportRowOutcome.Updated : ImportRowOutcome.Inserted, plan.NationalId, written.Value.Assignment.Id, []);
            anyChange = true;
        }

        return anyChange;
    }

    private static ImportRowResult Failed(ShiftPlanRowPlan plan, string column, string code) =>
        new(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, plan.EmployeeId, [new ImportRowError(column, code)]);

    private async Task<Guid?> ResolveEmployeeAsync(string? nationalId, Dictionary<string, Guid?> cache, List<ImportRowError> errors, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(nationalId))
        {
            return null;
        }

        if (!cache.TryGetValue(nationalId, out var employeeId))
        {
            employeeId = await employeeLookup.FindActiveByNationalIdAsync(tenant.TenantId, nationalId, cancellationToken);
            cache[nationalId] = employeeId;
        }

        if (employeeId is null)
        {
            errors.Add(new ImportRowError(ShiftPlanImportColumns.NationalId, ImportErrorCodes.NoMatch));
        }

        return employeeId;
    }

    private static (ShiftDefinition? Definition, ShiftDayType? DayType) ParseShiftCode(ImportRow row, Dictionary<string, ShiftDefinition> byCode, List<ImportRowError> errors)
    {
        var code = row.Get(ShiftPlanImportColumns.ShiftCode)?.Text?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            errors.Add(new ImportRowError(ShiftPlanImportColumns.ShiftCode, ImportErrorCodes.RequiredMissing));
            return (null, null);
        }

        if (ShiftPlanImportColumns.DayTypeOf(code) is { } dayType)
        {
            return (null, dayType);
        }

        if (byCode.TryGetValue(code, out var definition))
        {
            return (definition, ShiftDayType.Work);
        }

        errors.Add(new ImportRowError(ShiftPlanImportColumns.ShiftCode, ImportErrorCodes.LookupNotFound, new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = code }));
        return (null, null);
    }

    private static DateOnly? ParseDate(ImportRow row, List<ImportRowError> errors)
    {
        var cell = row.Get(ShiftPlanImportColumns.Date);
        if (cell is null)
        {
            errors.Add(new ImportRowError(ShiftPlanImportColumns.Date, ImportErrorCodes.RequiredMissing));
            return null;
        }

        if (cell.DateValue is { } dateValue)
        {
            return DateOnly.FromDateTime(dateValue);
        }

        if (DateOnly.TryParse(cell.Text, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ImportRowError(ShiftPlanImportColumns.Date, ImportErrorCodes.InvalidDate));
        return null;
    }

    private static IReadOnlyList<ImportRowResult> Ordered(ImportBatch batch, Dictionary<int, ImportRowResult> results) =>
        batch.Rows.Where(r => results.ContainsKey(r.RowNumber)).Select(r => results[r.RowNumber]).ToList();
}

/// <summary>A valid row, ready to be written as one plan cell.</summary>
internal sealed record ShiftPlanRowPlan(int RowNumber, string NationalId, Guid EmployeeId, DateOnly Date, ShiftDefinition? Definition, ShiftDayType DayType);
