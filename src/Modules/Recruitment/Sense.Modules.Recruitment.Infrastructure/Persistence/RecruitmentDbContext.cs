using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>Recruitment module DbContext (ATS/İşe Alım - R0: postings, candidates, applications). Schema "recruitment".</summary>
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IRecruitmentUnitOfWork
{
    public const string SchemaName = "recruitment";

    public override string Schema => SchemaName;

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<JobRequisition> JobRequisitions => Set<JobRequisition>();

    public DbSet<Tag> Tags => Set<Tag>();
}

internal static class RecruitmentTables
{
    public const string JobPostings = "job_postings";
    public const string JobPostingAccess = "job_posting_access";
    public const string Candidates = "candidates";
    public const string CandidateTags = "candidate_tags";
    public const string JobApplications = "job_applications";
    public const string JobRequisitions = "job_requisitions";
    public const string Tags = "tags";

    /// <summary>String length for enum-as-string columns (Status, EmploymentType, Stage...).</summary>
    public const int EnumCodeLength = 30;
}

public sealed class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> b)
    {
        b.ToTable(RecruitmentTables.JobPostings);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(RecruitmentLimits.TitleMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(RecruitmentLimits.DescriptionMaxLength);
        b.Property(x => x.CompanyNotes).HasMaxLength(RecruitmentLimits.CompanyNotesMaxLength);
        b.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();
        b.Property(x => x.ArchiveReason).HasConversion<string?>().HasMaxLength(RecruitmentTables.EnumCodeLength);
        b.Property(x => x.ApplySlug).HasMaxLength(RecruitmentLimits.ApplySlugLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => x.ApplySlug).IsUnique();
        b.HasIndex(x => x.RequisitionId);

        b.OwnsMany(x => x.Access, a =>
        {
            a.ToTable(RecruitmentTables.JobPostingAccess);
            a.WithOwner().HasForeignKey(x => x.JobPostingId);
            a.HasKey(x => new { x.JobPostingId, x.UserId });
        });

        b.Navigation(x => x.Access).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class JobRequisitionConfiguration : IEntityTypeConfiguration<JobRequisition>
{
    public void Configure(EntityTypeBuilder<JobRequisition> b)
    {
        b.ToTable(RecruitmentTables.JobRequisitions);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(RecruitmentLimits.TitleMaxLength).IsRequired();
        b.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();
        b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();
        b.Property(x => x.Justification).HasMaxLength(RecruitmentLimits.DescriptionMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => x.ApprovalRequestId);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> b)
    {
        b.ToTable(RecruitmentTables.Candidates);
        b.HasKey(x => x.Id);

        b.Property(x => x.FirstName).HasMaxLength(RecruitmentLimits.NameMaxLength).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(RecruitmentLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Email).HasMaxLength(RecruitmentLimits.EmailMaxLength).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(RecruitmentLimits.PhoneMaxLength);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Email });

        b.OwnsMany(x => x.Tags, a =>
        {
            a.ToTable(RecruitmentTables.CandidateTags);
            a.WithOwner().HasForeignKey(x => x.CandidateId);
            a.HasKey(x => new { x.CandidateId, x.TagId });
        });

        b.Navigation(x => x.Tags).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable(RecruitmentTables.Tags);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(RecruitmentLimits.NameMaxLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable(RecruitmentTables.JobApplications);
        b.HasKey(x => x.Id);

        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.JobPostingId });
        b.HasIndex(x => new { x.TenantId, x.CandidateId });
        // One application per (posting, candidate) pair - re-applying is done by moving the existing application's stage.
        b.HasIndex(x => new { x.JobPostingId, x.CandidateId }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}
