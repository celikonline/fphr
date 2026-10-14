using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

public sealed record PersonnelNumberRangeDto(Guid Id, string Code, string Name, string StartNumber, string EndNumber, PersonnelNumberAssignmentMode AssignmentMode, string? LastAssignedNumber, bool IsActive);
public sealed record PersonnelNumberRuleDto(Guid Id, Guid CompanyId, string CompanyName, PersonnelNumberOperation Operation, Guid RangeId, string RangeCode, PersonnelNumberAssignmentMode AssignmentMode, bool IsActive, Guid? PersonnelAreaId = null, string? PersonnelAreaName = null);
public sealed record LegacyPersonnelNumberDto(string SourceSystem, string PersonnelNumber);
public sealed record PersonnelNumberAreaDto(Guid Id, string Name);
public sealed record PersonnelNumberCompanyDto(Guid Id, string Name, IReadOnlyList<PersonnelNumberAreaDto> PersonnelAreas);
public sealed record PersonnelNumberAssignmentDto(bool Configured, Guid? CompanyId = null, string? CompanyName = null, Guid? RangeId = null, string? RangeCode = null, PersonnelNumberAssignmentMode? AssignmentMode = null, string? StartNumber = null, string? EndNumber = null, string? NextNumber = null, Guid? PersonnelAreaId = null, string? PersonnelAreaName = null);
public interface IPersonnelNumberService
{
    Task<IReadOnlyList<PersonnelNumberRangeDto>> ListRangesAsync(bool includeInactive, CancellationToken ct);
    Task<Result<PersonnelNumberRangeDto>> CreateRangeAsync(CreatePersonnelNumberRangeCommand command, CancellationToken ct);
    Task<Result<PersonnelNumberRangeDto>> UpdateRangeAsync(UpdatePersonnelNumberRangeCommand command, CancellationToken ct);
    Task<Result> SetRangeActiveAsync(Guid id, bool active, CancellationToken ct);
    Task<IReadOnlyList<PersonnelNumberRuleDto>> ListRulesAsync(CancellationToken ct);
    Task<Result<PersonnelNumberRuleDto>> SaveRuleAsync(Guid? id, Guid? companyId, PersonnelNumberOperation? operation, Guid rangeId, bool active, Guid? personnelAreaId, CancellationToken ct);
    Task<IReadOnlyList<PersonnelNumberCompanyDto>> CompaniesAsync(CancellationToken ct);
    Task<Result<PersonnelNumberAssignmentDto>> PreviewAsync(Guid? orgUnitId, PersonnelNumberOperation operation, CancellationToken ct);
    Task<Result<string?>> AssignAsync(Guid? orgUnitId, PersonnelNumberOperation operation, string? supplied, CancellationToken ct);
    Task<Result> AddLegacyAsync(Guid employeeId, string source, string number, CancellationToken ct);
    Task<Result> CheckLegacyAsync(Guid employeeId, string source, string number, CancellationToken ct);
    Task<IReadOnlyList<LegacyPersonnelNumberDto>> LegacyAsync(Guid employeeId, CancellationToken ct);
    void DiscardImportChanges();
    Task ExecuteImportAsync(Func<CancellationToken, Task> action, CancellationToken ct);
}

