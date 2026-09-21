using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.ExitSettlements;

/// <summary>
/// One payment line of an <see cref="ExitSettlement"/>: the automatic kıdem/ihbar/izin lines the exit event produced, or a
/// manual one (ikale bedeli, ek ödeme, çıkış ayı ücreti...). Carries the tax treatment it was netted with and the
/// resulting deductions. Child entity of the aggregate.
/// </summary>
public sealed class ExitSettlementLine : TenantEntity<Guid>
{
    internal ExitSettlementLine(
        Guid id, Guid tenantId, Guid exitSettlementId, ExitPayKind kind, string label, decimal grossAmount,
        ExitPayTaxTreatment treatment, bool isAutomatic)
        : base(id, tenantId)
    {
        ExitSettlementId = exitSettlementId;
        Kind = kind;
        Label = label;
        GrossAmount = grossAmount;
        SgkSubject = treatment.SgkSubject;
        IncomeTaxSubject = treatment.IncomeTaxSubject;
        StampTaxSubject = treatment.StampTaxSubject;
        IsAutomatic = isAutomatic;
    }

    private ExitSettlementLine()
    {
    }

    public Guid ExitSettlementId { get; private set; }

    public ExitPayKind Kind { get; private set; }

    /// <summary>Free-text name of a manual line; empty for automatic ones (the UI names those by <see cref="Kind"/>).</summary>
    public string Label { get; private set; } = string.Empty;

    public decimal GrossAmount { get; private set; }

    public bool SgkSubject { get; private set; }

    public bool IncomeTaxSubject { get; private set; }

    public bool StampTaxSubject { get; private set; }

    /// <summary>Produced by the exit event (kıdem/ihbar/izin) rather than entered by hand; such lines can be edited but not removed.</summary>
    public bool IsAutomatic { get; private set; }

    public decimal SgkAmount { get; private set; }

    public decimal IncomeTaxAmount { get; private set; }

    public decimal StampTaxAmount { get; private set; }

    public decimal NetAmount { get; private set; }

    public ExitPayTaxTreatment Treatment => new(SgkSubject, IncomeTaxSubject, StampTaxSubject);

    internal void Update(string label, decimal grossAmount, ExitPayTaxTreatment treatment)
    {
        Label = label;
        GrossAmount = grossAmount;
        SgkSubject = treatment.SgkSubject;
        IncomeTaxSubject = treatment.IncomeTaxSubject;
        StampTaxSubject = treatment.StampTaxSubject;
    }

    internal void ApplyNet(ExitPayLineResult result)
    {
        SgkAmount = result.Sgk;
        IncomeTaxAmount = result.IncomeTax;
        StampTaxAmount = result.StampTax;
        NetAmount = result.Net;
    }
}
