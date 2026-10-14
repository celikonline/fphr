using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

public enum PersonnelNumberAssignmentMode { Internal, External }
public enum PersonnelNumberOperation { Hire, Import }

public sealed class PersonnelNumberRange : TenantAggregateRoot<Guid>, IAuditLogged
{
    private PersonnelNumberRange() { }
    private PersonnelNumberRange(Guid tenantId) : base(Guid.CreateVersion7(), tenantId) { }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string StartNumber { get; private set; } = string.Empty;
    public string EndNumber { get; private set; } = string.Empty;
    public PersonnelNumberAssignmentMode AssignmentMode { get; private set; }
    public string? LastAssignedNumber { get; private set; }
    public bool IsActive { get; private set; } = true;
    public static bool IsNumber(string? value) => value is { Length: 8 } && value.All(c => c >= '0' && c <= '9') && value != "00000000";
    public static Result<PersonnelNumberRange> Create(Guid tenantId, string code, string name, string start, string end, PersonnelNumberAssignmentMode mode)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || !IsNumber(start) || !IsNumber(end) || string.CompareOrdinal(start, end) > 0 || !Enum.IsDefined(mode))
            return Error.Validation("Employee.PersonnelNumber.InvalidRange");
        return new PersonnelNumberRange(tenantId) { Code = code.Trim(), Name = name.Trim(), StartNumber = start, EndNumber = end, AssignmentMode = mode };
    }
    public Result Update(string name, string end)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || !IsNumber(end) || string.CompareOrdinal(end, EndNumber) < 0)
            return Error.Validation("Employee.PersonnelNumber.InvalidRange");
        Name = name.Trim(); EndNumber = end; return Result.Success();
    }
    public bool Contains(string number) => IsNumber(number) && string.CompareOrdinal(number, StartNumber) >= 0 && string.CompareOrdinal(number, EndNumber) <= 0;
    public void SetActive(bool active) => IsActive = active;
    public void RecordAssignment(string number) => LastAssignedNumber = number;
}

public sealed class PersonnelNumberRule : TenantAggregateRoot<Guid>, IAuditLogged
{
    private PersonnelNumberRule() { }
    public PersonnelNumberRule(Guid tenantId, Guid companyId, PersonnelNumberOperation operation, Guid rangeId, Guid? personnelAreaId = null) : base(Guid.CreateVersion7(), tenantId)
    { CompanyId = companyId; Operation = operation; RangeId = rangeId; PersonnelAreaId = personnelAreaId; }
    public Guid? PersonnelAreaId { get; private set; }
    public Guid CompanyId { get; private set; }
    public PersonnelNumberOperation Operation { get; private set; }
    public Guid RangeId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public void Update(Guid rangeId, bool active) { RangeId = rangeId; IsActive = active; }
}

public sealed class LegacyPersonnelNumber : TenantAggregateRoot<Guid>, IAuditLogged
{
    private LegacyPersonnelNumber() { }
    public LegacyPersonnelNumber(Guid tenantId, Guid employeeId, string sourceSystem, string personnelNumber) : base(Guid.CreateVersion7(), tenantId)
    { EmployeeId = employeeId; SourceSystem = sourceSystem; PersonnelNumber = personnelNumber; }
    public Guid EmployeeId { get; private set; }
    public string SourceSystem { get; private set; } = string.Empty;
    public string PersonnelNumber { get; private set; } = string.Empty;
}
