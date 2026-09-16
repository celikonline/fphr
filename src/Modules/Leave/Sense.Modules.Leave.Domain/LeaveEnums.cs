namespace Sense.Modules.Leave.Domain;

/// <summary>Cross-rule-set mapping, reporting and legislation key of a leave type (11.3.5 "LeaveType.Code").</summary>
public enum LeaveTypeCode
{
    Custom = 0,
    Annual = 1,
    Excuse = 2,
    Sick = 3,
    JobSearch = 4,
    Marriage = 5,
    Bereavement = 6,
    Paternity = 7,
    Maternity = 8,
    PostnatalUnpaid = 9,
    PaidMilitaryService = 10,
    Unpaid = 11,
    FosterCare = 12,
    RemoteWork = 13,
    Birthday = 14,
}

public enum LeaveLimitType
{
    Unlimited = 0,
    PerRequest = 1,
    PerYear = 2,
}

public enum NegativeBalanceMode
{
    None = 0,
    Unlimited = 1,
    Limited = 2,
}

public enum AccrualFrequency
{
    Yearly = 0,
    Monthly = 1,
    Daily = 2,
}

public enum AccrualTiming
{
    EndOfPeriod = 0,
    StartOfPeriod = 1,
}

public enum AccrualBasis
{
    HireDate = 0,
    CalendarYear = 1,
}

/// <summary>"Devretme" step: Sıfırlansın / Limitsiz Devretme (negative balances carry too) / Limitli Devretme + Limit.</summary>
public enum CarryOverMode
{
    Reset = 0,
    Unlimited = 1,
    Limited = 2,
}

public enum LeaveUnit
{
    FullDay = 0,
    HalfDay = 1,
    Hourly = 2,
}

/// <summary>Which half of the work day a <see cref="LeaveUnit.HalfDay"/> request covers.</summary>
public enum HalfDayPeriod
{
    Morning = 0,
    Afternoon = 1,
}

public enum SeniorityComparison
{
    GreaterOrEqual = 0,
    GreaterThan = 1,
}

/// <summary>Append-only ledger row kinds (11.3.5 "LeaveLedgerEntryType"; Expiry/StatutoryTopUp/SettlementClose come with later slices).</summary>
public enum LeaveLedgerEntryType
{
    OpeningBalance = 0,
    Accrual = 1,
    CarryOver = 2,
    Usage = 3,
    Adjustment = 4,
    ExtraGrant = 5,

    /// <summary>Restores the days of a cancelled approved leave (positive, dated like the Usage row it offsets).</summary>
    Reversal = 6,
}

public enum LeaveLedgerSource
{
    System = 0,
    Manual = 1,
    Import = 2,
    Request = 3,
    OvertimeConversion = 4,
}
