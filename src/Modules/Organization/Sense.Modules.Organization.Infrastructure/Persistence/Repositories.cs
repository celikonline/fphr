using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Infrastructure.Persistence;

/// <summary>EF Core-backed OrgUnitType repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class OrgUnitTypeRepository(OrganizationDbContext context) : IOrgUnitTypeRepository
{
    public void Add(OrgUnitType type) => context.OrgUnitTypes.Add(type);

    public async Task<Result<OrgUnitType>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await context.OrgUnitTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return type is null ? Error.NotFound(OrganizationDomainErrors.OrgUnitTypeNotFound) : type;
    }

    public async Task<IReadOnlyList<OrgUnitType>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = context.OrgUnitTypes.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query.OrderBy(t => t.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        await context.OrgUnitTypes.AnyAsync(t => t.Code == code, cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        await context.OrgUnitTypes.CountAsync(cancellationToken);
}

/// <summary>EF Core-backed OrgUnit repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class OrgUnitRepository(OrganizationDbContext context) : IOrgUnitRepository
{
    public void Add(OrgUnit unit) => context.OrgUnits.Add(unit);

    public async Task<Result<OrgUnit>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var unit = await context.OrgUnits.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return unit is null ? Error.NotFound(OrganizationDomainErrors.OrgUnitNotFound) : unit;
    }

    public async Task<IReadOnlyList<OrgUnit>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = context.OrgUnits.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(u => u.IsActive);
        }

        return await query.OrderBy(u => u.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrgUnit>> ListByTypeAsync(Guid typeId, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = context.OrgUnits.Where(u => u.TypeId == typeId);
        if (activeOnly)
        {
            query = query.Where(u => u.IsActive);
        }

        return await query.OrderBy(u => u.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasChildrenAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.ParentId == orgUnitId && u.IsActive, cancellationToken);

    public async Task<bool> ExistsActiveAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.Id == orgUnitId && u.IsActive, cancellationToken);
}

/// <summary>EF Core-backed Company repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class CompanyRepository(OrganizationDbContext context) : ICompanyRepository
{
    public void Add(Company company) => context.Companies.Add(company);

    public async Task<Result<Company>> GetByOrgUnitIdAsync(Guid orgUnitId, CancellationToken cancellationToken = default)
    {
        var company = await context.Companies.FirstOrDefaultAsync(c => c.OrgUnitId == orgUnitId, cancellationToken);
        return company is null ? Error.NotFound(OrganizationDomainErrors.CompanyNotFound) : company;
    }

    public async Task<Result<Company>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await context.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return company is null ? Error.NotFound(OrganizationDomainErrors.CompanyNotFound) : company;
    }
}

/// <summary>EF Core-backed OrgUnitApprovalPolicyAssignment repository (stub consumer: future Workflow module).</summary>
public sealed class OrgUnitApprovalPolicyAssignmentRepository(OrganizationDbContext context) : IOrgUnitApprovalPolicyAssignmentRepository
{
    public void Add(OrgUnitApprovalPolicyAssignment assignment) => context.ApprovalPolicyAssignments.Add(assignment);

    public async Task<IReadOnlyList<OrgUnitApprovalPolicyAssignment>> ListByOrgUnitAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.ApprovalPolicyAssignments.Where(a => a.OrgUnitId == orgUnitId).ToListAsync(cancellationToken);
}
