using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Infrastructure.Persistence;

/// <summary>Employee module DbContext.</summary>
public sealed class EmployeeDbContext(DbContextOptions<EmployeeDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "employee";

    public override string Schema => SchemaName;

    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();

    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
}

internal static class EmployeeTables
{
    public const string Employees = "employees";
    public const string CareerRecords = "career_records";
    public const string CareerOrgAssignments = "career_org_assignments";
    public const string CareerRecordIdColumn = "CareerRecordId";
    public const string SalaryRecords = "salary_records";
    public const string BankAccounts = "bank_accounts";
    public const string CustomFieldDefinitions = "custom_field_definitions";
    public const string CustomFieldValues = "custom_field_values";

    public const int StatusLength = 30;
}

/// <summary>jsonb value conversion for the small immutable string list on a custom field definition (its Options).</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<IReadOnlyList<string>> AsJsonb(this PropertyBuilder<IReadOnlyList<string>> property)
    {
        var converter = new ValueConverter<IReadOnlyList<string>, string>(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<List<string>>(s, Options) ?? new List<string>());
        var comparer = new ValueComparer<IReadOnlyList<string>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => v);
        property.HasConversion(converter, comparer).HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();
        return property;
    }
}

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<EmployeeEntity>
{
    public void Configure(EntityTypeBuilder<EmployeeEntity> b)
    {
        b.ToTable(EmployeeTables.Employees);
        b.HasKey(x => x.Id);

        b.Property(x => x.FirstName).HasMaxLength(EmployeeLimits.NameMaxLength).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(EmployeeLimits.NameMaxLength).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(EmployeeLimits.DisplayNameMaxLength).IsRequired();
        b.Property(x => x.NationalId).HasMaxLength(EmployeeLimits.NationalIdLength);
        b.Property(x => x.WorkEmail).HasMaxLength(EmployeeLimits.EmailMaxLength);
        b.Property(x => x.PersonalEmail).HasMaxLength(EmployeeLimits.EmailMaxLength);
        b.Property(x => x.WorkPhone).HasMaxLength(EmployeeLimits.PhoneMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.SuspensionReason).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);

        b.Property(x => x.Gender).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired().HasDefaultValue(Gender.Unspecified);
        b.Property(x => x.MaritalStatus).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired().HasDefaultValue(MaritalStatus.Unspecified);
        b.Property(x => x.EducationStatus).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.HighestEducationLevel).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.MilitaryStatus).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.DisabilityDegree).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.BloodType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.ContractType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);

        b.Property(x => x.MobilePhone).HasMaxLength(EmployeeLimits.PhoneMaxLength);
        b.Property(x => x.HomePhone).HasMaxLength(EmployeeLimits.PhoneMaxLength);
        b.Property(x => x.AddressLine).HasMaxLength(EmployeeLimits.AddressLineMaxLength);
        b.Property(x => x.City).HasMaxLength(EmployeeLimits.CityMaxLength);
        b.Property(x => x.PostalCode).HasMaxLength(EmployeeLimits.PostalCodeMaxLength);

        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => x.OrgUnitId);
        b.HasIndex(x => x.ManagerEmployeeId);

        // One employee record per Identity user, when linked; NULL (not-yet-linked) rows are excluded
        // from the uniqueness check by the partial index filter.
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");

        b.HasIndex(x => new { x.TenantId, x.NationalId }).IsUnique().HasFilter("national_id IS NOT NULL");

        // Career history is part of the aggregate: always loaded with it (read-side list queries opt out with IgnoreAutoIncludes).
        b.HasMany(x => x.CareerRecords).WithOne().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.CareerRecords).AutoInclude();

        // Salary history, bank accounts and custom field values: explicitly included by their own use cases only
        // (never auto-included, unlike career history).
        b.HasMany(x => x.SalaryRecords).WithOne().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.BankAccounts).WithOne().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.CustomFieldValues).WithOne().HasForeignKey(v => v.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.CareerTimeline);
        b.Ignore(x => x.SalaryHistory);
        b.Ignore(x => x.OrderedBankAccounts);
        b.Ignore(x => x.HasCareerData);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CareerRecordConfiguration : IEntityTypeConfiguration<CareerRecord>
{
    public void Configure(EntityTypeBuilder<CareerRecord> b)
    {
        b.ToTable(EmployeeTables.CareerRecords);
        b.HasKey(x => x.Id);

        // Client-generated (v7 / employee id for the initial record): without this a record added to a tracked employee is saved as an UPDATE.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ChangeType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength);
        b.Property(x => x.Note).HasMaxLength(EmployeeLimits.CareerNoteMaxLength);

        b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
        b.HasIndex(x => x.ManagerEmployeeId);
        b.Ignore(x => x.IsOpen);

        b.OwnsMany(x => x.Assignments, a =>
        {
            a.ToTable(EmployeeTables.CareerOrgAssignments);
            a.WithOwner().HasForeignKey(EmployeeTables.CareerRecordIdColumn);

            // One unit per level per record; a level's unit is moved in place, never removed and re-added in one save.
            a.HasKey(EmployeeTables.CareerRecordIdColumn, nameof(EmployeeOrgAssignment.OrgUnitTypeId));
            a.Property(x => x.OrgUnitTypeId).ValueGeneratedNever();
            a.HasIndex(x => x.OrgUnitId);
        });

        b.Navigation(x => x.Assignments).AutoInclude();
    }
}

