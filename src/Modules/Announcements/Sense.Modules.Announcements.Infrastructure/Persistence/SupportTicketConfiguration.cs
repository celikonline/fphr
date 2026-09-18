using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

internal static class SupportTables
{
    public const string SupportTickets = "support_tickets";
    public const string HelpSearchLogs = "help_search_logs";

    public const int SubjectLength = 200;
    public const int BodyLength = 10_000;
    public const int ResolutionLength = 5_000;
    public const int EnumLength = 30;
    public const int RouteLength = 300;
    public const int CorrelationIdLength = 100;
    public const int ExternalRefLength = 200;
    public const int SearchTermLength = 200;
}

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable(SupportTables.SupportTickets);
        b.HasKey(x => x.Id);

        b.Property(x => x.Subject).HasMaxLength(SupportTables.SubjectLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(SupportTables.BodyLength).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(SupportTables.EnumLength).IsRequired();
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(SupportTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SupportTables.EnumLength).IsRequired();
        b.Property(x => x.SourceRoute).HasMaxLength(SupportTables.RouteLength);
        b.Property(x => x.CorrelationId).HasMaxLength(SupportTables.CorrelationIdLength);
        b.Property(x => x.ExternalRef).HasMaxLength(SupportTables.ExternalRefLength);
        b.Property(x => x.Resolution).HasMaxLength(SupportTables.ResolutionLength);

        b.HasIndex(x => new { x.TenantId, x.RequestedByUserId, x.CreatedAt });
        b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class HelpSearchLogConfiguration : IEntityTypeConfiguration<HelpSearchLog>
{
    public void Configure(EntityTypeBuilder<HelpSearchLog> b)
    {
        b.ToTable(SupportTables.HelpSearchLogs);
        b.HasKey(x => x.Id);

        b.Property(x => x.Term).HasMaxLength(SupportTables.SearchTermLength).IsRequired();
        b.Property(x => x.Route).HasMaxLength(SupportTables.RouteLength);

        b.HasIndex(x => new { x.TenantId, x.Term });
        b.HasIndex(x => new { x.TenantId, x.ResultCount, x.SearchedAt });
    }
}
