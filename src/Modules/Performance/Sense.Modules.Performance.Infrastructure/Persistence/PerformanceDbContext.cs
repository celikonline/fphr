using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Performance.Infrastructure.Persistence;

/// <summary>Performance module DbContext (review cycles/competencies/participants/assignments). Schema "performance".</summary>
public sealed class PerformanceDbContext(DbContextOptions<PerformanceDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "performance";

    public override string Schema => SchemaName;

    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();

    public DbSet<ReviewCompetency> ReviewCompetencies => Set<ReviewCompetency>();

    public DbSet<ReviewQuestion> ReviewQuestions => Set<ReviewQuestion>();

    public DbSet<ReviewRelationConfig> ReviewRelationConfigs => Set<ReviewRelationConfig>();

    public DbSet<ReviewParticipant> ReviewParticipants => Set<ReviewParticipant>();

    public DbSet<ReviewAssignment> ReviewAssignments => Set<ReviewAssignment>();

    public DbSet<ReviewResponse> ReviewResponses => Set<ReviewResponse>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalCategory> GoalCategories => Set<GoalCategory>();
}

internal static class PerformanceTables
{
    public const string ReviewCycles = "review_cycles";
    public const string ReviewCompetencies = "review_competencies";
    public const string ReviewQuestions = "review_questions";
    public const string ReviewRelationConfigs = "review_relation_configs";
    public const string ReviewParticipants = "review_participants";
    public const string ReviewAssignments = "review_assignments";
    public const string ReviewResponses = "review_responses";
    public const string Goals = "goals";
    public const string GoalCategories = "goal_categories";

    /// <summary>String length for enum-as-string columns.</summary>
    public const int EnumCodeLength = 30;
}

public sealed class ReviewCycleConfiguration : IEntityTypeConfiguration<ReviewCycle>
{
    public void Configure(EntityTypeBuilder<ReviewCycle> b)
    {
        b.ToTable(PerformanceTables.ReviewCycles);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.RatingScale).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.DefaultCommentMode).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.EvaluatorLabelMode).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.CompletedBy).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.GoalEntryBy).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.CompetencyWeight).HasPrecision(5, 2);

        b.HasIndex(x => new { x.TenantId, x.Status });

        // Deep aggregate (config + participants) always loads fully - commands mutate it in memory via the aggregate root.
        b.HasMany(x => x.Competencies).WithOne().HasForeignKey(c => c.ReviewCycleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.RelationConfigs).WithOne().HasForeignKey(c => c.ReviewCycleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Participants).WithOne().HasForeignKey(p => p.ReviewCycleId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Competencies).AutoInclude();
        b.Navigation(x => x.RelationConfigs).AutoInclude();
        b.Navigation(x => x.Participants).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ReviewCompetencyConfiguration : IEntityTypeConfiguration<ReviewCompetency>
{
    public void Configure(EntityTypeBuilder<ReviewCompetency> b)
    {
        b.ToTable(PerformanceTables.ReviewCompetencies);
        b.HasKey(x => x.Id);

        // Client-generated Guid id (Guid.CreateVersion7() in the constructor) - without this EF can misclassify an
        // entity appended to an already-tracked ReviewCycle.Competencies as Modified instead of Added on a later
        // SaveChanges, which flags every scalar (including TenantId) as changed and trips the immutability check
        // (same class of bug documented on Workflow's ApprovalTask.Id).
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Name).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();

        b.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.ReviewCompetencyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Questions).AutoInclude();
    }
}

public sealed class ReviewQuestionConfiguration : IEntityTypeConfiguration<ReviewQuestion>
{
    public void Configure(EntityTypeBuilder<ReviewQuestion> b)
    {
        b.ToTable(PerformanceTables.ReviewQuestions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Text).HasMaxLength(PerformanceLimits.TextMaxLength).IsRequired();
        b.Property(x => x.CommentMode).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
    }
}

public sealed class ReviewRelationConfigConfiguration : IEntityTypeConfiguration<ReviewRelationConfig>
{
    public void Configure(EntityTypeBuilder<ReviewRelationConfig> b)
    {
        b.ToTable(PerformanceTables.ReviewRelationConfigs);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Relation).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.HasIndex(x => new { x.ReviewCycleId, x.Relation }).IsUnique();
    }
}

public sealed class ReviewParticipantConfiguration : IEntityTypeConfiguration<ReviewParticipant>
{
    public void Configure(EntityTypeBuilder<ReviewParticipant> b)
    {
        b.ToTable(PerformanceTables.ReviewParticipants);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.CompetencyProgress).HasPrecision(5, 2);
        b.Property(x => x.CompetencyScore).HasPrecision(7, 2);
        b.Property(x => x.GoalProgress).HasPrecision(5, 2);
        b.Property(x => x.GoalScore).HasPrecision(7, 2);
        b.Property(x => x.FinalScore).HasPrecision(7, 2);

        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasIndex(x => new { x.ReviewCycleId, x.EmployeeId }).IsUnique();

        b.HasMany(x => x.Assignments).WithOne().HasForeignKey(a => a.ReviewParticipantId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Goals).WithOne().HasForeignKey(g => g.ReviewParticipantId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Assignments).AutoInclude();
        b.Navigation(x => x.Goals).AutoInclude();
    }
}

public sealed class ReviewAssignmentConfiguration : IEntityTypeConfiguration<ReviewAssignment>
{
    public void Configure(EntityTypeBuilder<ReviewAssignment> b)
    {
        b.ToTable(PerformanceTables.ReviewAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Relation).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);

        b.HasIndex(x => new { x.TenantId, x.EvaluatorEmployeeId });

        b.HasMany(x => x.Responses).WithOne().HasForeignKey(r => r.ReviewAssignmentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Responses).AutoInclude();
    }
}

public sealed class ReviewResponseConfiguration : IEntityTypeConfiguration<ReviewResponse>
{
    public void Configure(EntityTypeBuilder<ReviewResponse> b)
    {
        b.ToTable(PerformanceTables.ReviewResponses);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Comment).HasMaxLength(PerformanceLimits.CommentMaxLength);
        b.Property(x => x.Score).HasPrecision(5, 2);

        b.HasIndex(x => new { x.ReviewAssignmentId, x.QuestionId }).IsUnique();
    }
}

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> b)
    {
        b.ToTable(PerformanceTables.Goals);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Name).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(PerformanceLimits.TextMaxLength);
        b.Property(x => x.ResultComment).HasMaxLength(PerformanceLimits.CommentMaxLength);
        b.Property(x => x.TargetValue).HasPrecision(18, 2);
        b.Property(x => x.ActualValue).HasPrecision(18, 2);
        b.Property(x => x.Points).HasPrecision(7, 2);
        b.Property(x => x.Weight).HasPrecision(5, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength);

        b.Property(x => x.Scale)
            .HasConversion(
                scale => JsonSerializer.Serialize(scale, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<List<GoalScaleRange>>(json, JsonSerializerOptions.Web) ?? new List<GoalScaleRange>())
            .HasColumnType(PersistenceDefaults.JsonbColumnType);

        b.HasIndex(x => x.CategoryId);
    }
}

public sealed class GoalCategoryConfiguration : IEntityTypeConfiguration<GoalCategory>
{
    public void Configure(EntityTypeBuilder<GoalCategory> b)
    {
        b.ToTable(PerformanceTables.GoalCategories);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
