using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// One employee's payroll line for one <see cref="Periods.PayrollPeriod"/> (11.3.7). This slice only stores the
/// fields the calculation trace needs to reference (gross/net/employer cost + the input snapshot/hash the "Değişti"
/// mechanism will compare against in P3) - not a full aggregate with line items, dirty flags or approval state yet.
/// </summary>
public sealed class Payslip : TenantAggregateRoot<Guid>
{
    private Payslip()
    {
    }

    private Payslip(
        Guid id, Guid tenantId, Guid employeeId, Guid payrollPeriodId, string inputSnapshot, string inputHash,
        int version, decimal grossAmount, decimal netAmount, decimal employerCost)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        PayrollPeriodId = payrollPeriodId;
        InputSnapshot = inputSnapshot;
        InputHash = inputHash;
        Version = version;
        GrossAmount = grossAmount;
        NetAmount = netAmount;
        EmployerCost = employerCost;
    }

    public Guid EmployeeId { get; private set; }

    public Guid PayrollPeriodId { get; private set; }

    /// <summary>JSON snapshot of the inputs the calculation used (11.3.7 "Değişti" mechanism, InputHash compares this later).</summary>
    public string InputSnapshot { get; private set; } = string.Empty;

    public string InputHash { get; private set; } = string.Empty;

    /// <summary>Incremented on each recalculation; <see cref="Payslips.PayslipCalculationTrace.Version"/> mirrors it.</summary>
    public int Version { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal EmployerCost { get; private set; }

    public static Payslip CreateFirstVersion(
        Guid tenantId, Guid employeeId, Guid payrollPeriodId, string inputSnapshot, string inputHash,
        decimal grossAmount, decimal netAmount, decimal employerCost)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        Guard.NotDefault(payrollPeriodId);
        return new Payslip(
            Guid.NewGuid(), tenantId, employeeId, payrollPeriodId, Guard.NotEmpty(inputSnapshot), Guard.NotEmpty(inputHash),
            version: 1, grossAmount, netAmount, employerCost);
    }

    /// <summary>Recompute with new figures (P3 "Değişenleri yeniden hesapla"): bumps <see cref="Version"/>.</summary>
    public void Recalculate(string inputSnapshot, string inputHash, decimal grossAmount, decimal netAmount, decimal employerCost)
    {
        InputSnapshot = Guard.NotEmpty(inputSnapshot);
        InputHash = Guard.NotEmpty(inputHash);
        GrossAmount = grossAmount;
        NetAmount = netAmount;
        EmployerCost = employerCost;
        Version++;
    }
}
