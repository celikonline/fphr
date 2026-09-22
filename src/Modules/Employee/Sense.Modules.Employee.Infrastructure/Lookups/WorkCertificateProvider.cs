using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// <see cref="IWorkCertificateProvider"/>: çalışma belgesi (4857 md.28) with the kind of work (title, department), the
/// employment start and end dates, and signature boxes. Turkish only - the document is a statutory Turkish form. The
/// issuer is the Company-level org unit of the employee's placement.
/// </summary>
public sealed class WorkCertificateProvider(
    EmployeeDbContext context,
    IOrgUnitLookup orgUnits,
    ISimplePdfWriter pdfWriter,
    TimeProvider clock) : IWorkCertificateProvider
{
    private const string DateFormat = "dd.MM.yyyy";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<ExportFileResult?> RenderPdfAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await context.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => new
            {
                e.FirstName,
                e.LastName,
                e.NationalId,
                e.HireDate,
                e.TerminationDate,
                e.Status,
                e.OrgUnitId,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null || employee.Status != EmployeeStatus.Terminated || employee.TerminationDate is not { } endDate)
        {
            return null;
        }

        var placement = new OrgPlacement(
            await orgUnits.ListActiveTypesAsync(tenantId, cancellationToken),
            await orgUnits.ListActiveUnitsAsync(tenantId, cancellationToken));
        var company = placement.LevelUnitFor(employee.OrgUnitId, "Company")?.Name;
        var department = placement.LevelUnitFor(employee.OrgUnitId, "Department")?.Name;
        var title = placement.LevelUnitFor(employee.OrgUnitId, "Title")?.Name;

        var fullName = $"{employee.FirstName} {employee.LastName}".Trim();
        var rows = new List<KeyValuePair<string, string>>
        {
            new("Adı Soyadı", fullName),
            new("T.C. Kimlik No", employee.NationalId ?? "-"),
            new("Görevi (işin çeşidi)", title ?? "-"),
            new("Birimi", department ?? "-"),
            new("İşe başlama tarihi", Format(employee.HireDate)),
            new("İşten ayrılış tarihi", Format(endDate)),
        };

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var employer = company ?? "İşyerimiz";
        var document = new SimplePdfDocument(
            "ÇALIŞMA BELGESİ",
            company,
            [
                new SimplePdfKeyValues(null, rows),
                new SimplePdfParagraph(
                    $"Yukarıda kimlik bilgileri yazılı {fullName}, {employer} bünyesinde {Format(employee.HireDate)} - {Format(endDate)} " +
                    $"tarihleri arasında {title ?? "çalışan"} olarak görev yapmıştır."),
                new SimplePdfParagraph("İşbu belge, 4857 sayılı İş Kanunu'nun 28. maddesi uyarınca ilgilinin talebi üzerine düzenlenmiştir."),
                new SimplePdfParagraph($"Düzenleme tarihi: {today.ToString(DateFormat, Turkish)}"),
                new SimplePdfSignatures(["İşveren / İşveren Vekili\nKaşe - İmza"]),
            ],
            "SenseİK tarafından oluşturulmuştur.");

        var fileName = $"calisma-belgesi_{Slug(fullName)}.pdf";
        return new ExportFileResult(fileName, "application/pdf", pdfWriter.Write(document));
    }

    private static string Format(DateOnly? date) => date?.ToString(DateFormat, Turkish) ?? "-";

    private static string Slug(string value)
    {
        var ascii = value.ToLower(Turkish)
            .Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i').Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
        var chars = ascii.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary><see cref="IEmployeeAudienceLookup"/>: Active and Suspended employees (announcement recipients).</summary>
public sealed class EmployeeAudienceLookup(EmployeeDbContext context) : IEmployeeAudienceLookup
{
    public async Task<IReadOnlyList<Guid>> ListNotifiableEmployeeIdsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => e.TenantId == tenantId && (e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Suspended))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
}
