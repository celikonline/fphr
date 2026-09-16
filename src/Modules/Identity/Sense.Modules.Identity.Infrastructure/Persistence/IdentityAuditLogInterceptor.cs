using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// TB-2/S-1 tur 1: <see cref="IAuditLogged"/> işaretli agregatlarda Added/Modified/Deleted için aynı SaveChanges'ta
/// (aynı transaction) bir <see cref="AuditLogEntry"/> üretir. Yalnız Identity'nin kendi DbContext'ine bağlanır
/// (<c>AddModuleDbContext</c>'in <c>configureAdditional</c> parametresiyle, bkz. IdentityModule), diğer modülleri
/// etkilemez. <see cref="AuditTenantInterceptor"/>'dan SONRA çalışacak şekilde kaydedilmelidir - soft-delete
/// dönüşümünü (Deleted → Modified, IsDeleted=true) o interceptor zaten uyguladığından burada "silme" durumu
/// entry.State üzerinden değil, IsDeleted alanının değişip değişmediğine bakılarak tespit edilir.
/// Alan bazlı diff YOK (TALIMAT'ın bu turki kararı) - Details yalnızca "{Resource} {action}" özetidir;
/// <see cref="IAuditLogged.SensitiveFields"/> (static virtual) bu turda hiç kullanılmaz.
/// </summary>
public sealed class IdentityAuditLogInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private const string SystemActor = "system";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var actor = currentUser.DisplayName ?? currentUser.Email ?? SystemActor;
        List<AuditLogEntry>? entries = null;

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is not IAuditLogged || entry.Entity is AuditLogEntry)
            {
                continue;
            }

            var action = DetermineAction(entry);
            if (action is null)
            {
                continue;
            }

            if (entry.Entity is not ITenantEntity tenantEntity)
            {
                continue;
            }

            entries ??= [];
            var resource = entry.Entity.GetType().Name;
            entries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantEntity.TenantId,
                OccurredAt = now,
                ActorUserId = currentUser.UserId,
                Actor = actor,
                Action = action,
                Resource = resource,
                ResourceId = TryGetResourceId(entry.Entity),
                Details = $"{resource} {action}",
            });
        }

        if (entries is { Count: > 0 })
        {
            context.Set<AuditLogEntry>().AddRange(entries);
        }
    }

    /// <summary>
    /// Create/Update/Delete eylemi. Soft-delete edilebilir bir agregat için <see cref="AuditTenantInterceptor"/>
    /// State'i zaten Deleted'dan Modified'a çevirmiş olur (IsDeleted=true) - burada gerçek niyeti (silme) IsDeleted
    /// alanının bu SaveChanges'ta değişip değişmediğine bakarak ayırt ediyoruz.
    /// </summary>
    private static string? DetermineAction(EntityEntry entry)
    {
        if (entry.Entity is ISoftDelete { IsDeleted: true } && entry.Property(nameof(ISoftDelete.IsDeleted)).IsModified)
        {
            return AuditActions.Delete;
        }

        return entry.State switch
        {
            EntityState.Added => AuditActions.Create,
            EntityState.Modified => AuditActions.Update,
            EntityState.Deleted => AuditActions.Delete,
            _ => null,
        };
    }

    /// <summary>"Id" property'sini reflection'la okur (bütün Entity&lt;TId&gt; alt sınıflarında var, TId serbest tip - bkz. Sense.Shared.Kernel.Domain.Entity&lt;TId&gt;).</summary>
    private static string? TryGetResourceId(object entity) =>
        entity.GetType().GetProperty(nameof(AuditLogEntry.Id))?.GetValue(entity)?.ToString();
}
