using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Employee.Domain.Employees;

namespace Sense.Modules.Employee.Infrastructure.Persistence;

public sealed class EmployeeNoteConfiguration : IEntityTypeConfiguration<EmployeeNote>
{
    public void Configure(EntityTypeBuilder<EmployeeNote> b)
    {
        b.ToTable("employee_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(EmployeeNoteLimits.TitleMaxLength).IsRequired();
        b.Property(x => x.Content).HasMaxLength(EmployeeNoteLimits.ContentMaxLength).IsRequired();
        b.Property(x => x.Importance).HasConversion<string>().HasMaxLength(EmployeeTables.StatusLength).IsRequired();
        b.Property(x => x.AuthorName).HasMaxLength(EmployeeNoteLimits.AuthorNameMaxLength);
        b.Property(x => x.AttachmentFileName).HasMaxLength(EmployeeNoteLimits.FileNameMaxLength);
        b.Property(x => x.AttachmentStorageKey).HasMaxLength(EmployeeNoteLimits.StorageKeyMaxLength);
        b.Property(x => x.AttachmentContentType).HasMaxLength(EmployeeNoteLimits.ContentTypeMaxLength);
        b.Property(x => x.PendingAttachmentFileName).HasMaxLength(EmployeeNoteLimits.FileNameMaxLength);
        b.Property(x => x.PendingAttachmentStorageKey).HasMaxLength(EmployeeNoteLimits.StorageKeyMaxLength);
        b.Property(x => x.PendingAttachmentContentType).HasMaxLength(EmployeeNoteLimits.ContentTypeMaxLength);
        b.Ignore(x => x.HasConfirmedAttachment);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAt });
    }
}

public sealed class EmployeeNoteRepository(EmployeeDbContext context) : IEmployeeNoteRepository
{
    public void Add(EmployeeNote note) => context.EmployeeNotes.Add(note);

    public void Remove(EmployeeNote note) => context.EmployeeNotes.Remove(note);

    public Task<EmployeeNote?> GetTrackedAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        context.EmployeeNotes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

    public async Task<(IReadOnlyList<EmployeeNote> Items, long Total)> ListAsync(
        Guid? employeeId, EmployeeNoteImportance? importance, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.EmployeeNotes.AsNoTracking().AsQueryable();
        if (employeeId is { } id)
        {
            query = query.Where(n => n.EmployeeId == id);
        }

        if (importance is { } level)
        {
            query = query.Where(n => n.Importance == level);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(skip).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (items, total);
    }
}
