using FluentValidation;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Incentives;

/// <summary>The profile's free-text <c>IncentiveLawCode</c> as a declaration code: digits only, padded on the left to 5 digits ("5510" becomes "05510").</summary>
public static class LegacyIncentiveCodes
{
    public const int DeclarationCodeLength = 5;

    /// <returns>The normalized code, or an empty string when the text holds no digit at all.</returns>
    public static string Normalize(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? string.Empty : digits.PadLeft(DeclarationCodeLength, '0');
    }
}

/// <summary>
/// One distinct profile incentive code in use. <see cref="CalculationChanges"/> is true when converting to the suggested
/// definition would change the calculation (the definition is unapproved or not an SGK rate discount: today's flat
/// 5 points would be lost).
/// </summary>
public sealed record LegacyIncentiveCodeDto(
    string Code,
    string NormalizedCode,
    int EmployeeCount,
    Guid? SuggestedDefinitionId,
    string? SuggestedDefinitionCode,
    bool? SuggestedIsApproved,
    bool CalculationChanges);

/// <summary>Preview of the migration tool: groups the incentive codes of the profiles in effect on <paramref name="AsOf"/> (default today) and matches them to definitions by declaration code.</summary>
[RequiresPermission("payroll.incentive.manage")]
public sealed record ListLegacyIncentiveCodesQuery(DateOnly? AsOf = null) : IQuery<IReadOnlyList<LegacyIncentiveCodeDto>>;

public sealed class ListLegacyIncentiveCodesHandler(
    IEmployeePayrollProfileRepository profiles, IIncentiveDefinitionRepository definitions, TimeProvider clock)
    : IQueryHandler<ListLegacyIncentiveCodesQuery, IReadOnlyList<LegacyIncentiveCodeDto>>
{
    public async Task<Result<IReadOnlyList<LegacyIncentiveCodeDto>>> Handle(ListLegacyIncentiveCodesQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var active = await profiles.ListActiveAsOfAsync(asOf, cancellationToken);
        var candidates = (await definitions.ListAsync(includeInactive: false, cancellationToken))
            .Where(d => d.Category == IncentiveCategory.Sgk && d.DeclarationCode is not null)
            .ToList();

        IReadOnlyList<LegacyIncentiveCodeDto> preview = active
            .Where(p => !string.IsNullOrWhiteSpace(p.IncentiveLawCode))
            .GroupBy(p => p.IncentiveLawCode!.Trim())
            .Select(g =>
            {
                var normalized = LegacyIncentiveCodes.Normalize(g.Key);
                var suggested = Suggest(candidates, normalized);
                return new LegacyIncentiveCodeDto(
                    g.Key, normalized, g.Select(p => p.EmployeeId).Distinct().Count(), suggested?.Id, suggested?.Code, suggested?.IsSmmmApproved,
                    suggested is not null && !KeepsFivePoints(suggested));
            })
            .OrderBy(x => x.NormalizedCode, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToList();
        return Result.Success(preview);
    }

    /// <summary>The definition with the same declaration code; an approved rate discount is preferred, then the lowest code.</summary>
    private static IncentiveDefinition? Suggest(IReadOnlyList<IncentiveDefinition> candidates, string normalizedCode) =>
        normalizedCode.Length == 0
            ? null
            : candidates
                .Where(d => d.DeclarationCode == normalizedCode)
                .OrderByDescending(KeepsFivePoints)
                .ThenBy(d => d.Code, StringComparer.Ordinal)
                .FirstOrDefault();

    private static bool KeepsFivePoints(IncentiveDefinition d) => d.IsSmmmApproved && d.Kind == IncentiveKind.EmployerSgkRateDiscount;
}

public sealed record ConvertLegacyIncentiveCodeResult(IReadOnlyList<Guid> CreatedIds, IReadOnlyList<Guid> SkippedEmployeeIds);

/// <summary>
/// Creates open assignments of one definition for every employee whose profile carries the given code (compared in its
/// normalized form), starting at the given month. The profile code itself is never touched. Closed months are refused;
/// employees who already have an overlapping SGK assignment are skipped and reported.
/// </summary>
[RequiresPermission("payroll.incentive.manage")]
public sealed record ConvertLegacyIncentiveCodeCommand(string Code, Guid IncentiveDefinitionId, int FromYear, int FromMonth, int? ToYear, int? ToMonth)
    : ICommand<ConvertLegacyIncentiveCodeResult>;

public sealed class ConvertLegacyIncentiveCodeValidator : AbstractValidator<ConvertLegacyIncentiveCodeCommand>
{
    public ConvertLegacyIncentiveCodeValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.IncentiveDefinitionId).NotEmpty();
        RuleFor(x => x.FromMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.FromYear).InclusiveBetween(1, 9999);
        RuleFor(x => x).Must(x => PayItemAssignmentInput.IsValidMonthPair(x.ToYear, x.ToMonth)).WithName("toYear/toMonth");
    }
}

