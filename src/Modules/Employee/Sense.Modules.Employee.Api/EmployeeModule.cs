using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.EmploymentPeriods;
using Sense.Modules.Employee.Application.Import;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Application.Subcontractors;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Application.Union;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Employee.Api;

/// <summary>Employee module composition root (Faz 0: employee record, manager hierarchy, directory/org chart; full personnel record is Faz 1).</summary>
public sealed class EmployeeModule : IModule
{
    public string Name => EmployeeDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(EmployeeModule).Assembly,
        typeof(EmployeePermissions).Assembly,
        typeof(EmployeeDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => EmployeePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // senseik-049: Identity dışına genişletilen denetim izi - IdentityModule'deki IdentityAuditLogInterceptor
        // deseninin aynısı, paylaşımlı AuditLogInterceptor ile (bkz. Sense.Shared.Infrastructure.Persistence.Audit).
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<EmployeeDbContext>(
            configuration, EmployeeDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<EmployeeDbContext>>();

        // Handlers/validators live in the Application assembly (EmployeePermissions is in Contracts, so it must not be the scan anchor).
        // Contracts assembly is also required: EmployeeHired/EmployeeSuspended/EmployeeReactivated live there, and
        // IIntegrationEventOutbox/ModuleUnitOfWorkResolver resolve an event's owning module by its assembly.
        services.AddModuleHandlers(EmployeeDbContext.SchemaName, typeof(EmployeeErrors).Assembly, typeof(EmployeeDbContext).Assembly, typeof(EmployeePermissions).Assembly);

        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IEmployeeReadStore, EmployeeReadStore>();
        services.AddScoped<EmployeeSelectionResolver>();
        services.AddScoped<EmployeeDetailBuilder>();
        services.AddScoped<EmployeeReferenceValidator>();
        services.AddScoped<EmployeeSensitiveAccess>();
        services.AddScoped<IEmployeeUnitOfWork, EmployeeUnitOfWork>();

        // Custom field definitions (tenant admin catalog) + employee values (E4)
        services.AddScoped<ICustomFieldDefinitionRepository, CustomFieldDefinitionRepository>();
        services.AddScoped<CustomFieldValueReader>();

        // Termination wizard (E5a): reason catalog (system-seeded per tenant) + the advisory job-security scope check.
        // ILegislationParameterReader is registered by the Leave module (ADR 0008 single-owner table); Employee only
        // consumes it here, no registration needed on this side.
        services.AddScoped<ITerminationReasonRepository, TerminationReasonRepository>();
        services.AddScoped<TerminationReasonProvisioningService>();

        // senseik-006/F-01: SGK exit code backfill service (run at API startup by TerminationReasonCorrectionHostedService below).
        services.AddScoped<TerminationReasonCorrectionService>();

        // Runs the correction at API startup for every writable tenant (idempotent; Worker does not load this module).
        services.AddScoped<TerminationReasonCorrectionSynchronizer>();
        services.AddHostedService<TerminationReasonCorrectionHostedService>();
        services.AddScoped<JobSecurityEvaluator>();
        services.AddScoped<CollectiveDismissalEvaluator>();

        // Employment periods + rehire (E5a): merged seniority for leave, severance and the job-security check.
        services.AddScoped<EmploymentHistoryReader>();

        // Union memberships (E5b): special-category data, encrypted with the shared IEncryptionProvider.
        services.AddScoped<UnionMembershipService>();

        // Subcontractor catalog (E5b): employees link to it; the İSG headcount threshold (Faz 7) reads that link.
        services.AddScoped<ISubcontractorRepository, SubcontractorRepository>();

        // Profile photo (bytea row per employee, served through the API so data scope and tenant isolation apply).
        services.AddScoped<IEmployeePhotoRepository, EmployeePhotoRepository>();
        services.AddScoped<EmployeePhotoWriter>();

        // Career history (E2): shared by the career endpoints, PUT /employees/{id}, bulk assign-manager and the import
        services.AddScoped<OrgPlacementLoader>();
        services.AddScoped<CareerService>();
        services.AddScoped<CareerReader>();

        // Salary history + bank accounts (E3): IBANs are stored as AES-GCM ciphertext via the shared IEncryptionProvider
        services.AddScoped<IIbanProtector, EncryptedIbanProtector>();
        services.AddScoped<PaymentsService>();
        services.AddScoped<BankAccountWriter>();
        services.AddScoped<SelfEmployeeResolver>();

        // Data import template "employee.master" (collected by the DataTransfer module)
        services.AddScoped<IImportTemplate, EmployeeMasterImportTemplate>();
        services.AddScoped<IImportRowHandler, EmployeeImportRowHandler>();

        // Cross-module contracts consumed by Organization/Identity
        services.AddScoped<IEmployeeLookup, EmployeeLookup>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<IEmployeeUserLinker, EmployeeUserLinker>();
        services.AddScoped<IEmployeeScopeLookup, EmployeeScopeLookup>();

        // Cross-module contract consumed by Notifications (recipient resolution, also in Sense.Worker)
        services.AddScoped<IEmployeeContactLookup, EmployeeContactLookup>();

        // Cross-module contract consumed by Documents (document requirement evaluation): derived booleans only, never raw sensitive fields
        services.AddScoped<IEmployeeDocumentEligibilityLookup, EmployeeDocumentEligibilityLookup>();

        // Cross-module contract consumed by Analytics (workforce KPIs)
        services.AddScoped<IEmployeeAnalyticsQueries, EmployeeAnalyticsQueries>();

        // Raporlar menüsü katkıları (roadmap #70): rapor okuyucu portu + Employee'nin hazır raporları.
        services.AddScoped<IEmployeeReportReader, EmployeeReportReader>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Infrastructure.Reporting.EmployeeListReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Infrastructure.Reporting.SalaryListReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Infrastructure.Reporting.DepartedEmployeesReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Infrastructure.Reporting.EmployeeBirthdayReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Infrastructure.Reporting.EmployeeTurnoverReportProvider>();

        // Cross-module contract consumed by Payroll (EmployeeHired -> initial EmployeePayrollProfile.WageBasis, 11.3.07 P2)
        services.AddScoped<IEmployeeSalaryLookup, EmployeeSalaryLookup>();

        // Cross-module contract consumed by Payroll (severance/notice estimate, senseik-011 F-06); API-only (no
        // Worker consumer), so unlike IEmployeeSalaryLookup/IEmployeeBirthDateLookup it needs no Sense.Worker registration.
        services.AddScoped<IEmployeeSeniorityLookup, EmployeeSeniorityLookup>();

        // Cross-module contract consumed by Payroll's bank payment file (plain IBAN of the primary account; permission-gated export only).
        services.AddScoped<IEmployeePayoutAccountLookup, EmployeePayoutAccountLookup>();

        // Cross-module contract consumed by Payroll (senseik-019 2b, GVK md.31 engelli çalışan gelir vergisi matrah indirimi)
        services.AddScoped<IEmployeeDisabilityLookup, EmployeeDisabilityLookup>();

        // Cross-module contract consumed by Leave (EmployeeHired -> EmployeeLeaveProfile.BirthDate, 4857 md.53/IV
        // yaş istisnası; also read in bulk by LeaveBalanceRecalculationJob's nightly backfill/resync pass)
        services.AddScoped<IEmployeeBirthDateLookup, EmployeeBirthDateLookup>();

        // Cross-module contract consumed by Recruitment (ATS to HR core bridge: candidate hire -> employee provisioning, Faz 9)
        services.AddScoped<IEmployeeProvisioning, EmployeeProvisioning>();

        // Cross-module contracts consumed by Notifications (exit e-mail's çalışma belgesi attachment, announcement
        // audience) - registered in Sense.Worker too, where the delivery processor and event handlers run.
        services.AddScoped<IWorkCertificateProvider, WorkCertificateProvider>();
        services.AddScoped<IEmployeeAudienceLookup, EmployeeAudienceLookup>();

        // Hangfire runs in Sense.Api: daily birthday pass publishing EmployeeBirthdaysToday for Notifications.
        services.AddScoped<Sense.Shared.Contracts.BackgroundJobs.IScheduledJobHandler,
            Infrastructure.BackgroundJobs.BirthdayGreetingJobHandler>();
    }
}
