namespace Sense.Modules.OccupationalSafety.Domain.HazardClassifications;

/// <summary>
/// İş sağlığı ve güvenliği tehlike sınıfı (6331 sayılı Kanun md.9, "İş Sağlığı ve Güvenliğine İlişkin İşyeri
/// Tehlike Sınıfları Tebliği"): her işyeri NACE koduna göre Az tehlikeli/Tehlikeli/Çok tehlikeli sınıflarından
/// birine tabidir. Bu MVP'de sınıf tenant tarafından elle atanır (NACE eşleştirmesi kapsam dışı, roadmap Faz 2+).
/// </summary>
public enum HazardClass
{
    /// <summary>Az tehlikeli.</summary>
    Low = 1,

    /// <summary>Tehlikeli.</summary>
    Medium = 2,

    /// <summary>Çok tehlikeli.</summary>
    High = 3,
}
