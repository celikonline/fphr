using System.Text.RegularExpressions;
using Sense.Modules.Payroll.Domain.PayItems.Formulas;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems;

/// <summary>Editable content of a <see cref="PayItemDefinition"/> (everything except the immutable <c>Code</c>).</summary>
public sealed record PayItemDefinitionData(
    string Name,
    PayItemDirection Direction,
    PayItemAmountBasis AmountBasis,
    bool IsSgkSubject,
    bool IsIncomeTaxSubject,
    bool IsStampTaxSubject,
    string Formula,
    decimal? DefaultValue,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

/// <summary>
/// A tenant-wide "ödeme türü" whose amount is computed from a formula (PAY-FORMULA-PLAN "PayItemDefinition"). The
/// code is unique per tenant (Application layer + unique index) and never changes. A fixed amount is also a formula
/// (<c>deger</c>). Validation: code pattern, name length, earning only, net items must be fully taxable, formula must
/// parse (PAY-FORMULA-1), date range.
/// </summary>
public sealed partial class PayItemDefinition : TenantAggregateRoot<Guid>
{
    public const int NameMaxLength = 100;
    public const int CodeMaxLength = 32;

    private PayItemDefinition()
    {
    }

    private PayItemDefinition(Guid tenantId, string code, PayItemDefinitionData data)
        : base(Guid.NewGuid(), tenantId)
    {
        Code = code;
        IsActive = true;
        Apply(data);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public PayItemDirection Direction { get; private set; }

    public PayItemAmountBasis AmountBasis { get; private set; }

    public bool IsSgkSubject { get; private set; }

    public bool IsIncomeTaxSubject { get; private set; }

    public bool IsStampTaxSubject { get; private set; }

    /// <summary>The user's formula text, stored as typed (it is echoed into the calculation trace).</summary>
    public string Formula { get; private set; } = string.Empty;

    public decimal? DefaultValue { get; private set; }

    public bool IsActive { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    /// <summary>Whether the formula references the per-assignment parameter <c>deger</c>.</summary>
    public bool UsesValue => ParsedFormula().UsedVariables.Contains(PayItemVariableNames.Value);

    public static Result<PayItemDefinition> Create(Guid tenantId, string code, PayItemDefinitionData data)
    {
        Guard.NotDefault(tenantId);
        var normalizedCode = (code ?? string.Empty).Trim();
        if (!CodePattern().IsMatch(normalizedCode))
        {
            return Error.Validation(PayrollDomainErrors.PayItemCodeInvalid);
        }

        var validated = Validate(data);
        return validated.IsFailure ? validated.Error : new PayItemDefinition(tenantId, normalizedCode, Normalize(data));
    }

    public Result Update(PayItemDefinitionData data)
    {
        var validated = Validate(data);
        if (validated.IsFailure)
        {
            return validated;
        }

        Apply(Normalize(data));
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Whether the definition is usable (active and date range overlapping) in the month starting <paramref name="monthStart"/>.</summary>
    public bool IsEffectiveInMonth(DateOnly monthStart)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return IsActive && ValidFrom <= monthEnd && (ValidTo is null || ValidTo >= monthStart);
    }

    private PayrollFormula ParsedFormula() => PayrollFormula.Parse(Formula).Value;

    private static PayItemDefinitionData Normalize(PayItemDefinitionData data) => data with { Name = data.Name.Trim() };

    private static Result Validate(PayItemDefinitionData data)
    {
        var name = (data.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > NameMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.PayItemNameInvalid);
        }

        if (data.Direction != PayItemDirection.Earning)
        {
            return Error.Validation(PayrollDomainErrors.PayItemDirectionNotSupported);
        }

        if (data.AmountBasis == PayItemAmountBasis.Net && !(data.IsSgkSubject && data.IsIncomeTaxSubject && data.IsStampTaxSubject))
        {
            return Error.Validation(PayrollDomainErrors.PayItemNetRequiresFullyTaxable);
        }

        if (data.ValidTo is { } to && to < data.ValidFrom)
        {
            return Error.Validation(PayrollDomainErrors.PayItemDateRangeInvalid);
        }

        var parsed = PayrollFormula.Parse(data.Formula);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        if (data.DefaultValue is { } value && Math.Abs(value) > FormulaLimits.MaxMagnitude)
        {
            return Error.Validation(PayrollDomainErrors.FormulaOverflow, ("position", 1));
        }

        return Result.Success();
    }

    private void Apply(PayItemDefinitionData data)
    {
        Name = data.Name;
        Direction = data.Direction;
        AmountBasis = data.AmountBasis;
        IsSgkSubject = data.IsSgkSubject;
        IsIncomeTaxSubject = data.IsIncomeTaxSubject;
        IsStampTaxSubject = data.IsStampTaxSubject;
        Formula = data.Formula;
        DefaultValue = data.DefaultValue;
        ValidFrom = data.ValidFrom;
        ValidTo = data.ValidTo;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
