using System.Text.Json.Serialization;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.PayItems.Formulas;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.PayItems;

/// <summary>Fixed, plausible example values the formula editor shows and <c>validate-formula</c> evaluates with (no tenant data involved).</summary>
public static class PayItemSampleValues
{
    public static readonly IReadOnlyDictionary<string, decimal> Defaults = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        [PayItemVariableNames.Gross] = 30000m,
        [PayItemVariableNames.MonthlyWage] = 30000m,
        [PayItemVariableNames.NetTarget] = 25000m,
        [PayItemVariableNames.DailyWage] = 1000m,
        [PayItemVariableNames.HourlyWage] = 133.33m,
        [PayItemVariableNames.WorkedDays] = 22m,
        [PayItemVariableNames.PaidDays] = 30m,
        [PayItemVariableNames.CalendarDays] = 30m,
        [PayItemVariableNames.MissingDays] = 2m,
        [PayItemVariableNames.SeniorityYears] = 3m,
        [PayItemVariableNames.MinimumWage] = 33030m,
        [PayItemVariableNames.OvertimeHours] = 10m,
        [PayItemVariableNames.Month] = 6m,
        [PayItemVariableNames.Value] = 1000m,
    };
}

public sealed record FormulaVariableDto(string Name, decimal SampleValue);

public sealed record FormulaFunctionDto(string Name, IReadOnlyList<string> Aliases, int MinArgs, int? MaxArgs);

public sealed record FormulaCatalogDto(IReadOnlyList<FormulaVariableDto> Variables, IReadOnlyList<FormulaFunctionDto> Functions);

/// <summary>Variables and functions a formula may use, for the editor's autocomplete.</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record GetFormulaCatalogQuery : IQuery<FormulaCatalogDto>;

public sealed class GetFormulaCatalogHandler : IQueryHandler<GetFormulaCatalogQuery, FormulaCatalogDto>
{
    public Task<Result<FormulaCatalogDto>> Handle(GetFormulaCatalogQuery query, CancellationToken cancellationToken)
    {
        var variables = PayItemVariableNames.All.Select(name => new FormulaVariableDto(name, PayItemSampleValues.Defaults[name])).ToList();
        var functions = PayrollFormula.Functions.Select(f => new FormulaFunctionDto(f.Name, f.Aliases, f.MinArgs, f.MaxArgs)).ToList();
        return Task.FromResult(Result.Success(new FormulaCatalogDto(variables, functions)));
    }
}

/// <summary>
/// Outcome of validating a formula text. Always a normal result (HTTP 200): a bad formula is <c>IsValid == false</c>
/// with the error code and 1-based position. <see cref="Error"/> is for the API layer to localize <c>ErrorMessage</c>
/// and is never serialized.
/// </summary>
public sealed record FormulaValidationDto(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage,
    int? Position,
    IReadOnlyList<string> UsedVariables,
    decimal? SampleResult)
{
    [JsonIgnore]
    public Error? Error { get; init; }
}

[RequiresPermission("payroll.payitem.manage")]
public sealed record ValidatePayItemFormulaQuery(string? Formula, PayItemAmountBasis? AmountBasis, IReadOnlyDictionary<string, decimal>? SampleValues)
    : IQuery<FormulaValidationDto>;

public sealed class ValidatePayItemFormulaHandler : IQueryHandler<ValidatePayItemFormulaQuery, FormulaValidationDto>
{
    public Task<Result<FormulaValidationDto>> Handle(ValidatePayItemFormulaQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(Validate(query)));

    private static FormulaValidationDto Validate(ValidatePayItemFormulaQuery query)
    {
        var parsed = PayrollFormula.Parse(query.Formula);
        if (parsed.IsFailure)
        {
            return Invalid(parsed.Error, []);
        }

        var evaluated = parsed.Value.Evaluate(MergeSamples(query.SampleValues));
        return evaluated.IsFailure
            ? Invalid(evaluated.Error, parsed.Value.UsedVariables)
            : new FormulaValidationDto(true, null, null, null, parsed.Value.UsedVariables, evaluated.Value);
    }

    private static Dictionary<string, decimal> MergeSamples(IReadOnlyDictionary<string, decimal>? overrides)
    {
        var values = new Dictionary<string, decimal>(PayItemSampleValues.Defaults, StringComparer.Ordinal);
        foreach (var (name, value) in overrides ?? new Dictionary<string, decimal>())
        {
            var key = name.ToLowerInvariant();
            if (PayItemVariableNames.IsAllowed(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static FormulaValidationDto Invalid(Error error, IReadOnlyList<string> usedVariables)
    {
        int? position = error.Args is not null && error.Args.TryGetValue("position", out var p) && p is int i ? i : null;
        return new FormulaValidationDto(false, error.Code, null, position, usedVariables, null) { Error = error };
    }
}
