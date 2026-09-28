using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Infrastructure.Terminals;

/// <inheritdoc cref="ITerminalCredentialSaver"/>
/// <remarks>
/// Komut işlemi içinde çağrılır: EF Core SaveChanges'ten önce kayıt noktası (savepoint) açar ve hata olunca ona döner, işlem kullanılabilir kalır.
/// Başarısız eklenen kimlik izlemeden çıkarılır (sonraki kayıtta tekrar denenmesin). Özet/değer loglanmaz, iletiye girmez.
/// </remarks>
public sealed class TerminalCredentialSaver(AttendanceDbContext context) : ITerminalCredentialSaver
{
    /// <summary>EmployeeTerminalCredential (tenant, kind, value) tekil indeksi (EF adlandırması).</summary>
    public const string UniqueIndexName = "ix_employee_terminal_credentials_tenant_id_kind_value";

    public async Task<Result> SaveAsync(TerminalCredentialKind kind, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueIndexName })
        {
            foreach (var entry in context.ChangeTracker.Entries<EmployeeTerminalCredential>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
            {
                entry.State = EntityState.Detached;
            }

            return TerminalCredentialRules.InUseBySomeoneElse(kind);
        }
    }
}
