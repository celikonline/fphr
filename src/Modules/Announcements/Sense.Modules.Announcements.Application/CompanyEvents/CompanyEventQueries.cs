using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

/// <summary>
/// Şirket etkinlik takvimi (senseik-027, roadmap #71/P-19): [From, To] aralığındaki manuel etkinlikler (görünürlük
/// filtrelenmiş) + doğum günleri (Employee.BirthDate'ten türetilen) + izin başlangıçları (Leave'in onaylı
/// taleplerinden türetilen, hassas türler hariç) birleşik olarak döner. OrgUnitId verilirse yalnız o birime açık
/// (veya tenant-geneli) etkinlikler döner; verilmezse çağıranın kendi birimi kullanılır. MVP basitleştirmesi:
/// görünürlük tam eşleşme ile kontrol edilir, alt birim ağacı gezilmez (bkz. handoff notu). Çok günlü etkinlikler
/// aralıkla kesişiyorsa döner (E4); Date başlangıç gününü verir.
/// </summary>
[RequiresPermission("announcements.my.events")]
public sealed record GetCompanyCalendarQuery(DateOnly From, DateOnly To, Guid? OrgUnitId) : IQuery<IReadOnlyList<CompanyCalendarEntryDto>>;

public sealed class GetCompanyCalendarHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    ICompanyEventRepository events,
    IEmployeeDirectory employeeDirectory,
    IEmployeeContactLookup employeeContacts,
    IEmployeeBirthDateLookup birthDates,
    IEmployeeLookup employeeLookup,
    ILeaveStartDateReader leaveStarts)
    : IQueryHandler<GetCompanyCalendarQuery, IReadOnlyList<CompanyCalendarEntryDto>>
{
    public async Task<Result<IReadOnlyList<CompanyCalendarEntryDto>>> Handle(GetCompanyCalendarQuery query, CancellationToken cancellationToken)
    {
        var range = CompanyEventCalendar.ValidateRange(query.From, query.To);
        if (range.IsFailure)
        {
            return range.Error;
        }

        var visibilityOrgUnitId = query.OrgUnitId ?? await CallerOrgUnitIdAsync(cancellationToken);

        var manualEntries = await BuildManualEntriesAsync(query, visibilityOrgUnitId, cancellationToken);
        var birthdayEntries = await BuildBirthdayEntriesAsync(query, cancellationToken);
        var leaveStartEntries = await BuildLeaveStartEntriesAsync(query, cancellationToken);

        IReadOnlyList<CompanyCalendarEntryDto> merged = manualEntries
            .Concat(birthdayEntries)
            .Concat(leaveStartEntries)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Result.Success(merged);
    }

    private async Task<Guid?> CallerOrgUnitIdAsync(CancellationToken cancellationToken) =>
        currentUser.EmployeeId is { } employeeId
            ? await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken)
            : null;

    private async Task<IReadOnlyList<CompanyCalendarEntryDto>> BuildManualEntriesAsync(
        GetCompanyCalendarQuery query, Guid? visibilityOrgUnitId, CancellationToken cancellationToken)
    {
        var all = await events.GetInRangeAsync(tenant.TenantId, query.From, query.To, cancellationToken);
        return all
            .Where(e => e.VisibilityOrgUnitId is null || e.VisibilityOrgUnitId == visibilityOrgUnitId)
            .Select(e => new CompanyCalendarEntryDto(
                e.Id, e.Title, e.Description, e.EventDate, e.Type, e.VisibilityOrgUnitId, EmployeeId: null, e.CreatedByUserId,
                e.StartsAt, e.EndsAt, e.IsAllDay, e.Mode, e.Location, e.OnlineUrl, e.CategoryId,
                e.Invitees.Select(i => i.EmployeeId).ToList()))
            .ToList();
    }

    private async Task<IReadOnlyList<CompanyCalendarEntryDto>> BuildBirthdayEntriesAsync(GetCompanyCalendarQuery query, CancellationToken cancellationToken)
    {
        var activeEmployeeIds = await employeeDirectory.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
        if (activeEmployeeIds.Count == 0)
        {
            return [];
        }

        var birthDatesByEmployeeId = await birthDates.GetBirthDatesAsync(tenant.TenantId, activeEmployeeIds, cancellationToken);
        var derived = CompanyEventCalendar.DeriveBirthdays(birthDatesByEmployeeId, query.From, query.To);
        if (derived.Count == 0)
        {
            return [];
        }

        var names = await NamesAsync(derived.Select(d => d.EmployeeId).Distinct().ToList(), cancellationToken);
        return derived
            .Select(d => new CompanyCalendarEntryDto(
                Id: null, names.GetValueOrDefault(d.EmployeeId, string.Empty), Description: null, d.Date,
                CompanyEventType.Birthday, VisibilityOrgUnitId: null, d.EmployeeId, CreatedByUserId: null))
            .ToList();
    }

    private async Task<IReadOnlyList<CompanyCalendarEntryDto>> BuildLeaveStartEntriesAsync(GetCompanyCalendarQuery query, CancellationToken cancellationToken)
    {
        var starts = await leaveStarts.GetApprovedLeaveStartsAsync(tenant.TenantId, query.From, query.To, cancellationToken);
        if (starts.Count == 0)
        {
            return [];
        }

        var names = await NamesAsync(starts.Select(s => s.EmployeeId).Distinct().ToList(), cancellationToken);
        return starts
            .Select(s => new CompanyCalendarEntryDto(
                Id: null, names.GetValueOrDefault(s.EmployeeId, string.Empty), Description: null, s.StartDate,
                CompanyEventType.LeaveStart, VisibilityOrgUnitId: null, s.EmployeeId, CreatedByUserId: null))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyList<Guid> employeeIds, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var contacts = await employeeContacts.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken);
        return contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());
    }
}