public sealed class ConvertLegacyIncentiveCodeHandler(
    ITenantContext tenant,
    IEmployeePayrollProfileRepository profiles,
    IPayrollPeriodRepository periods,
    IIncentiveDefinitionRepository definitions,
    IEmployeeIncentiveAssignmentRepository assignments,
    IncentiveChangeFlagger flagger,
    TimeProvider clock) : ICommandHandler<ConvertLegacyIncentiveCodeCommand, ConvertLegacyIncentiveCodeResult>
{
    public async Task<Result<ConvertLegacyIncentiveCodeResult>> Handle(ConvertLegacyIncentiveCodeCommand command, CancellationToken cancellationToken)
    {
        var normalized = LegacyIncentiveCodes.Normalize(command.Code);
        if (normalized.Length == 0)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveDeclarationCodeInvalid);
        }

        var definition = await definitions.FindByIdAsync(command.IncentiveDefinitionId, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        if (definition.Category != IncentiveCategory.Sgk)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", "kind"));
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var employeeIds = (await profiles.ListActiveAsOfAsync(today, cancellationToken))
            .Where(p => LegacyIncentiveCodes.Normalize(p.IncentiveLawCode) == normalized)
            .Select(p => p.EmployeeId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var from = EmployeeIncentiveAssignment.FirstOfMonth(command.FromYear, command.FromMonth);
        var to = PayItemAssignmentInput.ToMonth(command.ToYear, command.ToMonth);
        var closed = await ClosedMonths.LoadAsync(periods, cancellationToken);
        var existing = await assignments.ListByEmployeesAsync(employeeIds, cancellationToken);
        var byEmployee = existing.ToLookup(a => a.EmployeeId);
        var categoryOf = await IncentiveCategories.LoadAsync(definitions, existing, definition, cancellationToken);

        var created = new List<EmployeeIncentiveAssignment>();
        var skipped = new List<Guid>();
        foreach (var employeeId in employeeIds)
        {
            var assignment = EmployeeIncentiveAssignment.Create(
                tenant.TenantId, employeeId, definition, from, to, null, closed, byEmployee[employeeId].ToList(), categoryOf);
            if (assignment.IsSuccess)
            {
                created.Add(assignment.Value);
            }
            else if (assignment.Error.Code == PayrollDomainErrors.IncentiveAssignmentOverlap)
            {
                skipped.Add(employeeId);
            }
            else
            {
                return assignment.Error;
            }
        }

        foreach (var assignment in created)
        {
            assignments.Add(assignment);
            await flagger.FlagEmployeeAsync(assignment.EmployeeId, assignment.ValidFromMonth, assignment.ValidToMonth, cancellationToken);
        }

        return new ConvertLegacyIncentiveCodeResult(created.Select(a => a.Id).ToList(), skipped);
    }
}
