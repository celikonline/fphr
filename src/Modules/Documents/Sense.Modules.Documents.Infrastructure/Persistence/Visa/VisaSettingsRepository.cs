using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;

namespace Sense.Modules.Documents.Infrastructure.Persistence.Visa;

/// <summary>EF Core implementation of <see cref="IVisaSettingsRepository"/>. Mirrors Attendance's
/// ShiftSettingsRepository: <c>FirstOrDefaultAsync</c> falls back to <c>Local</c> so a settings row added earlier
/// in the same unit of work (before SaveChanges) is still found without a round trip.</summary>
public sealed class VisaSettingsRepository(DocumentsDbContext context) : IVisaSettingsRepository
{
    public void Add(VisaSettings settings) => context.VisaSettings.Add(settings);

    public async Task<VisaSettings?> FindAsync(CancellationToken cancellationToken = default) =>
        await context.VisaSettings.FirstOrDefaultAsync(cancellationToken)
        ?? context.VisaSettings.Local.FirstOrDefault();
}