public sealed class SalaryRecordConfiguration : IEntityTypeConfiguration<SalaryRecord>
{
    public void Configure(EntityTypeBuilder<SalaryRecord> b)
    {
        b.ToTable(EmployeeTables.SalaryRecords);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Amount).HasPrecision(EmployeeLimits.MoneyPrecision, EmployeeLimits.MoneyScale);
        b.Property(x => x.TransportAllowance).HasPrecision(EmployeeLimits.MoneyPrecision, EmployeeLimits.MoneyScale);
        b.Property(x => x.MealAllowance).HasPrecision(EmployeeLimits.MoneyPrecision, EmployeeLimits.MoneyScale);
        b.Property(x => x.Currency).HasMaxLength(Currencies.CodeLength).IsFixedLength().IsRequired();
        b.Property(x => x.SalaryType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.PayFrequency).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.Note).HasMaxLength(EmployeeLimits.SalaryNoteMaxLength);

        b.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique();
    }
}

public sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable(EmployeeTables.BankAccounts);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ProtectedIban).HasMaxLength(EmployeeLimits.ProtectedIbanMaxLength).IsRequired();
        b.Property(x => x.MaskedIban).HasMaxLength(EmployeeLimits.MaskedIbanMaxLength).IsRequired();
        b.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.BankName).HasMaxLength(EmployeeLimits.BankNameMaxLength);
        b.Property(x => x.Currency).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        b.Property(x => x.Note).HasMaxLength(EmployeeLimits.BankAccountNoteMaxLength);

        b.HasIndex(x => x.EmployeeId);
    }
}

public sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> b)
    {
        b.ToTable(EmployeeTables.CustomFieldDefinitions);
        b.HasKey(x => x.Id);

        b.Property(x => x.Key).HasMaxLength(CustomFieldLimits.KeyMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(CustomFieldLimits.NameMaxLength).IsRequired();
        b.Property(x => x.DataType).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.Section).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.IsReportable).IsRequired();

        // Options has no setter (backed by the private _options field): field-only access lets EF read/materialize
        // it without one, the same way CareerRecord's owned Assignments collection is backed by a private field.
        b.Property(x => x.Options)
            .HasField("_options")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AsJsonb();

        b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.IsActive });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> b)
    {
        b.ToTable(EmployeeTables.CustomFieldValues);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Value).HasMaxLength(CustomFieldLimits.ValueMaxLength);

        b.HasIndex(x => x.EmployeeId);
        b.HasIndex(x => new { x.EmployeeId, x.DefinitionId }).IsUnique();
        b.HasIndex(x => x.DefinitionId);
    }
}
