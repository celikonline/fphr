using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Infrastructure.Persistence;

/// <summary>Employee module DbContext (Faz 0 minimal stub - full personnel schema is Faz 1).</summary>
public sealed class EmployeeDbContext(DbContextOptions<EmployeeDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "employee";

    public override string Schema => SchemaName;

    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();
}

internal static class EmployeeTables
{
    public const string Employees = "employees";

    public const int DisplayNameLength = 200;
    public const int StatusLength = 30;
}

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<EmployeeEntity>
{
    public void Configure(EntityTypeBuilder<EmployeeEntity> b)
    {
        b.ToTable(EmployeeTables.Employees);
        b.HasKey(x => x.Id);

        b.Property(x => x.DisplayName).HasMaxLength(EmployeeTables.DisplayNameLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => x.OrgUnitId);

        // One employee record per Identity user, when linked; NULL (not-yet-linked) rows are excluded
        // from the uniqueness check by the partial index filter.
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");

        b.Ignore(x => x.DomainEvents);
    }
}
