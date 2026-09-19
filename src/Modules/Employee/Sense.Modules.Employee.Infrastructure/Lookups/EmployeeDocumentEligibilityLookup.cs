using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// Implements Employee.Contracts.IEmployeeDocumentEligibilityLookup: computes IsUnder18 from BirthDate (age &lt; 18
/// as of today; null BirthDate =&gt; false), HasDisability from DisabilityDegree (null or the explicit "no
/// disability" <see cref="DisabilityDegree.None"/> =&gt; false), and IsUndergroundWorker from the Employee's
/// IsUndergroundWorker flag (6331 md.15: periyodik sağlık muayenesi gerekir). Never exposes the raw sensitive fields themselves.
/// </summary>
public sealed class EmployeeDocumentEligibilityLookup(EmployeeDbContext context) : IEmployeeDocumentEligibilityLookup
{
    public async Task<EmployeeDocumentEligibilitySignals?> GetSignalsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var row = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => new { e.BirthDate, e.DisabilityDegree, e.IsUndergroundWorker, e.Nationality })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var isUnder18 = row.BirthDate is { } birthDate && Age(birthDate, today) < 18;
        var hasDisability = row.DisabilityDegree is { } degree && degree != DisabilityDegree.None;
        var isUndergroundWorker = row.IsUndergroundWorker;
        var isForeignNational = row.Nationality == Nationality.Foreign;
        return new EmployeeDocumentEligibilitySignals(isUnder18, hasDisability, isUndergroundWorker, isForeignNational);
    }

    private static int Age(DateOnly birthDate, DateOnly asOf)
    {
        var age = asOf.Year - birthDate.Year;
        if (birthDate > asOf.AddYears(-age))
        {
            age--;
        }

        return age;
    }
}
