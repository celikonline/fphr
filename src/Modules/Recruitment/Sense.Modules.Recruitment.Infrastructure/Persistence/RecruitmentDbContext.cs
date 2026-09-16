using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.AutomationRules;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>Recruitment module DbContext (ATS/İşe Alım). Schema "recruitment".</summary>
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

    public DbSet<RecruitmentFormTemplate> FormTemplates => Set<RecruitmentFormTemplate>();

    public DbSet<RecruitmentFormResponse> FormResponses => Set<RecruitmentFormResponse>();

    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
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
    public const string FormTemplates = "form_templates";
    public const string FormFields = "form_fields";
    public const string FormResponses = "form_responses";
    public const string FieldAnswers = "field_answers";
    public const string AutomationRules = "automation_rules";

    /// <summary>String length for enum-as-string columns (Status, EmploymentType, Stage...).</summary>
    public const int EnumCodeLength = 30;
}

/// <summary>jsonb value conversion for a small immutable string list (mirrors Employee.Infrastructure's identical helper for CustomFieldDefinition.Options).</summary>
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

public sealed class RecruitmentFormTemplateConfiguration : IEntityTypeConfiguration<RecruitmentFormTemplate>
{
    public void Configure(EntityTypeBuilder<RecruitmentFormTemplate> b)
    {
        b.ToTable(RecruitmentTables.FormTemplates);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(RecruitmentLimits.NameMaxLength).IsRequired();
        b.Property(x => x.TemplateType).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.IsActive });

        b.OwnsMany(x => x.Fields, f =>
        {
            f.ToTable(RecruitmentTables.FormFields);
            f.WithOwner().HasForeignKey("TemplateId");
            f.HasKey(x => x.Id);

            // Client-generated Guid key (Guid.CreateVersion7() at add-time) - see ApprovalTaskConfiguration's identical
            // comment in Workflow: without this, a field appended to an already-tracked template in a later
            // SaveChanges can be misclassified as Modified instead of Added.
            f.Property(x => x.Id).ValueGeneratedNever();

            f.Property(x => x.Label).HasMaxLength(RecruitmentLimits.NameMaxLength).IsRequired();
            f.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();
            f.Property(x => x.Options).AsJsonb();
        });

        b.Navigation(x => x.Fields).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RecruitmentFormResponseConfiguration : IEntityTypeConfiguration<RecruitmentFormResponse>
{
    public void Configure(EntityTypeBuilder<RecruitmentFormResponse> b)
    {
        b.ToTable(RecruitmentTables.FormResponses);
        b.HasKey(x => x.Id);

        b.Property(x => x.WeightedScore).HasPrecision(5, 2);

        b.HasIndex(x => new { x.TenantId, x.JobApplicationId });
        b.HasIndex(x => new { x.JobApplicationId, x.TemplateId });

        b.OwnsMany(x => x.Answers, a =>
        {
            a.ToTable(RecruitmentTables.FieldAnswers);
            a.WithOwner().HasForeignKey("ResponseId");
            a.HasKey("ResponseId", nameof(FieldAnswer.FieldId));

            a.Property(x => x.Value).HasMaxLength(RecruitmentLimits.FormAnswerMaxLength).IsRequired();
        });

        b.Navigation(x => x.Answers).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> b)
    {
        b.ToTable(RecruitmentTables.AutomationRules);
        b.HasKey(x => x.Id);

        b.Property(x => x.FromStage).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();
        b.Property(x => x.ToStage).HasConversion<string>().HasMaxLength(RecruitmentTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.FromStage, x.ToStage, x.IsActive });

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
