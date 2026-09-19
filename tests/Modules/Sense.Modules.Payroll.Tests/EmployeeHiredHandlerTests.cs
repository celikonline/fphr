using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Employees;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>QA teslim testi bulgu 5 regresyonu: EmployeeHired must create the initial payroll profile. The old default
/// (Cash meal variant with a 0 daily amount) failed <c>EmployeePayrollProfile.Validate</c> and the handler returned
/// silently, so no new hire ever got a profile and every payroll run skipped them.</summary>
public sealed class EmployeeHiredHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid CompanyUnitId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2026, 9, 16);

    private static (EmployeeHiredHandler Handler, IEmployeePayrollProfileRepository Profiles, IPayrollUnitOfWork UnitOfWork) Build(bool withCompanyUnit)
    {
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([]));
        var salary = Substitute.For<IEmployeeSalaryLookup>();
        var companyTypeId = Guid.NewGuid();
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ListActiveTypesAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OrgUnitTypeInfo>>([new OrgUnitTypeInfo(companyTypeId, "company", "Şirket", 0)]));
        orgUnits.ListActiveUnitsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OrgUnitInfo>>(withCompanyUnit ? [new OrgUnitInfo(CompanyUnitId, companyTypeId, null, "Acme")] : []));
        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();
        return (new EmployeeHiredHandler(profiles, salary, orgUnits, unitOfWork, NullLogger<EmployeeHiredHandler>.Instance), profiles, unitOfWork);
    }

    [Fact]
    public async Task Handle_CreatesTheInitialProfile_ValidFromTheHireDate()
    {
        var (handler, profiles, unitOfWork) = Build(withCompanyUnit: true);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        profiles.Received(1).Add(Arg.Is<EmployeePayrollProfile>(p =>
            p.EmployeeId == EmployeeId && p.ValidFrom == HireDate && p.SgkWorkplaceCompanyId == CompanyUnitId
            && p.WageBasis == WageBasis.Monthly && p.MealBenefitVariant == MealBenefitVariant.None));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheTenantHasNoCompanyUnit_CreatesNothing()
    {
        var (handler, profiles, unitOfWork) = Build(withCompanyUnit: false);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        profiles.DidNotReceive().Add(Arg.Any<EmployeePayrollProfile>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
