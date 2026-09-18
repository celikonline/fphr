using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
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
/// senseik-018 (E-01): the current user's context can be missing (e.g. a caller that runs SaveChanges without
/// CurrentUserAccessor.UseSystem() or an authenticated request), in which case it falls back to AnonymousUser and
/// Actor silently becomes the lowercase "system" placeholder below - indistinguishable from a deliberate SystemUser
/// (whose DisplayName is the capitalized <see cref="WellKnownRoles.System"/>) unless something is watching for it.
/// <c>logger</c> warns on that fallback so it surfaces in production instead of looking like a normal system record.
/// </summary>
public sealed class IdentityAuditLogInterceptor(
    ICurrentUser currentUser,
    IAuditSourceAccessor auditSource,
    TimeProvider clock,
    ILogger<IdentityAuditLogInterceptor> logger) : SaveChangesInterceptor
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
        if (actor == SystemActor)
        {
            logger.LogWarning(
                "Audit log entry recorded with no current user set (Actor fell back to \"{SystemActor}\"); " +
                "wrap the caller in CurrentUserAccessor.UseSystem() for background work, or confirm RequestContextMiddleware ran.",
                SystemActor);
        }

        var source = auditSource.Current ?? ResolveDefaultSource(actor);
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
                Source = source,
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

    /// <summary>
    /// Used when nothing called <see cref="IAuditSourceAccessor.BeginScope"/> for this SaveChanges (most call
    /// sites: RequestContextMiddleware already sets Web/Api explicitly, RunImportJobHandler sets Import). A
    /// deliberate SystemUser actor (capitalized, see <see cref="WellKnownRoles.System"/>) and the accidental
    /// lowercase fallback above both resolve to System - the fallback is a bug to fix at the call site, not a
    /// reason to also misreport its source.
    /// </summary>
    private string ResolveDefaultSource(string actor) =>
        actor == WellKnownRoles.System || actor == SystemActor ? AuditSources.System
        : currentUser.ApiKeyId is not null ? AuditSources.Api
        : AuditSources.Web;
}
