using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Infrastructure.Persistence;

public sealed class PersonnelNumberService(EmployeeDbContext db, ITenantContext tenant, OrgPlacementLoader placementLoader) : IPersonnelNumberService
{
    private Task LockAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"employee-personnel:" + tenant.TenantId}, 0))", ct);
    private static PersonnelNumberRangeDto Dto(PersonnelNumberRange r) => new(r.Id, r.Code, r.Name, r.StartNumber, r.EndNumber, r.AssignmentMode, r.LastAssignedNumber, r.IsActive);
    public async Task<IReadOnlyList<PersonnelNumberRangeDto>> ListRangesAsync(bool includeInactive, CancellationToken ct) =>
        (await db.Set<PersonnelNumberRange>().AsNoTracking().Where(r => includeInactive || r.IsActive).OrderBy(r => r.Code).ToListAsync(ct)).Select(Dto).ToList();
    public async Task<Result<PersonnelNumberRangeDto>> CreateRangeAsync(CreatePersonnelNumberRangeCommand c, CancellationToken ct)
    {
        var result = PersonnelNumberRange.Create(tenant.TenantId, c.Code, c.Name, c.StartNumber, c.EndNumber, c.AssignmentMode);
        if (result.IsFailure) return result.Error;
        await LockAsync(ct);
        if (await db.Set<PersonnelNumberRange>().AnyAsync(r => r.Code == result.Value.Code, ct)) return Error.Conflict("Employee.PersonnelNumber.DuplicateRange");
        if (await OverlapsAsync(null, c.StartNumber, c.EndNumber, ct)) return Error.Conflict("Employee.PersonnelNumber.OverlappingRange");
        db.Add(result.Value); return Dto(result.Value);
    }
    private Task<bool> OverlapsAsync(Guid? id, string start, string end, CancellationToken ct) => db.Set<PersonnelNumberRange>()
        .AnyAsync(r => r.Id != id && string.Compare(r.StartNumber, end) <= 0 && string.Compare(r.EndNumber, start) >= 0, ct);
    public async Task<Result<PersonnelNumberRangeDto>> UpdateRangeAsync(UpdatePersonnelNumberRangeCommand c, CancellationToken ct)
    {
        await LockAsync(ct);
        var range = await db.Set<PersonnelNumberRange>().SingleOrDefaultAsync(r => r.Id == c.Id, ct);
        if (range is null) return Error.NotFound("Employee.PersonnelNumber.RangeNotFound");
        await db.Entry(range).ReloadAsync(ct);
        if (await OverlapsAsync(c.Id, range.StartNumber, c.EndNumber, ct)) return Error.Conflict("Employee.PersonnelNumber.OverlappingRange");
        var result = range.Update(c.Name, c.EndNumber); return result.IsFailure ? result.Error : Dto(range);
    }
    public async Task<Result> SetRangeActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        await LockAsync(ct);
        var range = await db.Set<PersonnelNumberRange>().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (range is null) return Error.NotFound("Employee.PersonnelNumber.RangeNotFound");
        await db.Entry(range).ReloadAsync(ct);
        if (!active && await db.Set<PersonnelNumberRule>().AnyAsync(r => r.RangeId == id && r.IsActive, ct)) return Error.Rule("Employee.PersonnelNumber.RangeInUse");
        range.SetActive(active); return Result.Success();
    }
    public async Task<IReadOnlyList<PersonnelNumberCompanyDto>> CompaniesAsync(CancellationToken ct)
    {
        var placement = await placementLoader.LoadAsync(ct);
        var companyType = placement.Types.FirstOrDefault(t => t.Code == "Company");
        return companyType is null ? [] : placement.UnitsById.Values.Where(u => u.TypeId == companyType.Id)
            .OrderBy(u => u.Name).Select(u => new PersonnelNumberCompanyDto(u.Id, u.Name,
                placement.UnitsById.Values.Where(a => a.Id != u.Id && placement.LevelUnitFor(a.Id, "Company")?.Id == u.Id)
                    .OrderBy(a => a.Name).Select(a => new PersonnelNumberAreaDto(a.Id, a.Name)).ToList())).ToList();
    }
    private async Task<PersonnelNumberRuleDto> RuleDtoAsync(PersonnelNumberRule rule, CancellationToken ct)
    {
        var placement = await placementLoader.LoadAsync(ct);
        var range = await db.Set<PersonnelNumberRange>().SingleAsync(r => r.Id == rule.RangeId, ct);
        return new(rule.Id, rule.CompanyId, placement.UnitsById.GetValueOrDefault(rule.CompanyId)?.Name ?? string.Empty,
            rule.Operation, rule.RangeId, range.Code, range.AssignmentMode, rule.IsActive, rule.PersonnelAreaId,
            rule.PersonnelAreaId is { } areaId ? placement.UnitsById.GetValueOrDefault(areaId)?.Name : null);
    }
    public async Task<IReadOnlyList<PersonnelNumberRuleDto>> ListRulesAsync(CancellationToken ct)
    {
        var rows = await db.Set<PersonnelNumberRule>().OrderBy(r => r.CompanyId).ThenBy(r => r.Operation).ToListAsync(ct);
        var result = new List<PersonnelNumberRuleDto>();
        foreach (var row in rows) result.Add(await RuleDtoAsync(row, ct));
        return result;
    }
    public async Task<Result<PersonnelNumberRuleDto>> SaveRuleAsync(Guid? id, Guid? companyId, PersonnelNumberOperation? operation, Guid rangeId, bool active, Guid? personnelAreaId, CancellationToken ct)
    {
        await LockAsync(ct);
        var range = await db.Set<PersonnelNumberRange>().SingleOrDefaultAsync(r => r.Id == rangeId && (!active || r.IsActive), ct);
        if (range is null) return Error.NotFound("Employee.PersonnelNumber.RangeNotFound");
        PersonnelNumberRule rule;
        if (id is { } ruleId)
        {
            var existing = await db.Set<PersonnelNumberRule>().SingleOrDefaultAsync(r => r.Id == ruleId, ct);
            if (existing is null) return Error.NotFound("Employee.PersonnelNumber.RuleNotFound");
            await db.Entry(existing).ReloadAsync(ct);
            rule = existing;
        }
        else
        {
            if (companyId is null || operation is null || !Enum.IsDefined(operation.Value)) return Error.Validation("Employee.PersonnelNumber.InvalidRule");
            var companies = await CompaniesAsync(ct);
            var company = companies.SingleOrDefault(c => c.Id == companyId);
            if (company is null || (personnelAreaId is { } area && !company.PersonnelAreas.Any(a => a.Id == area))) return Error.Validation("Employee.PersonnelNumber.InvalidCompany");
            rule = new PersonnelNumberRule(tenant.TenantId, companyId.Value, operation.Value, rangeId, personnelAreaId);
        }
        if (active)
        {
            var company = (await CompaniesAsync(ct)).SingleOrDefault(c => c.Id == rule.CompanyId);
            if (company is null || (rule.PersonnelAreaId is { } areaId && !company.PersonnelAreas.Any(a => a.Id == areaId))) return Error.Validation("Employee.PersonnelNumber.InvalidCompany");
        }
        if (active && await db.Set<PersonnelNumberRule>().AnyAsync(r => r.Id != rule.Id && r.CompanyId == rule.CompanyId && r.Operation == rule.Operation && r.PersonnelAreaId == rule.PersonnelAreaId && r.IsActive, ct)) return Error.Conflict("Employee.PersonnelNumber.DuplicateRule");
        rule.Update(rangeId, active);
        if (id is null) db.Add(rule);
        return await RuleDtoAsync(rule, ct);
    }
    public async Task<Result<PersonnelNumberAssignmentDto>> PreviewAsync(Guid? orgUnitId, PersonnelNumberOperation operation, CancellationToken ct)
    {
        if (!Enum.IsDefined(operation)) return Error.Validation("Employee.PersonnelNumber.InvalidRule");
        if (!await db.Set<PersonnelNumberRange>().AnyAsync(ct) && !await db.Set<PersonnelNumberRule>().AnyAsync(ct)) return new PersonnelNumberAssignmentDto(false);
        var placement = await placementLoader.LoadAsync(ct);
        var company = placement.LevelUnitFor(orgUnitId, "Company");
        if (company is null) return Error.Rule("Employee.PersonnelNumber.MissingRule");
        var rules = await db.Set<PersonnelNumberRule>().Where(r => r.CompanyId == company.Id && r.Operation == operation && r.IsActive).ToListAsync(ct);
        PersonnelNumberRule? rule = null;
        var visited = new HashSet<Guid>();
        var current = orgUnitId;
        while (current is { } unitId && visited.Add(unitId))
        {
            rule = rules.FirstOrDefault(r => r.PersonnelAreaId == unitId);
            if (rule is not null) break;
            current = placement.UnitsById.GetValueOrDefault(unitId)?.ParentId;
        }
        rule ??= rules.FirstOrDefault(r => r.PersonnelAreaId is null);
        if (rule is null) return Error.Rule("Employee.PersonnelNumber.MissingRule");
        var range = await db.Set<PersonnelNumberRange>().SingleOrDefaultAsync(r => r.Id == rule.RangeId && r.IsActive, ct);
        if (range is null) return Error.Rule("Employee.PersonnelNumber.MissingRule");
        var next = range.AssignmentMode == PersonnelNumberAssignmentMode.Internal ? await NextAsync(range, ct) : null;
        return new PersonnelNumberAssignmentDto(true, company.Id, company.Name, range.Id, range.Code, range.AssignmentMode, range.StartNumber, range.EndNumber, next,
            rule.PersonnelAreaId, rule.PersonnelAreaId is { } areaId ? placement.UnitsById.GetValueOrDefault(areaId)?.Name : null);
    }
    private async Task<string?> NextAsync(PersonnelNumberRange range, CancellationToken ct)
    {
        var next = range.LastAssignedNumber is null ? int.Parse(range.StartNumber, CultureInfo.InvariantCulture) : int.Parse(range.LastAssignedNumber, CultureInfo.InvariantCulture) + 1;
        var end = int.Parse(range.EndNumber, CultureInfo.InvariantCulture);
        while (next <= end)
        {
            var number = next.ToString("D8", CultureInfo.InvariantCulture);
            if (!await TakenAsync(number, ct) && !db.Employees.Local.Any(e => e.TenantId == tenant.TenantId && e.SicilNumarasi == number)) return number;
            next++;
        }
        return null;
    }
    private Task<bool> TakenAsync(string number, CancellationToken ct) => db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenant.TenantId && e.SicilNumarasi == number, ct);
    public async Task<Result<string?>> AssignAsync(Guid? orgUnitId, PersonnelNumberOperation operation, string? supplied, CancellationToken ct)
    {
        await LockAsync(ct);
        // Refresh tracked ranges after the lock: another request may have advanced their counters.
        foreach (var entry in db.ChangeTracker.Entries<PersonnelNumberRange>().Where(e => e.State == EntityState.Unchanged && e.Entity.TenantId == tenant.TenantId).ToArray()) await entry.ReloadAsync(ct);
        var preview = await PreviewAsync(orgUnitId, operation, ct);
        if (preview.IsFailure) return preview.Error;
        var p = preview.Value;
        if (!p.Configured)
        {
            supplied = string.IsNullOrWhiteSpace(supplied) ? null : supplied.Trim();
            if (supplied is not null && (await TakenAsync(supplied, ct) || db.Employees.Local.Any(e => e.TenantId == tenant.TenantId && e.SicilNumarasi == supplied))) return Error.Conflict("Employee.PersonnelNumber.AlreadyAssigned");
            return Result.Success<string?>(supplied);
        }
        var range = await db.Set<PersonnelNumberRange>().SingleAsync(r => r.Id == p.RangeId, ct);
        if (range.AssignmentMode == PersonnelNumberAssignmentMode.Internal)
        {
            if (!string.IsNullOrWhiteSpace(supplied)) return Error.Rule("Employee.PersonnelNumber.InternalSupplied");
            var number = await NextAsync(range, ct);
            if (number is null) return Error.Rule("Employee.PersonnelNumber.Exhausted");
            range.RecordAssignment(number);
            return number;
        }
        if (supplied is null || !range.Contains(supplied)) return Error.Validation("Employee.PersonnelNumber.ExternalInvalid");
        if (await TakenAsync(supplied, ct) || db.Employees.Local.Any(e => e.TenantId == tenant.TenantId && e.SicilNumarasi == supplied)) return Error.Conflict("Employee.PersonnelNumber.AlreadyAssigned");
        return supplied;
    }
    public void DiscardImportChanges() => db.ChangeTracker.Clear();
    public async Task<IReadOnlyList<LegacyPersonnelNumberDto>> LegacyAsync(Guid employeeId, CancellationToken ct) => await db.Set<LegacyPersonnelNumber>().AsNoTracking().Where(r => r.EmployeeId == employeeId).Select(r => new LegacyPersonnelNumberDto(r.SourceSystem, r.PersonnelNumber)).ToListAsync(ct);
    public async Task<Result> CheckLegacyAsync(Guid employeeId, string source, string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 100 || string.IsNullOrWhiteSpace(number) || number.Trim().Length > 100) return Error.Validation("Employee.PersonnelNumber.InvalidLegacy");
        source = source.Trim(); number = number.Trim();
        var match = await db.Set<LegacyPersonnelNumber>().SingleOrDefaultAsync(r => r.SourceSystem == source && r.PersonnelNumber == number, ct);
        return match is null || match.EmployeeId == employeeId ? Result.Success() : Error.Conflict("Employee.PersonnelNumber.LegacyConflict");
    }
    public async Task<Result> AddLegacyAsync(Guid employeeId, string source, string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 100 || string.IsNullOrWhiteSpace(number) || number.Trim().Length > 100) return Error.Validation("Employee.PersonnelNumber.InvalidLegacy");
        source = source.Trim(); number = number.Trim();
        var match = await db.Set<LegacyPersonnelNumber>().SingleOrDefaultAsync(r => r.SourceSystem == source && r.PersonnelNumber == number, ct)
            ?? db.Set<LegacyPersonnelNumber>().Local.FirstOrDefault(r => r.TenantId == tenant.TenantId && r.SourceSystem == source && r.PersonnelNumber == number);
        if (match is not null) return match.EmployeeId == employeeId ? Result.Success() : Error.Conflict("Employee.PersonnelNumber.LegacyConflict");
        db.Add(new LegacyPersonnelNumber(tenant.TenantId, employeeId, source, number)); return Result.Success();
    }
    public Task ExecuteImportAsync(Func<CancellationToken, Task> action, CancellationToken ct) => db.ExecuteInTransactionAsync(async token => { await LockAsync(token); await action(token); }, ct);
}

