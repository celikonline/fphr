using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Domain;

namespace Sense.Shared.Infrastructure.Persistence.Audit;

/// <summary>
/// senseik-049: Identity'nin IdentityAuditLogInterceptor'ının (senseik-018/TB-2 tur 1) genelleştirilmiş hali - herhangi
/// bir <see cref="ModuleDbContext"/>'e bağlanabilir (bkz. o modülün *Module.cs'i, IdentityModule'deki desenin aynısı).
/// <see cref="IAuditLogged"/> işaretli agregatlarda Added/Modified/Deleted için aynı SaveChanges'ta (aynı transaction)
/// bir <see cref="AuditLogEntry"/> üretir. <see cref="AuditTenantInterceptor"/>'dan SONRA çalışacak şekilde
/// kaydedilmelidir - soft-delete dönüşümünü (Deleted → Modified, IsDeleted=true) o interceptor zaten uyguladığından
/// burada "silme" durumu entry.State üzerinden değil, IsDeleted alanının değişip değişmediğine bakılarak tespit edilir.
/// Identity'nin kendi AuditLogEntry/IdentityAuditLogInterceptor çifti kasıtlı olarak BU sınıfa taşınmadı (bkz.
/// AuditLogEntry.cs'in doc comment'i - migration/veri kaybı riski); bu yüzden Identity'nin User/Role agregatları hâlâ
/// eski yolu kullanır, bu interceptor Employee/Leave/Workflow (ve ileride katılacak modüller) içindir.
/// Alan bazlı diff (senseik-049, <see cref="IAuditLogged.AuditedFields"/>): yalnız Modified aksiyonunda ve yalnız o
/// agregatın listelediği alanlarda uygulanır - listelenmemiş bir alan hiç diff'e girmez, boş liste eski davranışa
/// ("{Resource} {action}") döner. <see cref="IAuditLogged.SensitiveFields"/> bu turda da kullanılmaz (Details zaten
/// tamamıyla identity.audit.sensitive_view arkasında maskelenir - bkz. GetAuditLogHandler).
/// </summary>
public sealed class AuditLogInterceptor(
    ICurrentUser currentUser,
    IAuditSourceAccessor auditSource,
    TimeProvider clock,
    ILogger<AuditLogInterceptor> logger) : SaveChangesInterceptor
{
    private const string SystemActor = "system";

    /// <summary>Bir entity tipinin (nadiren değişen) AuditedFields listesi - her SaveChanges'ta reflection tekrarlamamak için.</summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<string>> AuditedFieldsCache = new();

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
                Details = BuildDetails(entry, resource, action),
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

    /// <summary>
    /// senseik-049: action=update ve agregat AuditedFields listeliyorsa "Resource update: Alan: eski → yeni; ..."
    /// - yalnız gerçekten IsModified olan alanlar listelenir (Modified state'teki bir entity'nin izlenmeyen başka
    /// alanları da olabilir). Hiçbiri değişmemişse (yalnız izlenmeyen alanlar değiştiyse) eski özet formatına döner.
    /// </summary>
    private static string BuildDetails(EntityEntry entry, string resource, string action)
    {
        if (action != AuditActions.Update)
        {
            return $"{resource} {action}";
        }

        var auditedFields = GetAuditedFields(entry.Entity.GetType());
        if (auditedFields.Count == 0)
        {
            return $"{resource} {action}";
        }

        List<string>? changes = null;
        foreach (var field in auditedFields)
        {
            var property = entry.Metadata.FindProperty(field);
            if (property is null)
            {
                continue;
            }

            var propertyEntry = entry.Property(field);
            if (!propertyEntry.IsModified)
            {
                continue;
            }

            changes ??= [];
            changes.Add($"{field}: {FormatValue(propertyEntry.OriginalValue)} → {FormatValue(propertyEntry.CurrentValue)}");
        }

        return changes is { Count: > 0 }
            ? Truncate($"{resource} {action}: {string.Join("; ", changes)}")
            : $"{resource} {action}";
    }

    /// <summary>
    /// Serbest metin alanları (ör. LeaveRequest.CancellationReason) diff'i details kolonunun sınırını aşırabilir -
    /// aşarsa SaveChanges'ın tamamı (asıl iş işlemi dahil) DB hatasıyla düşerdi; bunun yerine kesip "…" ekliyoruz.
    /// </summary>
    private static string Truncate(string details) =>
        details.Length <= AuditLogTables.DetailsMaxLength
            ? details
            : string.Concat(details.AsSpan(0, AuditLogTables.DetailsMaxLength - 1), "…");

    private static string FormatValue(object? value) => value switch
    {
        null => "—",
        DateOnly d => d.ToString("yyyy-MM-dd"),
        DateTime d => d.ToString("yyyy-MM-dd"),
        DateTimeOffset d => d.ToString("yyyy-MM-dd"),
        _ => value.ToString() ?? "—",
    };

    /// <summary>
    /// <see cref="IAuditLogged.AuditedFields"/> static virtual üyesini, agregatın CLR tipine göre okur (implicit
    /// implementasyon - bkz. Employee/LeaveRequest/OvertimeRequest/ApprovalRequest - "public static IReadOnlyList
    /// &lt;string&gt; AuditedFields" adında bir static property tanımlar, tıpkı User.SensitiveFields örneğindeki gibi).
    /// Override etmeyen tipler için doğrudan boş liste döner (interface'in kendi varsayımıyla aynı sonuç) - static
    /// virtual bir interface üyesini yalnız CLR Type'tan (generic olmadan) çağırmanın pratik yolu budur.
    /// </summary>
    private static IReadOnlyList<string> GetAuditedFields(Type entityType) =>
        AuditedFieldsCache.GetOrAdd(entityType, static t =>
            t.GetProperty(nameof(IAuditLogged.AuditedFields), BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                as IReadOnlyList<string> ?? []);

    /// <summary>"Id" property'sini reflection'la okur (bütün Entity&lt;TId&gt; alt sınıflarında var, TId serbest tip).</summary>
    private static string? TryGetResourceId(object entity) =>
        entity.GetType().GetProperty(nameof(AuditLogEntry.Id))?.GetValue(entity)?.ToString();

    /// <summary>
    /// Used when nothing called <see cref="IAuditSourceAccessor.BeginScope"/> for this SaveChanges. A deliberate
    /// SystemUser actor (capitalized, see <see cref="WellKnownRoles.System"/>) and the accidental lowercase fallback
    /// above both resolve to System.
    /// </summary>
    private string ResolveDefaultSource(string actor) =>
        actor == WellKnownRoles.System || actor == SystemActor ? AuditSources.System
        : currentUser.ApiKeyId is not null ? AuditSources.Api
        : AuditSources.Web;
}
