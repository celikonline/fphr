using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Employees;
using Sense.Modules.Payroll.Application.Events;
using Sense.Modules.Payroll.Application.Closing;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Infrastructure.Adapters;
using Sense.Modules.Payroll.Infrastructure.Jobs;
using Sense.Modules.Payroll.Infrastructure.Lookups;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Payroll.Infrastructure;

public static class PayrollServiceCollectionExtensions
{
    /// <summary>
    /// Everything the API module needs (DbContext and handler scanning are registered by the caller).
    /// <see cref="Sense.Shared.Contracts.Legislation.ILegislationParameterReader"/> is registered by the Leave module
    /// (ADR 0008 owner) via AddLeaveServices/AddLeaveCoreServices, which every host wiring Payroll must also register.
    /// </summary>
    public static IServiceCollection AddPayrollServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPayrollUnitOfWork>(sp => sp.GetRequiredService<PayrollDbContext>());
        services.AddScoped<IPayrollPeriodRepository, PayrollPeriodRepository>();
        services.AddScoped<IPayslipRepository, PayslipRepository>();
        services.AddScoped<IPayslipCalculationTraceRepository, PayslipCalculationTraceRepository>();
        services.AddScoped<IEmployeePayrollProfileRepository, EmployeePayrollProfileRepository>();
        services.AddScoped<IPayslipDirtyFlagRepository, PayslipDirtyFlagRepository>();
        services.AddScoped<IChangeDetectionRunRepository, ChangeDetectionRunRepository>();
        services.AddScoped<IUnionRepository, UnionRepository>();
        services.AddScoped<IEmployeeUnionMembershipRepository, EmployeeUnionMembershipRepository>();

        // senseik-010/F-03: Documents' DocumentRequirementMaterializationService reads this to materialize
        // IncentiveDocument (HasIncentive), mirroring Assets.Contracts.IAssetLookup's registration.
        services.AddScoped<IEmployeeIncentiveLookup, EmployeeIncentiveLookup>();

        // Ücret pusulası PDF: the payslip download and Notifications' payslip e-mail attachment (both hosts).
        services.AddScoped<IPayslipDocumentProvider, Application.Declarations.PayslipDocumentProvider>();

        // senseik-019 2c: icra/nafaka kesinti emirleri.
        services.AddScoped<IGarnishmentOrderRepository, GarnishmentOrderRepository>();

        // "Çıkış hesabı": frozen kıdem/ihbar/izin amounts per exit + the estimator it shares with the wizard preview.
        services.AddScoped<IExitSettlementRepository, ExitSettlementRepository>();
        services.AddScoped<SeveranceEstimator>();
        services.AddScoped<ExitSettlementNetService>();
        services.AddScoped<IPeriodClosingCheckRepository, PeriodClosingCheckRepository>();
        services.AddScoped<IPayrollAdjustmentRepository, PayrollAdjustmentRepository>();

        // TB-7 Attendance köprüsü (roadmap #23, S-8): Attendance.Contracts.IAttendanceMonthlySummaryReader landed
        // this tur - AttendanceWorkedTimeReaderAdapter mirrors Leave's WorkScheduleReaderAdapter registration.
        services.AddScoped<IWorkedTimeReader, AttendanceWorkedTimeReaderAdapter>();

        // TB-7 Expense köprüsü (roadmap #23, S-13, faz-07 tur 1 madde 3): approved-but-unpaid expense reimbursement
        // added as a tax-free NET addition; ExpensePayoutReaderAdapter mirrors AttendanceWorkedTimeReaderAdapter.
        services.AddScoped<IExpensePayoutReader, ExpensePayoutReaderAdapter>();

        // Leave devamsızlık köprüsü (G-7-08/09, faz-07 tur 2 madde 3): only consulted when IWorkedTimeReader has no
        // data (double-count guard, see PayrollPeriodCalculationService.CalculateOneAsync).
        services.AddScoped<IAbsenceDaysReader, LeaveAbsenceDaysReaderAdapter>();

        // P3 "Dönem hesabı" / "Değişti" orchestration (period run, recalc-dirty/all, dirty flagging on profile create/update).
        services.AddScoped<PayrollPeriodCalculationService>();
        services.AddScoped<PayslipDirtyFlagService>();
        return services;
    }

    /// <summary>
    /// Payroll's cross-module integration event handlers, registered explicitly for Sense.Worker, mirroring
    /// AddLeaveIntegrationEventHandlers/AddDocumentsIntegrationEventHandlers:
    /// - P2: EmployeeHired -> initial EmployeePayrollProfile.
    /// - P3 "Değişti" mechanism: SalaryChanged/EmployeeProfileChanged (Employee) and LeaveCancelled (Leave, the only
    ///   Leave.Leave*/Overtime* event that exists today - see PayslipDirtyFlagHandlers doc) -> PayslipDirtyFlag.
    /// The caller must also register Employee's IEmployeeSalaryLookup and Organization's IOrgUnitLookup, which the
    /// EmployeeHired handler depends on.
    /// </summary>
    public static IServiceCollection AddPayrollIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredHandler>();
        services.AddScoped<IIntegrationEventHandler<SalaryChanged>, SalaryChangedDirtyFlagHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeProfileChanged>, EmployeeProfileChangedDirtyFlagHandler>();
        services.AddScoped<IIntegrationEventHandler<LeaveCancelled>, LeaveCancelledDirtyFlagHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeTerminated>, EmployeeTerminatedSettlementHandler>();
        services.AddScoped<IIntegrationEventHandler<OvertimeConvertedToPay>, OvertimeConvertedToPayDirtyFlagHandler>();
        return services;
    }

    /// <summary>ChangeDetectionRun periodic hash-reconciliation job (P3 "event kaçırma sigortası"), registered
    /// explicitly for Sense.Worker, mirroring AddLeaveBalanceRecalculationJob. Requires AddPayrollServices and
    /// Employee's IEmployeeSalaryLookup to already be registered.</summary>
    public static IServiceCollection AddPayrollChangeDetectionJob(this IServiceCollection services)
    {
        services.AddSingleton<ChangeDetectionJob>();
        services.AddHostedService<ChangeDetectionHostedService>();
        return services;
    }
}