public sealed class PersonnelNumberRangeConfiguration : IEntityTypeConfiguration<PersonnelNumberRange>
{
    public void Configure(EntityTypeBuilder<PersonnelNumberRange> b)
    {
        b.ToTable("personnel_number_ranges"); b.HasKey(r => r.Id);
        b.Property(r => r.Code).HasMaxLength(30).IsRequired(); b.Property(r => r.Name).HasMaxLength(200).IsRequired();
        b.Property(r => r.StartNumber).HasMaxLength(8).IsRequired(); b.Property(r => r.EndNumber).HasMaxLength(8).IsRequired(); b.Property(r => r.LastAssignedNumber).HasMaxLength(8);
        b.Property(r => r.AssignmentMode).HasConversion<string>().HasMaxLength(10).IsRequired(); b.HasIndex(r => new { r.TenantId, r.Code }).IsUnique(); b.Ignore(r => r.DomainEvents);
    }
}
public sealed class PersonnelNumberRuleConfiguration : IEntityTypeConfiguration<PersonnelNumberRule>
{
    public void Configure(EntityTypeBuilder<PersonnelNumberRule> b)
    {
        b.ToTable("personnel_number_rules"); b.HasKey(r => r.Id); b.Property(r => r.Operation).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.CompanyId, r.Operation }).IsUnique().HasFilter("is_active AND personnel_area_id IS NULL");
        b.HasIndex(r => new { r.TenantId, r.CompanyId, r.Operation, r.PersonnelAreaId }).IsUnique().HasFilter("is_active AND personnel_area_id IS NOT NULL");
        b.HasOne<PersonnelNumberRange>().WithMany().HasForeignKey(r => r.RangeId).OnDelete(DeleteBehavior.Restrict); b.Ignore(r => r.DomainEvents);
    }
}
public sealed class LegacyPersonnelNumberConfiguration : IEntityTypeConfiguration<LegacyPersonnelNumber>
{
    public void Configure(EntityTypeBuilder<LegacyPersonnelNumber> b)
    {
        b.ToTable("legacy_personnel_numbers"); b.HasKey(r => r.Id); b.Property(r => r.SourceSystem).HasMaxLength(100).IsRequired(); b.Property(r => r.PersonnelNumber).HasMaxLength(100).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.SourceSystem, r.PersonnelNumber }).IsUnique();
        b.HasOne<Domain.Employees.Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict); b.Ignore(r => r.DomainEvents);
    }
}
