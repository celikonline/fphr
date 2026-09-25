namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Terminal yönetimi ve okutma ucu hata kodları (Z5.1); çevirileri SharedResource{,.en}.resx'te.</summary>
public static class TerminalErrors
{
    /// <summary>Kiracının başka bir terminali aynı kodu kullanıyor.</summary>
    public const string CodeDuplicate = "attendance.terminal.code_duplicate";

    /// <summary>Terminale bağlanmak istenen lokasyon kiracıda yok ya da pasif.</summary>
    public const string LocationNotFound = "attendance.terminal.location_not_found";

    /// <summary>Gruba bağlı terminal varken grup silinemez.</summary>
    public const string GroupInUse = "attendance.terminal_group.in_use";

    /// <summary>Okutma isteği 1..<see cref="TerminalPunchIngest.MaxItemsPerRequest"/> kalem taşımalı.</summary>
    public const string BatchSizeInvalid = "attendance.terminal_ingest.batch_size_invalid";

    /// <summary>
    /// Kimlik özeti pepper'ı (Attendance:Terminals:CredentialPepper) yapılandırılmamış: kimlik ekleme/içe aktarma/okutma çözümü 503 döner;
    /// pepper gerektirmeyen uçlar (terminal/grup yönetimi, anahtar, nabız) çalışır. İleti yapılandırma ayrıntısı içermez.
    /// </summary>
    public const string CredentialPepperMissing = "attendance.terminal_credential.pepper_missing";
}

/// <summary>Okutma ucunun kalem bazlı red nedenleri (makine okur; çevrilmez).</summary>
public static class TerminalPunchRejectReasons
{
    /// <summary>Kimlik (kart/PIN/dış kimlik) terminalin kiracısında aktif bir kimliğe eşlenmedi.</summary>
    public const string UnknownCredential = "unknown_credential";

    /// <summary>Kimlik doğru ama çalışan aktif değil (işten ayrılmış, askıda, taslak ya da kaydı yok).</summary>
    public const string EmployeeNotActive = "employee_not_active";

    /// <summary>Kalem eksik/bozuk: kimlik türü/değeri ya da okutma anı yok.</summary>
    public const string InvalidItem = "invalid_item";

    /// <summary>Okutmanın ayı onaylı aylık puantajla kapalı (K6).</summary>
    public const string PeriodLocked = "period_locked";

    /// <summary>Recorder okutmayı ne kabul etti ne yoksaydı ne reddetti (beklenmez; sessizce "kabul" sayılmaz).</summary>
    public const string NotRecorded = "not_recorded";
}
