namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Duyuru repository.
/// </summary>
public interface IAnnouncementRepository
{
    /// <summary>
    /// Duyuru ekle.
    /// </summary>
    Task AddAsync(Announcement announcement, CancellationToken ct = default);

    /// <summary>
    /// Duyuru ID ile getir.
    /// </summary>
    Task<Announcement?> GetByIdAsync(Guid tenantId, Guid announcementId, CancellationToken ct = default);

    /// <summary>
    /// Tüm duyuruları getir (tenant tarafından filtrelenmesi repository'de).
    /// </summary>
    Task<IReadOnlyList<Announcement>> GetAllAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Yayınlanmış duyuruları getir (geçerliler).
    /// </summary>
    Task<IReadOnlyList<Announcement>> GetPublishedAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Duyuru okunma kaydı repository.
/// </summary>
public interface IAnnouncementReadRecordRepository
{
    /// <summary>
    /// Okunma kaydı ekle.
    /// </summary>
    Task AddAsync(AnnouncementReadRecord record, CancellationToken ct = default);

    /// <summary>
    /// Çalışan'ın duyuru okunma kayıtlarını getir.
    /// </summary>
    Task<IReadOnlyList<AnnouncementReadRecord>> GetByEmployeeAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default);

    /// <summary>
    /// Duyurunun okunma kayıtlarını getir (analytics).
    /// </summary>
    Task<IReadOnlyList<AnnouncementReadRecord>> GetByAnnouncementAsync(
        Guid tenantId,
        Guid announcementId,
        CancellationToken ct = default);
}
