using System.Globalization;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Identity.Domain.Users;

public enum UserStatus
{
    /// <summary>Davet edildi, parola belirlemedi.</summary>
    Invited = 0,
    Active = 1,
    Locked = 2,
    Disabled = 3,
}

/// <summary>Kilitleme politikası (ayarlardan gelir; domain değerleri parametre olarak alır).</summary>
public readonly record struct LockoutPolicy(int MaxFailedAttempts, TimeSpan Duration);

/// <summary>Giriş yapabilen kullanıcı. Çalışan kaydı (Employee modülü) ile 1:1 bağ EmployeeId üzerinden.</summary>
public sealed class User : TenantAggregateRoot<Guid>, ISoftDelete, IAuditLogged, IAnonymizable
{
    private readonly List<UserRole> _roles = [];
    private readonly List<UserPermissionOverride> _permissionOverrides = [];

    private User()
    {
    }

    private User(Guid id, Guid tenantId, EmailAddress email, string firstName, string lastName, string preferredLanguage) : base(id, tenantId)
    {
        Email = email.Value;
        NormalizedEmail = email.Value.ToUpperInvariant();
        FirstName = firstName;
        LastName = lastName;
        Status = UserStatus.Invited;
        SecurityStamp = NewStamp();
        PreferredLanguage = preferredLanguage;
    }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public bool EmailConfirmed { get; private set; }

