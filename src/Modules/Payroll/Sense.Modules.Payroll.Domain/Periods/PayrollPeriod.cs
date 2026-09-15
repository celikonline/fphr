using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Periods;

/// <summary>
/// One tenant-month of payroll (11.3.7 "Dönemler"). This slice only creates/reads Open periods; Approved/Paid/Locked
/// transitions, the "Değişenler" dirty-flag workflow and Submit/Lock actions are P3+.
/// </summary>
public sealed class PayrollPeriod : TenantAggregateRoot<Guid>
{
    private PayrollPeriod()
    {
    }

    private PayrollPeriod(Guid id, Guid tenantId, int year, int month)
        : base(id, tenantId)
    {
        Year = year;
        Month = month;
        Status = PayrollPeriodStatus.Open;
    }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public PayrollPeriodStatus Status { get; private set; }

    public static Result<PayrollPeriod> Create(Guid tenantId, int year, int month)
    {
        Guard.NotDefault(tenantId);
        if (month is < 1 or > 12)
        {
            return Error.Validation(PayrollDomainErrors.PeriodMonthInvalid);
        }

        return new PayrollPeriod(Guid.NewGuid(), tenantId, year, month);
    }
}
