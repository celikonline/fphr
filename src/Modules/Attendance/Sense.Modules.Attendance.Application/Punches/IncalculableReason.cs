namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Hesaplanamayan günün (<c>AttendanceAnomaly.Incalculable</c>) açıklaması (spec §5.1) - kullanıcıya "neden" ipucu verir; kalıcı değildir,
/// günün okutmalarından sorgu anında türetilir (<see cref="DayFromPunches"/>).
/// </summary>
public enum IncalculableReason
{
    None = 0,

    /// <summary>Tek sayıda okutma içinde yönsüz (Unknown) okutma var.</summary>
    UnknownDirection = 1,

    /// <summary>Yön sırası bozuk (In,In / ilk kayıt Out / tek sayı) - tek kanaldan gelen okutmalarda.</summary>
    BrokenSequence = 2,

    /// <summary>Kanal çakışması: gün düzeyi bir kayıt (elle / Excel) ile okutma kanalının (cihaz, self-servis, terminal…) okutmaları aynı günde.</summary>
    ChannelConflict = 3,

    /// <summary>Okutma var ama hiç plan yok (ne vardiya ataması ne çalışma çizelgesi).</summary>
    NoPlan = 4,

    /// <summary>Gece vardiyası çakışması: okutma iki vardiya başlangıcına eşit uzaklıkta.</summary>
    NightShiftOverlap = 5,
}