    public string? PasswordHash { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public UserStatus Status { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public bool MfaEnabled { get; private set; }

    /// <summary>Şifrelenmiş TOTP gizli anahtarı (Infrastructure IEncryptionProvider).</summary>
    public string? TotpSecretEncrypted { get; private set; }

    /// <summary>Parola/2FA/rol değişiminde yenilenir; refresh token ve izin cache'ini geçersiz kılar.</summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    public int FailedAccessCount { get; private set; }

    public DateTime? LockoutEndUtc { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    public string PreferredLanguage { get; private set; } = string.Empty;

    /// <summary>Sense (platform) ekibi hesabı; kiracı verisine varsayılan erişimi yoktur, yalnız onaylı ImpersonationGrant ile geçici oturum açar.</summary>
    public bool IsPlatformStaff { get; private set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedUserId { get; set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public IReadOnlyCollection<UserPermissionOverride> PermissionOverrides => _permissionOverrides.AsReadOnly();

    public string FullName => string.Concat(FirstName, " ", LastName).Trim();

    public static IReadOnlySet<string> SensitiveFields { get; } =
        new HashSet<string>([nameof(PasswordHash), nameof(TotpSecretEncrypted), nameof(SecurityStamp)], StringComparer.Ordinal);

    public static Result<User> Create(Guid tenantId, string email, string firstName, string lastName, string preferredLanguage, string? phone = null)
    {
        if (!EmailAddress.IsValid(email))
        {
            return Error.Validation(IdentityDomainErrors.UserInvalidEmail);
        }

        var user = new User(
            Guid.CreateVersion7(),
            tenantId,
            EmailAddress.Of(email),
            Guard.MaxLength(Guard.NotEmpty(firstName), IdentityLimits.NameMaxLength),
            Guard.MaxLength(Guard.NotEmpty(lastName), IdentityLimits.NameMaxLength),
            Guard.NotEmpty(preferredLanguage))
        {
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        };
        user.Raise(new UserCreated(user.Id, tenantId, user.Email));
        return user;
    }

    /// <summary>Davet kabulü veya parola sıfırlama: parola belirlenir, hesap aktifleşir.</summary>
    public void SetPassword(string passwordHash)
    {
        PasswordHash = Guard.NotEmpty(passwordHash);
        SecurityStamp = NewStamp();
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        if (Status is UserStatus.Invited or UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }

        EmailConfirmed = true;
    }

    public Result CanSignIn(DateTime nowUtc)
    {
        if (IsDeleted || Status == UserStatus.Disabled)
        {
            return Error.Unauthorized(IdentityDomainErrors.UserDisabled);
        }

        if (Status == UserStatus.Invited || PasswordHash is null)
        {
            return Error.Unauthorized(IdentityDomainErrors.UserNotActivated);
        }

        if (LockoutEndUtc is { } end && end > nowUtc)
        {
            var minutes = Math.Ceiling((end - nowUtc).TotalMinutes).ToString(CultureInfo.InvariantCulture);
            return Error.Unauthorized(IdentityDomainErrors.LockedOut, (IdentityDomainErrors.Args.Minutes, minutes));
        }

        return Result.Success();
    }

    public void RecordFailedAccess(DateTime nowUtc, LockoutPolicy policy)
    {
        FailedAccessCount++;
        if (FailedAccessCount >= policy.MaxFailedAttempts)
        {
            LockoutEndUtc = nowUtc.Add(policy.Duration);
            FailedAccessCount = 0;
            Raise(new UserLockedOut(Id, TenantId, LockoutEndUtc.Value));
        }
    }

    public void RecordSuccessfulLogin(DateTime nowUtc)
    {
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        LastLoginAt = nowUtc;
    }

    public void UpdateProfile(string firstName, string lastName, string? phone, string? preferredLanguage)
    {
        FirstName = Guard.MaxLength(Guard.NotEmpty(firstName), IdentityLimits.NameMaxLength);
        LastName = Guard.MaxLength(Guard.NotEmpty(lastName), IdentityLimits.NameMaxLength);
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            PreferredLanguage = preferredLanguage.Trim().ToLowerInvariant();
        }
    }

    /// <summary>FG-001: kullanıcının kendi dil tercihi (iki harfli kod, küçük harfe normalize edilir). Desteklenen dil
    /// denetimi yapılandırmaya bağlı olduğu için Application'dadır (<c>ChangeMyLanguageHandler</c>).</summary>
    public void ChangePreferredLanguage(string language) => PreferredLanguage = Guard.NotEmpty(language).Trim().ToLowerInvariant();

    public Result ChangeEmail(string email)
    {
        if (!EmailAddress.IsValid(email))
        {
            return Error.Validation(IdentityDomainErrors.UserInvalidEmail);
        }

        var e = EmailAddress.Of(email);
        Email = e.Value;
        NormalizedEmail = e.Value.ToUpperInvariant();
        EmailConfirmed = false;
        return Result.Success();
    }

    public void LinkEmployee(Guid employeeId)
    {
        EmployeeId = employeeId;
        Raise(new UserLinkedToEmployee(Id, TenantId, employeeId));
    }

    public void UnlinkEmployee() => EmployeeId = null;

    /// <summary>Platform (Sense ekibi) bayrağı: yalnız impersonation isteği/oturumu açma yetkisinin ön koşuludur, izin vermez.</summary>
    public void SetPlatformStaff(bool isPlatformStaff) => IsPlatformStaff = isPlatformStaff;

    public void Disable()
    {
        Status = UserStatus.Disabled;
        SecurityStamp = NewStamp();
        Raise(new UserStatusChanged(Id, TenantId, Status));
    }

    public Result Enable()
    {
        if (PasswordHash is null)
        {
            return Error.Rule(IdentityDomainErrors.UserNoPassword);
        }

        Status = UserStatus.Active;
        LockoutEndUtc = null;
        FailedAccessCount = 0;
        Raise(new UserStatusChanged(Id, TenantId, Status));
        return Result.Success();
    }

    public void Unlock()
    {
        LockoutEndUtc = null;
        FailedAccessCount = 0;
        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
        }
    }

    /// <summary>Enrollment 1. adım: secret şifrelenip yazılır ama <see cref="MfaEnabled"/> henüz açılmaz — kullanıcı
    /// kodu doğrulayana kadar (<see cref="EnableMfa"/>) devre dışı kalır.</summary>
    public void BeginMfaEnrollment(string totpSecretEncrypted) => TotpSecretEncrypted = Guard.NotEmpty(totpSecretEncrypted);

    public void EnableMfa(string totpSecretEncrypted)
    {
        TotpSecretEncrypted = Guard.NotEmpty(totpSecretEncrypted);
        MfaEnabled = true;
        SecurityStamp = NewStamp();
    }

    public void DisableMfa()
    {
        TotpSecretEncrypted = null;
        MfaEnabled = false;
        SecurityStamp = NewStamp();
    }

    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        var target = roleIds.Distinct().ToHashSet();
        _roles.RemoveAll(r => !target.Contains(r.RoleId));
        foreach (var roleId in target.Where(id => _roles.All(r => r.RoleId != id)))
        {
            _roles.Add(new UserRole(Id, roleId));
        }

        SecurityStamp = NewStamp();
        Raise(new UserRolesChanged(Id, TenantId));
    }

    public bool HasRole(Guid roleId) => _roles.Any(r => r.RoleId == roleId);

    public void SetPermissionOverride(string permission, bool allow)
    {
        var key = Guard.NotEmpty(permission);
        _permissionOverrides.RemoveAll(p => p.Permission == key);
        _permissionOverrides.Add(new UserPermissionOverride(Id, key, allow));
        Raise(new UserRolesChanged(Id, TenantId));
    }

    public void RemovePermissionOverride(string permission)
    {
        _permissionOverrides.RemoveAll(p => p.Permission == permission);
        Raise(new UserRolesChanged(Id, TenantId));
    }

    /// <summary>KVKK: kişisel veriler silinir, kimlik izi ve denetim kayıtları korunur.</summary>
    public void Anonymize()
    {
        var suffix = Id.ToString("N")[..IdentityLimits.AnonymizedSuffixLength];
        Email = string.Format(CultureInfo.InvariantCulture, IdentityLimits.AnonymizedEmailFormat, suffix);
        NormalizedEmail = Email.ToUpperInvariant();
        FirstName = string.Empty;
        LastName = suffix;
        Phone = null;
        PasswordHash = null;
        TotpSecretEncrypted = null;
        MfaEnabled = false;
        Status = UserStatus.Disabled;
        SecurityStamp = NewStamp();
        IsDeleted = true;
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}

public sealed class UserRole : AuditableRecord
{
    private UserRole()
    {
    }

    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }
}

public sealed class UserPermissionOverride : AuditableRecord
{
    private UserPermissionOverride()
    {
    }

    internal UserPermissionOverride(Guid userId, string permission, bool allow)
    {
        UserId = userId;
        Permission = permission;
        Allow = allow;
    }

    public Guid UserId { get; private set; }

    public string Permission { get; private set; } = string.Empty;

    public bool Allow { get; private set; }
}

public sealed record UserCreated(Guid UserId, Guid TenantId, string Email) : DomainEvent;

public sealed record UserLockedOut(Guid UserId, Guid TenantId, DateTime LockoutEndUtc) : DomainEvent;

public sealed record UserLinkedToEmployee(Guid UserId, Guid TenantId, Guid EmployeeId) : DomainEvent;

public sealed record UserStatusChanged(Guid UserId, Guid TenantId, UserStatus Status) : DomainEvent;

public sealed record UserRolesChanged(Guid UserId, Guid TenantId) : DomainEvent;
