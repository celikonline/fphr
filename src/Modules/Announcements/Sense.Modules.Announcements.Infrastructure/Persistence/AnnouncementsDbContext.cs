using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>Announcements module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "announcements").</summary>
public sealed class AnnouncementsDbContext(DbContextOptions<AnnouncementsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IAnnouncementsUnitOfWork
{
    public const string SchemaName = "announcements";

    public override string Schema => SchemaName;

    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementReadRecord> AnnouncementReadRecords => Set<AnnouncementReadRecord>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<HelpSearchLog> HelpSearchLogs => Set<HelpSearchLog>();
    public DbSet<CompanyEvent> CompanyEvents => Set<CompanyEvent>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
