using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Payroll.Application.Incentives;

/// <summary>
/// What the incentive models decided for one employee in one payroll run.
/// </summary>
/// <param name="Effects">Approved, in-force (a)/(b)/(c) effects for the calculation engine (empty = none).</param>
/// <param name="LegacyProfileRule">The old rule applies: no effective SGK assignment and the profile's incentive code is filled (the 5-point discount).</param>
/// <param name="Warnings">Non-blocking issues (<c>incentive_not_approved</c>, <c>incentive_duplicate_assignment</c>, <c>incentive_profile_code_ignored</c>).</param>
/// <param name="MissingParameter">A referenced legislation key that could not be resolved for the period (or resolved to an impossible rate); the employee is skipped.</param>
public sealed record IncentiveResolution(
    IReadOnlyList<IncentiveEffectInput> Effects,
    bool LegacyProfileRule,
    IReadOnlyList<PayrollWarning> Warnings,
    string? MissingParameter = null);

/// <summary>
/// Resolves, for one payroll run, which incentive models apply to each employee (PAY-INCENTIVE-PLAN "Hesaba entegrasyon").
/// The month plan (assignments and definitions) is loaded once; legislation keys are resolved at the period start and cached for the run.
/// Rules: an SGK-category assignment in force replaces the profile's <c>IncentiveLawCode</c> rule; the tax category is independent;
/// only APPROVED definitions reach the calculation; (d) declaration-only never does.
/// </summary>
public sealed class IncentiveCalculation
{
    private readonly IncentiveMonthPlan _plan;
    private readonly ILegislationParameterReader _legislation;
    private readonly DateOnly _asOf;
    private readonly Dictionary<string, decimal?> _legislationValues = [];

    private IncentiveCalculation(IncentiveMonthPlan plan, ILegislationParameterReader legislation, DateOnly asOf)
    {
        _plan = plan;
        _legislation = legislation;
        _asOf = asOf;
    }

    public static async Task<IncentiveCalculation> LoadAsync(
        DateOnly periodStart,
        IEmployeeIncentiveAssignmentRepository assignments,
        IIncentiveDefinitionRepository definitions,
        ILegislationParameterReader legislation,
        CancellationToken cancellationToken = default)
    {
        var plan = await IncentiveMonthPlan.LoadAsync(periodStart, assignments, definitions, cancellationToken);
        return new IncentiveCalculation(plan, legislation, EmployeeIncentiveAssignment.FirstOfMonth(periodStart));
    }

    public async Task<IncentiveResolution> ResolveAsync(Guid employeeId, string? profileLawCode, CancellationToken cancellationToken = default)
    {
        var profileCodeFilled = !string.IsNullOrWhiteSpace(profileLawCode);
        var effective = _plan.EffectiveForEmployee(employeeId);
        if (effective.Count == 0)
        {
            return new IncentiveResolution([], profileCodeFilled, []);
        }

        var warnings = new List<PayrollWarning>();
        var effects = new List<IncentiveEffectInput>();
        var sgk = Select(employeeId, effective, IncentiveCategory.Sgk, warnings);
        var tax = Select(employeeId, effective, IncentiveCategory.Tax, warnings);

        // The assignment replaces the profile's flat 5 points. Warn when the codes differ, or when the assignment itself changes no
        // calculation ((d) declaration-only or unapproved), so the employee silently loses the 5 points.
        var assignmentHasNoEffect = sgk is not null && (sgk.Definition.Kind == IncentiveKind.DeclarationOnly || !sgk.Definition.IsSmmmApproved);
        if (sgk is not null && profileCodeFilled && (assignmentHasNoEffect || !SameDeclarationCode(profileLawCode, sgk.Definition.DeclarationCode)))
        {
            warnings.Add(new PayrollWarning(employeeId, PayrollWarningCodes.IncentiveProfileCodeIgnored, profileLawCode!.Trim()));
        }

        foreach (var chosen in new[] { sgk, tax }.OfType<IncentiveAssignmentInMonth>())
        {
            var definition = chosen.Definition;
            if (definition.Kind == IncentiveKind.DeclarationOnly)
            {
                continue; // (d): only the declaration code is reported, the calculation is untouched.
            }

            if (!definition.IsSmmmApproved)
            {
                warnings.Add(new PayrollWarning(employeeId, PayrollWarningCodes.IncentiveNotApproved, definition.Code));
                continue;
            }

            var (effect, missing) = await BuildEffectAsync(definition, cancellationToken);
            if (effect is null)
            {
                return new IncentiveResolution([], profileCodeFilled, warnings, missing);
            }

            effects.Add(effect);
        }

        return new IncentiveResolution(effects, sgk is null && profileCodeFilled, warnings);
    }

