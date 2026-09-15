using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>Why an employment period started ("Çalışma dönemleri", 11.3.03 E5a).</summary>
public enum EmploymentPeriodSource
{
    /// <summary>First period of the employee (its hire date).</summary>
    Hire = 0,

    /// <summary>Opened by <see cref="Employee.Rehire"/> after a termination ("Yeniden işe al").</summary>
    Rehire = 1,

    /// <summary>Grup içi nakil / işyeri devri: recorded by E5b, no write path yet.</summary>
    GroupTransfer = 2,
    BusinessTransfer = 3,
    Import = 4,
}

/// <summary>
/// Which seniority a calculation is for; the periods that count differ per purpose (11.3.03 "Kıdem birleştirme").
/// </summary>
public enum SeniorityPurpose
{
    /// <summary>
    /// İzin kıdemi: previous periods count even when severance was paid, except those settled at exit
    /// (<see cref="EmploymentPeriod.LeaveSettledAtExit"/>) - annual leave used up or paid out.
    /// </summary>
    Leave = 0,

    /// <summary>Kıdem tazminatı kıdemi: periods whose severance was already paid (<see cref="EmploymentPeriod.SeverancePaid"/>) are excluded.</summary>
    Severance = 1,

    /// <summary>İş güvencesi kıdemi (4857 md.18): every period counts, including suspensions and probation.</summary>
    JobSecurity = 2,
}

/// <summary>
/// One continuous employment period of an employee ("EmploymentPeriod", 11.3.03 E5a): open while the employee works,
/// closed on termination with the settlement flags the exit wizard collected. Child entity of the <see cref="Employee"/>
/// aggregate, loaded only by the use cases that need it (like salary/bank records, unlike career history).
/// </summary>
/// <remarks>
/// The spec's CompanyId and TransferSalarySnapshot belong to group employment / işyeri devri (E5b) and are deliberately
/// not stored yet - <see cref="EmploymentPeriodSource.GroupTransfer"/>/<see cref="EmploymentPeriodSource.BusinessTransfer"/>
/// have no write path this package.
/// </remarks>
public sealed class EmploymentPeriod : TenantEntity<Guid>
{
    internal EmploymentPeriod(Guid id, Guid tenantId, Guid employeeId, DateOnly startDate, EmploymentPeriodSource source)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        StartDate = startDate;
        Source = source;
    }

    private EmploymentPeriod()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Null while the period is open; the termination date once closed.</summary>
    public DateOnly? EndDate { get; private set; }

    public EmploymentPeriodSource Source { get; private set; }

    /// <summary>The TerminationReason this period ended with (same-module, FK-less like Employee.TerminationReasonId).</summary>
    public Guid? TerminationReasonId { get; private set; }

    /// <summary>Kıdem tazminatı ödendi: the period is excluded from <see cref="SeniorityPurpose.Severance"/>.</summary>
    public bool SeverancePaid { get; private set; }

    /// <summary>
    /// Yıllık izinleri kullandırıldı veya çıkışta ücreti ödendi ("tasfiye edilmiş"): the period is excluded from
    /// <see cref="SeniorityPurpose.Leave"/> (11.3.5 eki C2).
    /// </summary>
    public bool LeaveSettledAtExit { get; private set; }

    public bool IsOpen => EndDate is null;

    internal void Close(DateOnly endDate, Guid? terminationReasonId, bool severancePaid, bool leaveSettledAtExit)
    {
        EndDate = endDate;
        TerminationReasonId = terminationReasonId;
        SeverancePaid = severancePaid;
        LeaveSettledAtExit = leaveSettledAtExit;
    }

    /// <summary>Calendar days worked in this period, both ends inclusive; an open period is measured up to <paramref name="asOf"/>.</summary>
    public int CountedDays(DateOnly asOf)
    {
        var end = EndDate ?? asOf;
        return end < StartDate ? 0 : end.DayNumber - StartDate.DayNumber + 1;
    }

    /// <summary>Whether this period counts towards <paramref name="purpose"/> (see <see cref="SeniorityPurpose"/>).</summary>
    public bool CountsFor(SeniorityPurpose purpose) => purpose switch
    {
        SeniorityPurpose.Leave => !LeaveSettledAtExit,
        SeniorityPurpose.Severance => !SeverancePaid,
        _ => true,
    };
}
