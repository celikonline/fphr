using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>
/// Validates a request's parameter sözlüğü against the provider's <see cref="IReportProvider.Parameters"/> before the
/// execution row is created: required filters must be present, present filters must parse for their kind. Providers
/// then read the same values through <see cref="ReportParameters"/> without re-validating. Unknown extra keys are
/// ignored (a provider may accept more than it advertises).
/// </summary>
public static class ReportParameterValidation
{
    public static Result Validate(IReadOnlyList<ReportParameterDescriptor> descriptors, IReadOnlyDictionary<string, string> parameters)
    {
        foreach (var descriptor in descriptors)
        {
            var result = descriptor.Kind switch
            {
                ReportParameterKind.OrgUnits => ValidateGuidList(descriptor, parameters),
                ReportParameterKind.Year => ValidateInt(descriptor, parameters, 1900, 9999),
                ReportParameterKind.Month => ValidateInt(descriptor, parameters, 1, 12),
                ReportParameterKind.DateRange => ValidateDateRange(descriptor, parameters),
                ReportParameterKind.Boolean => ValidateBool(descriptor, parameters),
                ReportParameterKind.Select => ValidateSelect(descriptor, parameters),
                _ => Result.Success(),
            };

            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    private static Result ValidateGuidList(ReportParameterDescriptor descriptor, IReadOnlyDictionary<string, string> parameters)
    {
        if (!ReportParameters.TryGetGuidList(parameters, descriptor.Key, out var ids))
        {
            return Error.Validation(ReportingErrors.ParameterInvalid);
        }

        return descriptor.IsRequired && ids.Count == 0 ? Error.Validation(ReportingErrors.ParameterRequired) : Result.Success();
    }

    private static Result ValidateInt(ReportParameterDescriptor descriptor, IReadOnlyDictionary<string, string> parameters, int min, int max)
    {
        if (!ReportParameters.TryGetInt(parameters, descriptor.Key, out var value))
        {
            return Error.Validation(ReportingErrors.ParameterInvalid);
        }

        if (value is null)
        {
            return descriptor.IsRequired ? Error.Validation(ReportingErrors.ParameterRequired) : Result.Success();
        }

        return value < min || value > max ? Error.Validation(ReportingErrors.ParameterInvalid) : Result.Success();
    }

    private static Result ValidateDateRange(ReportParameterDescriptor descriptor, IReadOnlyDictionary<string, string> parameters)
    {
        if (!ReportParameters.TryGetDate(parameters, ReportParameterKeys.From, out var from)
            || !ReportParameters.TryGetDate(parameters, ReportParameterKeys.To, out var to))
        {
            return Error.Validation(ReportingErrors.ParameterInvalid);
        }

        if (from is null && to is null)
        {
            return descriptor.IsRequired ? Error.Validation(ReportingErrors.ParameterRequired) : Result.Success();
        }

        // Half-open input is treated as malformed rather than silently defaulting one end.
        if (from is null || to is null || from > to)
        {
            return Error.Validation(ReportingErrors.ParameterInvalid);
        }

        return Result.Success();
    }

    private static Result ValidateBool(ReportParameterDescriptor descriptor, IReadOnlyDictionary<string, string> parameters)
    {
        if (!ReportParameters.TryGetBool(parameters, descriptor.Key, out var value))
        {
            return Error.Validation(ReportingErrors.ParameterInvalid);
        }

        return descriptor.IsRequired && value is null ? Error.Validation(ReportingErrors.ParameterRequired) : Result.Success();
    }

    private static Result ValidateSelect(ReportParameterDescriptor descriptor, IReadOnlyDictionary<string, string> parameters)
    {
        if (!ReportParameters.Has(parameters, descriptor.Key))
        {
            return descriptor.IsRequired ? Error.Validation(ReportingErrors.ParameterRequired) : Result.Success();
        }

        var value = parameters[descriptor.Key].Trim();
        var allowed = descriptor.Options is null || descriptor.Options.Contains(value, StringComparer.OrdinalIgnoreCase);
        return allowed ? Result.Success() : Error.Validation(ReportingErrors.ParameterInvalid);
    }
}
