using Microsoft.EntityFrameworkCore;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Domain;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Training.Infrastructure.Persistence;

public class TrainingDbContext : ModuleDbContext, ITrainingUnitOfWork
{
    public const string SchemaName = "training";

    public TrainingDbContext(DbContextOptions<TrainingDbContext> options)
        : base(options, SchemaName)
    {
    }

    public override string Schema => SchemaName;

    public DbSet<TrainingProgram> TrainingPrograms => Set<TrainingProgram>();
    public DbSet<ProgramEnrollment> Enrollments => Set<ProgramEnrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ApplyConfigurations(builder);
    }

    private static void ApplyConfigurations(ModelBuilder builder)
    {
        // TrainingProgram configuration
        builder.Entity<TrainingProgram>(b =>
        {
            b.ToTable("TrainingPrograms");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            b.Property(x => x.Description)
                .HasMaxLength(2000);

            b.Property(x => x.StartDate)
                .IsRequired();

            b.Property(x => x.EndDate)
                .IsRequired();

            b.HasIndex(x => new { x.TenantId, x.Title })
                .IsUnique();
        });

        // ProgramEnrollment configuration
        builder.Entity<ProgramEnrollment>(b =>
        {
            b.ToTable("ProgramEnrollments");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.ProgramId)
                .IsRequired();

            b.Property(x => x.EmployeeId)
                .IsRequired();

            b.Property(x => x.EnrolledAt)
                .IsRequired();

            b.HasIndex(x => new { x.TenantId, x.ProgramId })
                .HasName("IX_ProgramEnrollments_TenantId_ProgramId");

            b.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasName("IX_ProgramEnrollments_TenantId_EmployeeId");
        });
    }
}
