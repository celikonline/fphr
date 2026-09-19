using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Domain.Tenants;

public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    Suspended = 2,
    Closed = 3,
}

/// <summary>Kiracı = müşteri şirket (hesap). Şirketin tüzel yapısı Organization modülünde.</summary>
public sealed class Tenant : AggregateRoot<Guid>
{
    private Tenant()
    {
    }

    private Tenant(Guid id, string slug, string name, DateTime? trialEndsAt, string locale, string timeZone) : base(id)
    {
        Slug = slug;
        Name = name;
        Status = TenantStatus.Trial;
        TrialEndsAt = trialEndsAt;
        Locale = locale;
        TimeZone = timeZone;
    }

    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? TaxNumber { get; private set; }

    public TenantStatus Status { get; private set; }

    public DateTime? TrialEndsAt { get; private set; }

    public string Locale { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = string.Empty;

    /// <summary>Kiracı ayarları (jsonb): 2FA politikası, parola politikası, oturum süresi vb.</summary>
    public string SettingsJson { get; private set; } = IdentityLimits.EmptyJsonObject;

    /// <summary>
    /// Erişim ayarları sayfası (jsonb, ayrı kolon): ana sayfa/hesap/takvim görünürlük anahtarları.
    /// KASITLI: <see cref="TenantAccessSettings.Default"/> paylaşılan tekil örneğini burada ATAMA — birden fazla
    /// Tenant aynı CLR referansını "owned" varlık olarak paylaşırsa EF Core change tracker'ı karışır
    /// (aynı anda birden çok kiracıya sahip aynı nesne, farklı TenantId gölge anahtarıyla izlenmeye çalışılır).
    /// Her örnek kendi (değer olarak eşit ama referans olarak farklı) kaydını alır.
    /// </summary>
    public TenantAccessSettings AccessSettings { get; private set; } = new();

    /// <summary>
    /// Güvenlik politikası sayfası (jsonb, ayrı kolon): zorunlu 2FA rol listesi (senseik-051).
    /// KASITLI: <see cref="TenantSecurityPolicy.Default"/> paylaşılan tekil örneğini burada ATAMA — <see cref="AccessSettings"/>
    /// ile aynı gerekçe (EF Core change tracker'ın paylaşılan "owned" referansla karışması).
    /// </summary>
    public TenantSecurityPolicy SecurityPolicy { get; private set; } = new();

    /// <summary>
    /// Modül aç/kapa ayarı (jsonb, ayrı kolon): kiracının kapattığı ürün modülleri (senseik-057).
    /// KASITLI: <see cref="TenantModuleSettings.Default"/> paylaşılan tekil örneğini burada ATAMA — <see cref="AccessSettings"/>
    /// ile aynı gerekçe (EF Core change tracker'ın paylaşılan "owned" referansla karışması).
    /// </summary>
    public TenantModuleSettings ModuleSettings { get; private set; } = new();

    public DateTime? SuspendedAt { get; private set; }

    public string? SuspendReason { get; private set; }

    /// <param name="trialDays">Deneme süresi (ayar); 0 → deneme yok, doğrudan Active.</param>
    public static Result<Tenant> Create(string slug, string name, DateTime nowUtc, int trialDays, string locale, string timeZone, Guid? id = null)
    {
        var s = Guard.NotEmpty(slug).ToLowerInvariant();
        if (!IsValidSlug(s))
        {
            return Error.Validation(IdentityDomainErrors.TenantInvalidSlug);
        }

        var tenant = new Tenant(
            id ?? Guid.CreateVersion7(),
            s,
            Guard.MaxLength(Guard.NotEmpty(name), IdentityLimits.CompanyNameMaxLength),
            trialDays > 0 ? nowUtc.AddDays(trialDays) : null,
            Guard.NotEmpty(locale),
            Guard.NotEmpty(timeZone));

        if (trialDays <= 0)
        {
            tenant.Status = TenantStatus.Active;
        }

        tenant.Raise(new TenantCreated(tenant.Id, tenant.Slug, tenant.Name));
        return tenant;
    }

    public static bool IsValidSlug(string slug) =>
        slug.Length is >= IdentityLimits.SlugMinLength and <= IdentityLimits.SlugMaxLength
        && slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == IdentityLimits.SlugSeparator)
        && !slug.StartsWith(IdentityLimits.SlugSeparator) && !slug.EndsWith(IdentityLimits.SlugSeparator);

    public void Rename(string name) => Name = Guard.MaxLength(Guard.NotEmpty(name), IdentityLimits.CompanyNameMaxLength);

    public void SetTaxNumber(string? taxNumber) => TaxNumber = string.IsNullOrWhiteSpace(taxNumber) ? null : taxNumber.Trim();

    public void SetLocale(string locale, string timeZone)
    {
        Locale = Guard.NotEmpty(locale);
        TimeZone = Guard.NotEmpty(timeZone);
    }

    public void UpdateSettings(string settingsJson) => SettingsJson = Guard.NotEmpty(settingsJson);

    /// <summary>Erişim ayarları sayfası "Kaydet" eylemi: 11 anahtar birlikte, anında etkili biçimde değiştirilir.</summary>
    public void UpdateAccessSettings(TenantAccessSettings settings) => AccessSettings = settings;

    /// <summary>Güvenlik politikası sayfası "Kaydet" eylemi: zorunlu 2FA rol listesi anında etkili biçimde değiştirilir.</summary>
    public void UpdateSecurityPolicy(TenantSecurityPolicy policy) => SecurityPolicy = policy;

    /// <summary>Modüller sayfası "Kaydet" eylemi: kapalı modül listesi anında etkili biçimde değiştirilir.</summary>
    public void UpdateModuleSettings(TenantModuleSettings settings) => ModuleSettings = settings;

    public Result Activate()
    {
        if (Status == TenantStatus.Closed)
        {
            return Error.Rule(IdentityDomainErrors.TenantClosed);
        }

        Status = TenantStatus.Active;
        SuspendedAt = null;
        SuspendReason = null;
        Raise(new TenantStatusChanged(Id, Status));
        return Result.Success();
    }

    public Result Suspend(string reason, DateTime nowUtc)
    {
        if (Status == TenantStatus.Closed)
        {
            return Error.Rule(IdentityDomainErrors.TenantClosed);
        }

        Status = TenantStatus.Suspended;
        SuspendedAt = nowUtc;
        SuspendReason = Guard.MaxLength(Guard.NotEmpty(reason), IdentityLimits.ReasonMaxLength);
        Raise(new TenantStatusChanged(Id, Status));
        return Result.Success();
    }

    public void Close()
    {
        Status = TenantStatus.Closed;
        Raise(new TenantStatusChanged(Id, Status));
    }

    public bool IsWritable => Status is TenantStatus.Trial or TenantStatus.Active;
}

public sealed record TenantCreated(Guid TenantId, string Slug, string Name) : DomainEvent;

public sealed record TenantStatusChanged(Guid TenantId, TenantStatus Status) : DomainEvent;
