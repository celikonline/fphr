using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.ExitSettlements;

/// <summary>
/// "Çıkış hesabı": the kıdem/ihbar/kullanılmamış izin ücreti amounts frozen at the moment an employee was terminated,
/// with the step trace that produced them. Unlike the wizard's read-only estimate this is a record - later salary,
/// legislation or leave-balance changes never move it. Own aggregate because it carries the cross-module
/// <see cref="EmployeeId"/> (same pattern as <c>GarnishmentOrder</c>).
/// </summary>
public sealed class ExitSettlement : TenantAggregateRoot<Guid>
{
    private readonly List<ExitSettlementLine> _lines = [];

    private ExitSettlement()
    {
    }

    private ExitSettlement(Guid id, Guid tenantId, ExitSettlementData data, DateTimeOffset calculatedAt)
        : base(id, tenantId)
    {
        EmployeeId = data.EmployeeId;
        TerminationDate = data.TerminationDate;
        CauseCategory = data.CauseCategory;
        Currency = data.Currency;
        SeniorityDays = data.SeniorityDays;
        DailyGrossWage = data.DailyGrossWage;
        SeveranceAmount = data.SeveranceAmount;
        NoticeAmount = data.NoticeAmount;
        UnusedLeaveAmount = data.UnusedLeaveAmount;
        TotalAmount = data.TotalAmount;
        StepsJson = data.StepsJson;
        CalculatedAt = calculatedAt;
        AddAutomaticLine(ExitPayKind.Severance, data.SeveranceAmount);
        AddAutomaticLine(ExitPayKind.Notice, data.NoticeAmount);
        AddAutomaticLine(ExitPayKind.UnusedLeave, data.UnusedLeaveAmount);
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly TerminationDate { get; private set; }

    public SeveranceCauseCategory CauseCategory { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public int SeniorityDays { get; private set; }

    public decimal DailyGrossWage { get; private set; }

    public decimal SeveranceAmount { get; private set; }

    public decimal NoticeAmount { get; private set; }

    public decimal UnusedLeaveAmount { get; private set; }

    /// <summary>Gross total of every line (the automatic ones plus whatever was added or edited since).</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Whether the deductions below were computed for the current lines; false until the net calculation ran (e.g. legislation was missing) and after any line change.</summary>
    public bool NetCalculated { get; private set; }

    public decimal SgkDeduction { get; private set; }

    public decimal IncomeTax { get; private set; }

    public decimal StampTax { get; private set; }

    public decimal NetTotal { get; private set; }

    public IReadOnlyList<ExitSettlementLine> Lines => _lines;

    /// <summary>The calculator's trace steps as JSON (audit document, same purpose as a payslip's calculation trace).</summary>
    public string StepsJson { get; private set; } = "[]";

    public DateTimeOffset CalculatedAt { get; private set; }

    public static ExitSettlement Create(Guid tenantId, ExitSettlementData data, DateTimeOffset calculatedAt)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(data.EmployeeId);
        Guard.Against(data.TotalAmount < 0m, "TotalAmount must not be negative.");
        return new ExitSettlement(Guid.NewGuid(), tenantId, data, calculatedAt);
    }

    /// <summary>Adds a manual line (ikale bedeli, ek ödeme, çıkış ayı ücreti...). The net figures are stale until recalculated.</summary>
    public Result<ExitSettlementLine> AddLine(ExitPayKind kind, string label, decimal grossAmount, ExitPayTaxTreatment treatment)
    {
        if (!IsValidLine(label, grossAmount))
        {
            return Error.Validation(PayrollDomainErrors.ExitSettlementLineInvalid);
        }

        var line = new ExitSettlementLine(Guid.NewGuid(), TenantId, Id, kind, label.Trim(), grossAmount, treatment, isAutomatic: false);
        _lines.Add(line);
        MarkStale();
        return line;
    }

    /// <summary>Edits a line's name, amount and tax treatment. An automatic line keeps an empty name (the UI names it by kind) but may be overridden, e.g. an ihbar computed on giydirilmiş ücret.</summary>
    public Result UpdateLine(Guid lineId, string label, decimal grossAmount, ExitPayTaxTreatment treatment)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Error.NotFound(PayrollDomainErrors.ExitSettlementLineNotFound);
        }

        var name = label?.Trim() ?? string.Empty;
        if (grossAmount <= 0m || name.Length > PayrollLimits.ExitLineLabelMaxLength || (!line.IsAutomatic && name.Length == 0))
        {
            return Error.Validation(PayrollDomainErrors.ExitSettlementLineInvalid);
        }

        line.Update(name, grossAmount, treatment);
        MarkStale();
        return Result.Success();
    }

    /// <summary>Removes a manual line; the automatic kıdem/ihbar/izin lines can only be edited.</summary>
    public Result RemoveLine(Guid lineId)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Error.NotFound(PayrollDomainErrors.ExitSettlementLineNotFound);
        }

        if (line.IsAutomatic)
        {
            return Error.Validation(PayrollDomainErrors.ExitSettlementLineNotRemovable);
        }

        _lines.Remove(line);
        MarkStale();
        return Result.Success();
    }

    /// <summary>The lines as calculator input (key = line id).</summary>
    public IReadOnlyList<ExitPayInput> PayInputs() => _lines.Select(l => new ExitPayInput(l.Id, l.GrossAmount, l.Treatment)).ToList();

    /// <summary>Stores the net calculation of the current lines (per line and in total).</summary>
    public void ApplyNet(ExitNetResult result)
    {
        foreach (var lineResult in result.Lines)
        {
            _lines.First(l => l.Id == lineResult.Key).ApplyNet(lineResult);
        }

        SgkDeduction = result.SgkTotal;
        IncomeTax = result.IncomeTaxTotal;
        StampTax = result.StampTaxTotal;
        NetTotal = result.NetTotal;
        NetCalculated = true;
    }

    private static bool IsValidLine(string label, decimal grossAmount) =>
        !string.IsNullOrWhiteSpace(label) && label.Trim().Length <= PayrollLimits.ExitLineLabelMaxLength && grossAmount > 0m;

    private void AddAutomaticLine(ExitPayKind kind, decimal grossAmount)
    {
        if (grossAmount > 0m)
        {
            _lines.Add(new ExitSettlementLine(Guid.NewGuid(), TenantId, Id, kind, string.Empty, grossAmount, ExitSettlementNetCalculator.DefaultTreatment(kind), isAutomatic: true));
        }
    }

    private void MarkStale()
    {
        TotalAmount = _lines.Sum(l => l.GrossAmount);
        NetCalculated = false;
    }
}

/// <summary>Plain values a settlement is created from (keeps the factory signature short and the entity immutable).</summary>
public sealed record ExitSettlementData(
    Guid EmployeeId,
    DateOnly TerminationDate,
    SeveranceCauseCategory CauseCategory,
    string Currency,
    int SeniorityDays,
    decimal DailyGrossWage,
    decimal SeveranceAmount,
    decimal NoticeAmount,
    decimal UnusedLeaveAmount,
    decimal TotalAmount,
    string StepsJson);
