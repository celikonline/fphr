namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>Biological/self-reported sex; Unspecified is the default when not provided.</summary>
public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
    Other = 3,
}

/// <summary>Unspecified is the default when not provided.</summary>
public enum MaritalStatus
{
    Unspecified = 0,
    Single = 1,
    Married = 2,
    Divorced = 3,
}

/// <summary>Whether the employee is currently studying or has graduated; null = not recorded.</summary>
public enum EducationStatus
{
    Student = 0,
    Graduate = 1,
}

/// <summary>Highest completed education level; null = not recorded.</summary>
public enum HighestEducationLevel
{
    Primary = 0,
    MiddleSchool = 1,
    HighSchool = 2,
    Associate = 3,
    Bachelor = 4,
    Master = 5,
    Doctorate = 6,
}

/// <summary>Military service status (typically applies to male employees; not hard-validated); null = not recorded.</summary>
public enum MilitaryStatus
{
    NotCompleted = 0,
    Completed = 1,
    Deferred = 2,
    Exempt = 3,
    DraftEvader = 4,
    Absentee = 5,
}

/// <summary>Disability degree; null = not recorded (distinct from <see cref="None"/>, which is an explicit "no disability").</summary>
public enum DisabilityDegree
{
    None = 0,
    First = 1,
    Second = 2,
    Third = 3,
}

/// <summary>Blood type; null = not recorded.</summary>
public enum BloodType
{
    OPos = 0,
    ONeg = 1,
    APos = 2,
    ANeg = 3,
    BPos = 4,
    BNeg = 5,
    ABPos = 6,
    ABNeg = 7,
}

/// <summary>Employment contract duration; null = not recorded.</summary>
public enum ContractType
{
    Indefinite = 0,
    FixedTerm = 1,
}

/// <summary>
/// Türk vatandaşı mı yabancı uyruklu mu (senseik-009/F-02): Turkish is the default so existing rows (all TCKN'li,
/// hence really Turkish) keep their meaning unchanged when this column lands. Foreign employees need
/// <see cref="Employee.ForeignIdNumber"/> or <see cref="Employee.PassportNumber"/> instead of (or alongside) a
/// TCKN-shaped <see cref="Employee.NationalId"/> - see <see cref="Employee.Validate"/>.
/// </summary>
public enum Nationality
{
    Turkish = 0,
    Foreign = 1,
}

/// <summary>Working-time/engagement type; null = not recorded.</summary>
public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Temporary = 2,
    Contract = 3,
    Freelance = 4,
    Intern = 5,
    Mobile = 6,
    Apprentice = 7,
    Seasonal = 8,
    AgencyWorker = 9,
    Subcontractor = 10,
}
