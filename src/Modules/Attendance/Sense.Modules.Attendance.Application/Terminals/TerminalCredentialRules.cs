using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Terminal kimliği tanımlama (Z5.4) hata kodları; çevirileri SharedResource{,.en}.resx'te. Hiçbiri kimlik değerini ya da sahibini taşımaz.</summary>
public static class TerminalCredentialErrors
{
    /// <summary>Sicil numarası (ya da çalışan kimliği) kiracıda hiçbir çalışana eşlenmedi.</summary>
    public const string EmployeeNotFound = "attendance.terminal_credential.employee_not_found";

    /// <summary>Çalışan işten ayrılmış: yeni kimlik tanımlanamaz.</summary>
    public const string EmployeeNotActive = "attendance.terminal_credential.employee_not_active";

    /// <summary>PIN yalnız rakamlardan oluşmalı ve <see cref="TerminalCredentialRules.PinMinLength"/>–<see cref="TerminalCredentialRules.PinMaxLength"/> haneli olmalı.</summary>
    public const string PinInvalid = "attendance.terminal_credential.pin_invalid";

    /// <summary>Kart numarası/dış kimlik en çok <see cref="TerminalCredentialRules.ValueMaxLength"/> karakter.</summary>
    public const string ValueTooLong = "attendance.terminal_credential.value_too_long";

    /// <summary>İçe aktarmada tür sütunu Kart / PIN / Harici değil (hücre değeri iletiye yazılmaz: yanlış sütuna yazılmış bir PIN sızmasın).</summary>
    public const string KindInvalid = "attendance.terminal_credential.kind_invalid";

    /// <summary>
    /// PIN atanamadı (genel ret). Başka çalışanda aktif PIN için <see cref="AttendanceDomainErrors.TerminalCredentialInUse"/> yerine kullanılır: düşük
    /// entropili PIN'in kiracıda "başkasında var" olduğunu açıkça söylememek için (T1 güvenlik incelemesi, kaba kuvvet riski). Kart/harici için in_use kalır.
    /// </summary>
    public const string Rejected = "attendance.terminal_credential.rejected";
}

/// <summary>Kimliğin bir çalışana bağlanma sonucu.</summary>
public enum TerminalCredentialAssignment
{
    /// <summary>Yeni kimlik kaydı açıldı.</summary>
    Created,

    /// <summary>Pasif kimlik bu çalışana bağlanıp etkinleştirildi (başka çalışandan devralma ya da aynı çalışanda yeniden etkinleştirme).</summary>
    Reassigned,

    /// <summary>Kimlik zaten bu çalışanda aktif (idempotent tekrar).</summary>
    Unchanged,
}

/// <summary>
/// Kimlik ekleme ucu ile Excel içe aktarmanın (Z5.4) ortak kuralları: biçim denetimi ve tekillik (kiracı, tür, özet). Aynı çalışana aynı kimlik
/// → değişiklik yok; başka çalışanda AKTİF kimlik → <see cref="AttendanceDomainErrors.TerminalCredentialInUse"/> (kart/harici) ya da PIN'de genel
/// <see cref="TerminalCredentialErrors.Rejected"/> (sahip bilgisi hiçbirinde dönmez); başka
/// çalışanda PASİF kimlik → <see cref="EmployeeTerminalCredential.Reassign"/> (T1 kararı: tekil indeks pasifleri de kapsar, yeni kayıt açılmaz).
/// </summary>
public static class TerminalCredentialRules
{
    /// <summary>T1 güvenlik incelemesi (kaba kuvvet): PIN en az 6 hane.</summary>
    public const int PinMinLength = 6;
    public const int PinMaxLength = 8;
    public const int ValueMaxLength = 64;

    /// <summary>Kırpılmış ham değerin biçim hatası (hata kodu) ya da geçerliyse null.</summary>
    public static string? FormatError(TerminalCredentialKind kind, string trimmedValue) => kind switch
    {
        TerminalCredentialKind.Pin => IsPin(trimmedValue) ? null : TerminalCredentialErrors.PinInvalid,
        _ => trimmedValue.Length > ValueMaxLength ? TerminalCredentialErrors.ValueTooLong : null,
    };

    /// <summary>
    /// <paramref name="digest"/> özetli kimliği çalışana bağlar. <paramref name="existing"/>: aynı (kiracı, tür, özet) ile kayıtlı kimlik (aktif ya da
    /// pasif) veya yoksa null. Yeni kayıt depoya eklenir; kaydetme çağırandadır.
    /// </summary>
    public static Result<(TerminalCredentialAssignment Outcome, EmployeeTerminalCredential Credential)> Assign(
        ITerminalRepository terminals, Guid tenantId, Guid employeeId, TerminalCredentialKind kind, string digest, EmployeeTerminalCredential? existing)
    {
        if (existing is null)
        {
            var created = EmployeeTerminalCredential.Create(tenantId, employeeId, kind, digest);
            if (created.IsFailure)
            {
                return created.Error;
            }

            terminals.AddCredential(created.Value);
            return (TerminalCredentialAssignment.Created, created.Value);
        }

        if (existing.IsActive)
        {
            return existing.EmployeeId == employeeId ? (TerminalCredentialAssignment.Unchanged, existing) : InUseBySomeoneElse(kind);
        }

        var reassigned = existing.Reassign(employeeId);
        return reassigned.IsFailure ? reassigned.Error : (TerminalCredentialAssignment.Reassigned, existing);
    }

    private static Error InUseBySomeoneElse(TerminalCredentialKind kind) =>
        kind == TerminalCredentialKind.Pin ? Error.Rule(TerminalCredentialErrors.Rejected) : Error.Conflict(AttendanceDomainErrors.TerminalCredentialInUse);

    private static bool IsPin(string value) => value.Length is >= PinMinLength and <= PinMaxLength && value.All(char.IsAsciiDigit);
}
