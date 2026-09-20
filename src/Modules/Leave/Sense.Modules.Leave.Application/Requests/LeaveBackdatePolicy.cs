using Sense.Modules.Identity.Contracts;

namespace Sense.Modules.Leave.Application.Requests;

/// <summary>
/// Ürün kararı (denetim A10): çalışanın kendi (self-servis) izin talebinde başlangıç tarihi, kiracının yerel bugününden
/// en fazla <see cref="SelfServiceMaxBackdateDays"/> gün geriye gidebilir. İK'nın çalışan adına "İzin Ekle" akışı
/// (bypass dahil) bu sınırı uygulamaz. Kiracı bazlı sayısal bir ayar deposu henüz yok; sınırın değiştirileceği tek yer
/// <see cref="SelfServiceMaxBackdateDays"/> sabitidir.
/// </summary>
public sealed class LeaveBackdatePolicy(TimeProvider clock, ITenantTimeZoneLookup? timeZones = null)
{
    /// <summary>Kiracı yerel bugününden geriye izin verilen azami gün sayısı.</summary>
    public const int SelfServiceMaxBackdateDays = 7;

    /// <summary>Bu tarihten (dahil) önce başlayan self-servis talep reddedilir.</summary>
    public async Task<DateOnly> EarliestSelfServiceStartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var zoneId = timeZones is null ? null : await timeZones.FindTimeZoneIdAsync(tenantId, cancellationToken);
        var zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return today.AddDays(-SelfServiceMaxBackdateDays);
    }
}
