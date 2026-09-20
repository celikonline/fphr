using Sense.Modules.Announcements.Domain;
using Sense.Modules.Organization.Domain.OrgUnits;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    /// <summary>
    /// A handful of manual company calendar entries (senseik-027, roadmap #71/P-19; merge_gate: "demo seed'de birkaç
    /// etkinlik türü görünüyor"). Birthday and leave-start entries need no seeding here - they are derived at query
    /// time from the demo employees' Employee.BirthDate and from SeedDemoLeaveAsync's upcoming annual leave for
    /// colleagues, both already part of the Demo profile.
    /// </summary>
    private void SeedDemoCompanyEvents(Guid tenantId, Guid createdByUserId, IReadOnlyList<OrgUnit> departments)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        AddCompanyEventOrThrow(tenantId, "Yıl Sonu Kutlaması", "Tüm şirket daveti - ofiste kutlama.",
            today.AddDays(30), CompanyEventType.CompanyEvent, visibilityOrgUnitId: null, createdByUserId);

        AddCompanyEventOrThrow(tenantId, "Aylık Genel Toplantı", "Şirket geneli bilgilendirme toplantısı.",
            today.AddDays(7), CompanyEventType.Meeting, visibilityOrgUnitId: null, createdByUserId);

        var engineering = departments.FirstOrDefault(d => d.Name == "Yazılım Geliştirme");
        if (engineering is not null)
        {
            AddCompanyEventOrThrow(tenantId, "Sprint Planlama", "Yazılım Geliştirme ekibi sprint planlama toplantısı.",
                today.AddDays(3), CompanyEventType.Meeting, engineering.Id, createdByUserId);
        }
    }

    private void AddCompanyEventOrThrow(
        Guid tenantId, string title, string? description, DateOnly eventDate, CompanyEventType type,
        Guid? visibilityOrgUnitId, Guid createdByUserId)
    {
        var result = CompanyEvent.Create(tenantId, title, description, eventDate, type, visibilityOrgUnitId, createdByUserId);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed demo company event '{title}': {result.Error}");
        }

        announcementsCtx.CompanyEvents.Add(result.Value);
    }
}
