namespace Sense.Modules.Organization.Application.Companies;

/// <summary>
/// Full "Şirket profili" screen (Faz 2, TB-10): the tenant's primary Company plus the legal/registry
/// fields that live on <see cref="Sense.Modules.Organization.Domain.Companies.Company"/> (name still
/// comes from the attached OrgUnit, same as <see cref="CompanyDto"/>). Deliberately a separate DTO from
/// CompanyDto - that one is the "Şirket ekle" / holiday-calendar-assignment shape used by 3 existing
/// callers and must not change.
/// </summary>
public sealed record CompanyProfileDto(
    Guid Id,
    Guid OrgUnitId,
    string Name,
    string? RegistrationNumber,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Country,
    string? OperatingHoursStart,
    string? OperatingHoursEnd,
    IReadOnlyList<string> WorkingDays,
    Guid? LogoDocumentId,
    string? LogoUrl,
    DateTimeOffset UpdatedAt);