    /// <summary>
    /// The employee's effective assignment of one category: the latest-starting one (ties: the largest id) when several overlap
    /// (concurrent requests); the overlap is reported as a warning.
    /// </summary>
    private static IncentiveAssignmentInMonth? Select(
        Guid employeeId, IReadOnlyList<IncentiveAssignmentInMonth> effective, IncentiveCategory category, List<PayrollWarning> warnings)
    {
        var ofCategory = effective
            .Where(p => p.Definition.Category == category)
            .OrderBy(p => p.Assignment.ValidFromMonth)
            .ThenBy(p => p.Assignment.Id)
            .ToList();
        if (ofCategory.Count == 0)
        {
            return null;
        }

        var chosen = ofCategory[^1];
        if (ofCategory.Count > 1)
        {
            warnings.Add(new PayrollWarning(employeeId, PayrollWarningCodes.IncentiveDuplicateAssignment, chosen.Definition.Code));
        }

        return chosen;
    }

    private static bool SameDeclarationCode(string? profileLawCode, string? declarationCode) =>
        LegacyIncentiveCodes.Normalize(profileLawCode) == LegacyIncentiveCodes.Normalize(declarationCode);

    private async Task<(IncentiveEffectInput? Effect, string? MissingParameter)> BuildEffectAsync(IncentiveDefinition definition, CancellationToken cancellationToken)
    {
        var parameters = definition.Parameters;
        var (rate, missingRate) = await ResolveRateAsync(parameters.Rate, $"Incentive.{definition.Code}.Rate", cancellationToken);
        if (rate is null)
        {
            return (null, missingRate);
        }

        ResolvedRate? employerSgkRate = null;
        if (definition.Kind == IncentiveKind.EmployerShareCoverage && parameters.CoversEmployerSgk)
        {
            var (resolved, missing) = await ResolveRateAsync(parameters.EmployerSgkRate, $"Incentive.{definition.Code}.EmployerSgkRate", cancellationToken);
            if (resolved is null)
            {
                return (null, missing);
            }

            employerSgkRate = resolved;
        }

        var effect = new IncentiveEffectInput(definition.Code, definition.Kind, rate);
        if (definition.Kind != IncentiveKind.EmployerShareCoverage)
        {
            return (effect, null);
        }

        return (effect with
        {
            CoverageBase = parameters.CoverageBase,
            CapMultiplier = parameters.CapMultiplier,
            EmployerSgkRate = employerSgkRate,
            CoversEmployerSgk = parameters.CoversEmployerSgk,
            CoversEmployerUnemployment = parameters.CoversEmployerUnemployment,
            CoversEmployeeSgk = parameters.CoversEmployeeSgk,
            CoversEmployeeUnemployment = parameters.CoversEmployeeUnemployment,
        }, null);
    }

    /// <summary>A literal rate is used as is; a legislation reference is read at the period start (cached for the run). A rate outside (0, 1] is as unusable as a missing one.</summary>
    private async Task<(ResolvedRate? Rate, string? MissingParameter)> ResolveRateAsync(IncentiveRate? rate, string literalKey, CancellationToken cancellationToken)
    {
        if (rate is { Value: { } literal })
        {
            return IsUsable(literal) ? (new ResolvedRate(literal, literalKey), null) : (null, literalKey);
        }

        if (rate is { LegislationKey: { } key })
        {
            if (!_legislationValues.TryGetValue(key, out var value))
            {
                value = await _legislation.GetDecimalAsync(key, _asOf, cancellationToken);
                _legislationValues[key] = value;
            }

            return value is { } resolved && IsUsable(resolved) ? (new ResolvedRate(resolved, key), null) : (null, key);
        }

        return (null, literalKey);
    }

    private static bool IsUsable(decimal rate) => rate is > 0m and <= 1m;
}