[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record ListPersonnelNumberRangesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<PersonnelNumberRangeDto>>;
[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record CreatePersonnelNumberRangeCommand(string Code, string Name, string StartNumber, string EndNumber, PersonnelNumberAssignmentMode AssignmentMode) : ICommand<PersonnelNumberRangeDto>;
[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record UpdatePersonnelNumberRangeCommand(Guid Id, string Name, string EndNumber) : ICommand<PersonnelNumberRangeDto>;
[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record SetPersonnelNumberRangeActiveCommand(Guid Id, bool IsActive) : ICommand;
[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record ListPersonnelNumberRulesQuery : IQuery<IReadOnlyList<PersonnelNumberRuleDto>>;
[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record CreatePersonnelNumberRuleCommand(Guid CompanyId, PersonnelNumberOperation Operation, Guid RangeId, Guid? PersonnelAreaId = null) : ICommand<PersonnelNumberRuleDto>;
[RequiresPermission(EmployeePermissions.PersonnelNumberManageKey)]
public sealed record UpdatePersonnelNumberRuleCommand(Guid Id, Guid RangeId, bool IsActive) : ICommand<PersonnelNumberRuleDto>;
public sealed record PreviewPersonnelNumberQuery(Guid? OrgUnitId, PersonnelNumberOperation Operation = PersonnelNumberOperation.Hire) : IQuery<PersonnelNumberAssignmentDto>;
public sealed record ListPersonnelNumberCompaniesQuery : IQuery<IReadOnlyList<PersonnelNumberCompanyDto>>;

public sealed class PersonnelNumberHandlers(IPersonnelNumberService service, Sense.Shared.Contracts.Context.ICurrentUser user, IPermissionService permissions, IDataScopeService dataScope) :
    IQueryHandler<ListPersonnelNumberRangesQuery, IReadOnlyList<PersonnelNumberRangeDto>>,
    ICommandHandler<CreatePersonnelNumberRangeCommand, PersonnelNumberRangeDto>,
    ICommandHandler<UpdatePersonnelNumberRangeCommand, PersonnelNumberRangeDto>,
    ICommandHandler<SetPersonnelNumberRangeActiveCommand>,
    IQueryHandler<ListPersonnelNumberRulesQuery, IReadOnlyList<PersonnelNumberRuleDto>>,
    ICommandHandler<CreatePersonnelNumberRuleCommand, PersonnelNumberRuleDto>,
    ICommandHandler<UpdatePersonnelNumberRuleCommand, PersonnelNumberRuleDto>,
    IQueryHandler<PreviewPersonnelNumberQuery, PersonnelNumberAssignmentDto>,
    IQueryHandler<ListPersonnelNumberCompaniesQuery, IReadOnlyList<PersonnelNumberCompanyDto>>
{
    private async Task<Result<IReadOnlySet<Guid>?>> AllowedUnitsAsync(CancellationToken ct)
    {
        if (user.UserId is not { } id) return Error.Forbidden(ErrorCodes.Forbidden);
        if (await permissions.HasAsync(id, EmployeePermissions.PersonnelNumberManageKey, ct)) return Result.Success<IReadOnlySet<Guid>?>(null);
        var permitted = new HashSet<Guid>(); var granted = false;
        foreach (var key in new[] { EmployeePermissions.EmployeesCreateKey, EmployeePermissions.EmployeesReadKey, EmployeePermissions.EmployeesUpdateKey })
        {
            if (!await permissions.HasAsync(id, key, ct)) continue;
            granted = true;
            var units = await dataScope.GetAccessibleOrgUnitIdsAsync(key, ct);
            if (units is null) return Result.Success<IReadOnlySet<Guid>?>(null);
            permitted.UnionWith(units);
        }
        return granted ? Result.Success<IReadOnlySet<Guid>?>(permitted) : Error.Forbidden(ErrorCodes.Forbidden);
    }
    public async Task<Result<IReadOnlyList<PersonnelNumberRangeDto>>> Handle(ListPersonnelNumberRangesQuery q, CancellationToken ct) => Result.Success<IReadOnlyList<PersonnelNumberRangeDto>>(await service.ListRangesAsync(q.IncludeInactive, ct));
    public Task<Result<PersonnelNumberRangeDto>> Handle(CreatePersonnelNumberRangeCommand c, CancellationToken ct) => service.CreateRangeAsync(c, ct);
    public Task<Result<PersonnelNumberRangeDto>> Handle(UpdatePersonnelNumberRangeCommand c, CancellationToken ct) => service.UpdateRangeAsync(c, ct);
    public Task<Result> Handle(SetPersonnelNumberRangeActiveCommand c, CancellationToken ct) => service.SetRangeActiveAsync(c.Id, c.IsActive, ct);
    public async Task<Result<IReadOnlyList<PersonnelNumberRuleDto>>> Handle(ListPersonnelNumberRulesQuery q, CancellationToken ct) => Result.Success<IReadOnlyList<PersonnelNumberRuleDto>>(await service.ListRulesAsync(ct));
    public Task<Result<PersonnelNumberRuleDto>> Handle(CreatePersonnelNumberRuleCommand c, CancellationToken ct) => service.SaveRuleAsync(null, c.CompanyId, c.Operation, c.RangeId, true, c.PersonnelAreaId, ct);
    public Task<Result<PersonnelNumberRuleDto>> Handle(UpdatePersonnelNumberRuleCommand c, CancellationToken ct) => service.SaveRuleAsync(c.Id, null, null, c.RangeId, c.IsActive, null, ct);
    public async Task<Result<PersonnelNumberAssignmentDto>> Handle(PreviewPersonnelNumberQuery q, CancellationToken ct)
    {
        var allowed = await AllowedUnitsAsync(ct);
        if (allowed.IsFailure) return allowed.Error;
        if (allowed.Value is { } ids && (q.OrgUnitId is not { } unitId || !ids.Contains(unitId))) return Error.Forbidden(ErrorCodes.Forbidden);
        return await service.PreviewAsync(q.OrgUnitId, q.Operation, ct);
    }
    public async Task<Result<IReadOnlyList<PersonnelNumberCompanyDto>>> Handle(ListPersonnelNumberCompaniesQuery q, CancellationToken ct)
    {
        var allowed = await AllowedUnitsAsync(ct);
        if (allowed.IsFailure) return allowed.Error;
        var companies = await service.CompaniesAsync(ct);
        if (allowed.Value is not { } ids) return Result.Success(companies);
        return Result.Success<IReadOnlyList<PersonnelNumberCompanyDto>>(companies.Where(c => ids.Contains(c.Id) || c.PersonnelAreas.Any(a => ids.Contains(a.Id)))
            .Select(c => c with { PersonnelAreas = c.PersonnelAreas.Where(a => ids.Contains(a.Id)).ToList() }).ToList());
    }
}
