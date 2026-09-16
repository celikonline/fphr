using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Reporting.Infrastructure.Persistence;

/// <summary>Reporting module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "reporting").</summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IReportingUnitOfWork
{
    public const string SchemaName = "reporting";

    public override string Schema => SchemaName;

    public DbSet<ReportExecution> ReportExecutions => Set<ReportExecution>();
}

internal static class ReportingTables
{
    public const string ReportExecutions = "report_executions";

    public const int ReportKeyLength = 100;
    public const int StatusLength = 30;
    public const int FileNameLength = 255;
    public const int ContentTypeLength = 100;
    public const int ErrorMessageLength = 2000;
}

public sealed class ReportExecutionConfiguration : IEntityTypeConfiguration<ReportExecution>
{
    public void Configure(EntityTypeBuilder<ReportExecution> b)
    {
        b.ToTable(ReportingTables.ReportExecutions);
        b.HasKey(x => x.Id);

        b.Property(x => x.ReportKey).HasMaxLength(ReportingTables.ReportKeyLength).IsRequired();
        b.Property(x => x.ParametersJson).HasColumnType(PersistenceDefaults.JsonbColumnType);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(ReportingTables.StatusLength).IsRequired();
        b.Property(x => x.ResultFileName).HasMaxLength(ReportingTables.FileNameLength);
        b.Property(x => x.ResultContentType).HasMaxLength(ReportingTables.ContentTypeLength);
        b.Property(x => x.ErrorMessage).HasMaxLength(ReportingTables.ErrorMessageLength);

        b.HasIndex(x => new { x.TenantId, x.RequestedByUserId, x.RequestedAt });

        b.Ignore(x => x.DomainEvents);
    }
}
