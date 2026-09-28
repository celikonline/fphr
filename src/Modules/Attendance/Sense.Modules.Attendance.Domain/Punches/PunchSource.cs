namespace Sense.Modules.Attendance.Domain.Punches;

/// <summary>Where a raw <see cref="Punch"/> came from (spec §4). Mirrors <see cref="AttendanceDays.AttendanceDaySource"/> so a day's source can be derived from its punches.</summary>
public enum PunchSource
{
    /// <summary>İK/yönetici tarafından elle girilen okutma (mevcut Record/UpdateRecord akışı dahil).</summary>
    Manual = 0,

    /// <summary>Toplu Excel/PDKS içe aktarma satırı.</summary>
    Import = 1,

    /// <summary>PDKS cihaz günlüğü (CSV) içe aktarma.</summary>
    Device = 2,

    /// <summary>Çalışanın kendi QR/GPS okutması (S-31, <c>SelfCheckIn</c>).</summary>
    SelfService = 3,

    /// <summary>Onaylanmış eksik kayıt talebi (Z2.1, <c>PunchRequest</c>).</summary>
    Request = 4,

    /// <summary>Kayıtlı terminalden gelen okutma (Z5.1).</summary>
    Terminal = 5,
}
