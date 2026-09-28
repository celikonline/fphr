using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sense.Modules.Announcements.Infrastructure;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Modules.Assets.Infrastructure;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Documents.Infrastructure;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Infrastructure;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Infrastructure;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Expense.Infrastructure;
using Sense.Modules.Expense.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Modules.Integrations.Infrastructure;
using Sense.Modules.Integrations.Infrastructure.Persistence;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Delivery;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Modules.Payroll.Infrastructure;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Modules.Performance.Infrastructure;
using Sense.Modules.Recruitment.Infrastructure;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Modules.Training.Infrastructure;
using Sense.Modules.Onboarding.Infrastructure;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Infrastructure.Messaging;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Modules.OccupationalSafety.Infrastructure;
using Sense.Modules.OccupationalSafety.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Outbox;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSenseCore(builder.Configuration);
// Documents' visa approval handler (senseik-001) copies template documents server-side through IFileStorage (MinIO:*).
builder.Services.AddFileStorage(builder.Configuration);
// Every module's outbox receives both Aggregate.Raise(...) domain events (AuditTenantInterceptor collects them on
// SaveChanges) and explicit IIntegrationEventOutbox writes. EventTypeRegistry must know each polled module's
// Domain+Contracts assemblies or OutboxProcessor retries forever with "Event type could not be resolved".
builder.Services.AddModuleDbContext<IdentityDbContext>(builder.Configuration, IdentityDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    IdentityDbContext.SchemaName,
    typeof(Sense.Modules.Identity.Contracts.AccessSettingKeys).Assembly,
    typeof(IdentityDbContext).Assembly,
    typeof(Sense.Modules.Identity.Domain.IUserRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<IdentityDbContext>>();

builder.Services.AddModuleDbContext<DocumentsDbContext>(builder.Configuration, DocumentsDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    DocumentsDbContext.SchemaName,
    typeof(Sense.Modules.Documents.Contracts.DocumentsPermissions).Assembly,
    typeof(DocumentsDbContext).Assembly,
    typeof(Sense.Modules.Documents.Domain.IDocumentRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<DocumentsDbContext>>();
// Documents consumes EmployeeHired (personnel document requirement materialization at hire time); registered
// explicitly (not the whole Application assembly) so API-only handler dependencies stay out, mirroring Leave/Attendance below.
builder.Services.AddDocumentsCoreServices();
builder.Services.AddDocumentsIntegrationEventHandlers();

builder.Services.AddModuleDbContext<OrganizationDbContext>(builder.Configuration, OrganizationDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    OrganizationDbContext.SchemaName,
    typeof(OrganizationPermissions).Assembly,
    typeof(OrganizationDbContext).Assembly,
    typeof(IOrgUnitRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<OrganizationDbContext>>();
// Organization consumes Identity's TenantProvisioned (new tenant → unit types, root company, TR holiday calendar); registered
// explicitly since this host does not scan Organization's Application assembly.
builder.Services.AddOrganizationIntegrationEventHandlers();

// Employee lifecycle integration events (EmployeeHired/Suspended/Reactivated) consumed by Leave/Workflow.
builder.Services.AddModuleDbContext<EmployeeDbContext>(builder.Configuration, EmployeeDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    EmployeeDbContext.SchemaName,
    typeof(EmployeePermissions).Assembly,
    typeof(EmployeeDbContext).Assembly,
    typeof(IEmployeeRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<EmployeeDbContext>>();
// Employee's note attachment cleanup (EmployeeNoteFileOrphaned): its handler lives in the Application assembly this host does not scan.
builder.Services.AddEmployeeIntegrationEventHandlers();

// Workflow writes ApprovalCompleted/ApprovalRejected to its own outbox (see IIntegrationEventOutbox usages in
// Sense.Modules.Workflow.Application's decision handlers) - see the EventTypeRegistry note at the top.
builder.Services.AddModuleDbContext<WorkflowDbContext>(builder.Configuration, WorkflowDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    WorkflowDbContext.SchemaName,
    typeof(WorkflowPermissions).Assembly,
    typeof(WorkflowDbContext).Assembly,
    typeof(IApprovalPolicyRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<WorkflowDbContext>>();

// Notifications (B2) consumes the Workflow events above (request created/approved/rejected, task assigned): writes
// in-app notifications + pushes them over SignalR (Redis backplane → Api connections), queues e-mail deliveries and
// sends them with EmailDeliveryPollingService. Its handlers need these cross-module lookups, which resolve here
// with just the modules' DbContexts registered above.
var redisConnection = builder.Configuration.GetConnectionString(ConnectionStringNames.Redis);
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("senseik-signalr-"));
}

builder.Services.AddScoped<IEmployeeContactLookup, EmployeeContactLookup>();
builder.Services.AddScoped<IUserProfileLookup, UserProfileLookup>();
builder.Services.AddScoped<IRoleAssignmentLookup, RoleAssignmentLookup>();
// Attendance's ITenantLocalClock (registered by AddAttendanceCoreServices for both hosts) resolves the tenant time zone
// through this Identity read contract; without it the host fails DI validation at startup.
builder.Services.AddScoped<Sense.Modules.Identity.Domain.ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantTimeZoneLookup, TenantTimeZoneLookup>();
// Identity's EmployeeTerminated handler (G-3-01) disables the terminated employee's login; it lives in the Application
// assembly the Worker does not scan, so it is registered explicitly with the repositories it needs.
builder.Services.AddScoped<Sense.Modules.Identity.Domain.IUserRepository, UserRepository>();
builder.Services.AddScoped<Sense.Modules.Identity.Domain.IRoleRepository, RoleRepository>();
builder.Services.AddScoped<Sense.Modules.Identity.Application.IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
builder.Services.AddScoped<Sense.Shared.Contracts.Events.IIntegrationEventHandler<Sense.Modules.Employee.Contracts.Events.EmployeeTerminated>, Sense.Modules.Identity.Application.Users.EmployeeTerminatedHandler>();
// Documents' EmployeeHired handler materializes requirements through this Employee read contract.
builder.Services.AddScoped<IEmployeeDocumentEligibilityLookup, EmployeeDocumentEligibilityLookup>();
// Notifications' announcement audience (every active employee) and the exit e-mail's çalışma belgesi PDF attachment.
builder.Services.AddScoped<IEmployeeAudienceLookup, EmployeeAudienceLookup>();
builder.Services.AddScoped<IWorkCertificateProvider, WorkCertificateProvider>();
builder.Services.AddWorkflowDecisionLinks(builder.Configuration);
builder.Services.AddNotificationsCore(builder.Configuration);
builder.Services.AddHostedService<EmailDeliveryPollingService>();
builder.Services.AddHostedService<PushDeliveryPollingService>();
// Leave consumes EmployeeHired (leave profile + default rule set + accruals) and Workflow's ApprovalCompleted/Rejected
// (leave request decisions); OutboxProcessor publishes through this process's IEventBus, so the handlers live here.
// Handlers are registered explicitly (not the whole Application assembly) so API-only handler dependencies stay out.
// EmployeeHiredHandler and LeaveBalanceRecalculationJob both read BirthDate through this Contracts port (4857
// md.53/IV yaş istisnası) - never carried on EmployeeHired itself, mirroring IEmployeeDocumentEligibilityLookup above.
builder.Services.AddScoped<IEmployeeBirthDateLookup, EmployeeBirthDateLookup>();
builder.Services.AddModuleDbContext<Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext>(builder.Configuration, Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext.SchemaName);
builder.Services.AddLeaveCoreServices(builder.Configuration);
builder.Services.AddLeaveIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext.SchemaName,
    typeof(Sense.Modules.Leave.Contracts.LeavePermissions).Assembly,
    typeof(Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext).Assembly,
    typeof(Sense.Modules.Leave.Domain.ILeaveRuleSetRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext>>();
builder.Services.AddLeaveBalanceRecalculationJob();
// AddModuleHandlers above scans the Leave Infrastructure assembly, which also holds the API-only company ledger query
// handler (senseik-048). It needs IDataScopeService/report readers that only the API host registers and the Worker never
// serves queries, so drop it here or the host fails DI validation at startup.
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<
    Sense.Shared.Contracts.Messaging.IQueryHandler<
        Sense.Modules.Leave.Application.Ledger.GetCompanyLeaveLedgerQuery,
        Sense.Modules.Leave.Application.Ledger.CompanyLeaveLedgerDto>>(builder.Services);

// Attendance consumes EmployeeHired (default work schedule assignment, Source=Hire) and publishes WorkScheduleAssigned
// for Leave to eventually replace its Monday–Friday placeholder (roadmap #23).
builder.Services.AddModuleDbContext<AttendanceDbContext>(builder.Configuration, AttendanceDbContext.SchemaName);
builder.Services.AddAttendanceCoreServices(builder.Configuration);
builder.Services.AddAttendanceIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    AttendanceDbContext.SchemaName,
    typeof(Sense.Modules.Attendance.Contracts.AttendancePermissions).Assembly,
    typeof(AttendanceDbContext).Assembly,
    typeof(Sense.Modules.Attendance.Domain.IWorkScheduleRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<AttendanceDbContext>>();

// Payroll (Faz 3 P0-P3): calculation-engine golden set + EmployeePayrollProfile CRUD, EmployeeHired -> initial
// profile, and (P3) the "Değişti" dirty-flag mechanism: SalaryChanged/EmployeeProfileChanged (Employee) and
// LeaveCancelled (Leave) consumers write PayslipDirtyFlag rows, plus the periodic ChangeDetectionRun hash-
// reconciliation hosted service ("5 dk mutabakat", event kaçırma sigortası). The EmployeeHired handler needs
// Employee's IEmployeeSalaryLookup (WageBasis) and Organization's IOrgUnitLookup (default SGK workplace company);
// ChangeDetectionJob also reads IEmployeeSalaryLookup directly - both registered explicitly here since this host
// wires modules manually rather than through each IModule.AddModule (mirrors IEmployeeContactLookup above).
builder.Services.AddModuleDbContext<PayrollDbContext>(builder.Configuration, PayrollDbContext.SchemaName);
builder.Services.AddPayrollServices(builder.Configuration);
builder.Services.AddPayrollIntegrationEventHandlers();
builder.Services.AddPayrollChangeDetectionJob();
builder.Services.AddScoped<Sense.Modules.Employee.Contracts.IEmployeeSalaryLookup, EmployeeSalaryLookup>();
// EmployeeTerminatedSettlementHandler ("Çıkış hesabı") runs SeveranceEstimator, which also needs the counted-seniority port
// (Leave core already provides ILeaveBalanceLookup and ILegislationParameterReader).
builder.Services.AddScoped<Sense.Modules.Employee.Contracts.IEmployeeSeniorityLookup, EmployeeSeniorityLookup>();
// PayrollPeriodCalculationService reads the disability degree (engelli vergi indirimi) through this Employee contract.
builder.Services.AddScoped<Sense.Modules.Employee.Contracts.IEmployeeDisabilityLookup, EmployeeDisabilityLookup>();
builder.Services.AddScoped<Sense.Modules.Organization.Contracts.IOrgUnitLookup, OrgUnitLookup>();
// RecordIncidentHandler (OHS) also needs the tenant's holiday calendar to classify an incident's date.
builder.Services.AddScoped<Sense.Modules.Organization.Contracts.IHolidayCalendarLookup, HolidayCalendarLookup>();
builder.Services.AddModuleHandlers(
    PayrollDbContext.SchemaName,
    typeof(Sense.Modules.Payroll.Contracts.PayrollPermissions).Assembly,
    typeof(PayrollDbContext).Assembly,
    typeof(Sense.Modules.Payroll.Domain.IPayrollPeriodRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<PayrollDbContext>>();

// Recruitment: JobPostingPublished/JobApplicationStageChanged still have no subscriber (a later Recruitment package)
// but drain anyway so EventTypeRegistry knows the types. R1 adds requisition approval consumption: Workflow's
// ApprovalCompleted/ApprovalRejected for RequestType="Requisition" (see Organization.Contracts.ApprovalRequestTypes)
// flip the linked JobRequisition to Approved/Rejected, which unlocks/blocks publishing its JobPosting.
builder.Services.AddModuleDbContext<RecruitmentDbContext>(builder.Configuration, RecruitmentDbContext.SchemaName);
builder.Services.AddScoped<Sense.Modules.Recruitment.Domain.IJobRequisitionRepository, Sense.Modules.Recruitment.Infrastructure.Persistence.JobRequisitionRepository>();
builder.Services.AddRecruitmentCoreServices();
builder.Services.AddRecruitmentIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    RecruitmentDbContext.SchemaName,
    typeof(Sense.Modules.Recruitment.Contracts.RecruitmentPermissions).Assembly,
    typeof(RecruitmentDbContext).Assembly,
    typeof(Sense.Modules.Recruitment.Domain.IJobPostingRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<RecruitmentDbContext>>();

// Performance (faz-08 R0): consumes Employee's EmployeeTerminated to archive (freeze, not delete/cancel) the
// terminated employee's open goals - see Sense.Modules.Performance.Application.Employees.EmployeeTerminatedHandler.
// Registered explicitly (not the whole Application assembly) so API-only handler dependencies (IReviewCycleRepository,
// IDataScopeService, etc.) stay out, mirroring Leave/Attendance/Documents/Payroll above.
builder.Services.AddModuleDbContext<Sense.Modules.Performance.Infrastructure.Persistence.PerformanceDbContext>(
    builder.Configuration, Sense.Modules.Performance.Infrastructure.Persistence.PerformanceDbContext.SchemaName);
builder.Services.AddPerformanceCoreServices();
builder.Services.AddScoped<Sense.Modules.Performance.Domain.IGoalRepository, Sense.Modules.Performance.Infrastructure.Persistence.GoalRepository>();
builder.Services.AddScoped<Sense.Shared.Contracts.Events.IIntegrationEventHandler<Sense.Modules.Employee.Contracts.Events.EmployeeTerminated>, Sense.Modules.Performance.Application.Employees.EmployeeTerminatedHandler>();
builder.Services.AddModuleHandlers(
    Sense.Modules.Performance.Infrastructure.Persistence.PerformanceDbContext.SchemaName,
    typeof(Sense.Modules.Performance.Contracts.PerformancePermissions).Assembly,
    typeof(Sense.Modules.Performance.Infrastructure.Persistence.PerformanceDbContext).Assembly,
    typeof(Sense.Modules.Performance.Domain.IGoalRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<Sense.Modules.Performance.Infrastructure.Persistence.PerformanceDbContext>>();

// Expense: consumes Workflow's ApprovalCompleted/ApprovalRejected for RequestType "ExpenseClaim"/"Advance"
// (ExpenseClaimApprovalCompletedHandler/RejectedHandler, AdvanceApprovalCompletedHandler/RejectedHandler in
// Sense.Modules.Expense.Application), and writes its own ClaimSubmitted/Approved/Rejected + AdvanceRequestSubmitted/
// Approved/Rejected events back to its own outbox - no consumer yet (Notifications templates are a later package),
// but drained anyway so EventTypeRegistry knows the types (see the note at the top of this file).
builder.Services.AddModuleDbContext<ExpenseDbContext>(builder.Configuration, ExpenseDbContext.SchemaName);
builder.Services.AddExpenseCoreServices();
builder.Services.AddExpenseIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    ExpenseDbContext.SchemaName,
    typeof(Sense.Modules.Expense.Contracts.ExpensePermissions).Assembly,
    typeof(ExpenseDbContext).Assembly,
    typeof(Sense.Modules.Expense.Domain.IExpenseClaimRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<ExpenseDbContext>>();

// Assets (zimmet, roadmap #65): writes AssetAssigned/AssetReturned to its own outbox; Documents consumes both
// (AssetEventDocumentRequirementHandlers, registered in AddDocumentsIntegrationEventHandlers above) to
// materialize/keep the AssetHandoverForm conditional requirement in sync. Assets itself also consumes Employee's
// EmployeeTerminated (faz4 offboarding↔zimmet: EmployeeTerminatedAssetAuditHandler writes an
// AssetUnreturnedAtTermination audit trail entry per still-open asset, non-blocking) via AddAssetsIntegrationEventHandlers.
builder.Services.AddModuleDbContext<AssetsDbContext>(builder.Configuration, AssetsDbContext.SchemaName);
builder.Services.AddAssetsCoreServices();
builder.Services.AddAssetsIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    AssetsDbContext.SchemaName,
    typeof(Sense.Modules.Assets.Application.AssetsErrors).Assembly,
    typeof(AssetsDbContext).Assembly,
    typeof(Sense.Modules.Assets.Contracts.AssetsPermissions).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<AssetsDbContext>>();
// The scan above includes the Assets Application assembly, so it also picks up the API-only AssignAssetHandler, which
// (TK-018) submits to Workflow through IWorkflowService - registered only by the API host. The Worker never serves
// commands, so drop it here or the host fails DI validation at startup (mirrors the Leave ledger query above).
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<
    Sense.Shared.Contracts.Messaging.ICommandHandler<Sense.Modules.Assets.Application.Assets.AssignAssetCommand>>(builder.Services);

// Integrations (Faz 8 #61): consumes Employee's employee.* lifecycle events and turns them into outbound webhook
// deliveries for tenants with an active subscription (see faz8-durum-takibi.md). Writes nothing to its own outbox
// yet, so no OutboxPollingService - just DbContext + handler scanning so EmployeeLifecycleWebhookHandlers resolves.
builder.Services.AddModuleDbContext<IntegrationsDbContext>(builder.Configuration, IntegrationsDbContext.SchemaName);
builder.Services.AddIntegrationsServices();
builder.Services.AddModuleHandlers(
    IntegrationsDbContext.SchemaName,
    typeof(Sense.Modules.Integrations.Application.Webhooks.WebhookDispatchService).Assembly,
    typeof(IntegrationsDbContext).Assembly,
    typeof(Sense.Modules.Integrations.Domain.IWebhookSubscriptionRepository).Assembly,
    typeof(Sense.Modules.Integrations.Contracts.IntegrationsPermissions).Assembly);

// Announcements/Support (11.3.13, roadmap #69): writes SupportTicketCreated/Answered (Contracts) to its own outbox;
// AddAnnouncementsIntegrationEventHandlers resolves ISupportChannel's default e-mail adapter, which in turn
// needs Identity's IUserProfileLookup (registered above, near the Notifications block) for the ticket-answered
// notification recipient's e-mail. The Application assembly is deliberately not scanned: its query handlers
// (GetSupportTicketHandler) take Identity's IPermissionService, which only the API host registers - same
// "explicit handlers, no Application scan" pattern as Leave/Attendance/Documents above.
builder.Services.AddModuleDbContext<AnnouncementsDbContext>(builder.Configuration, AnnouncementsDbContext.SchemaName);
builder.Services.AddAnnouncementsCoreServices(builder.Configuration);
builder.Services.AddAnnouncementsIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    AnnouncementsDbContext.SchemaName,
    typeof(Sense.Modules.Announcements.Domain.Announcement).Assembly,
    typeof(AnnouncementsDbContext).Assembly,
    typeof(Sense.Modules.Announcements.Contracts.AnnouncementsPermissions).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<AnnouncementsDbContext>>();

// Occupational Safety (İSG, senseik-015): hazard classification + risk assessments, and (senseik-015 devamı)
// RecordIncidentHandler, which needs Employee's IEmployeeLookup to validate the reporting employee - registered
// explicitly here since this host wires modules manually rather than through EmployeeModule.AddModule (mirrors the
// other Employee.Contracts lookups above). No own-outbox writes yet (Documents' materialization hooks,
// committee/accident events, PPE zimmet are later phases per the handoff doc).
builder.Services.AddModuleDbContext<OhsDbContext>(builder.Configuration, OhsDbContext.SchemaName);
builder.Services.AddOhsCoreServices();
builder.Services.AddScoped<Sense.Modules.Employee.Contracts.IEmployeeLookup, EmployeeLookup>();
builder.Services.AddModuleHandlers(
    OhsDbContext.SchemaName,
    typeof(Sense.Modules.OccupationalSafety.Application.IOhsUnitOfWork).Assembly,
    typeof(OhsDbContext).Assembly,
    typeof(Sense.Modules.OccupationalSafety.Contracts.OhsPermissions).Assembly);
// WorkAccidentRecorded / SgkNotificationReminderDue are written to the ohs outbox and published from here.
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<OhsDbContext>>();

// Training (Faz 4 #72: ön koşul eğitim + mentör atama): no cross-module events yet - DbContext + handler scanning
// only, kept consistent with the other modules so future integration events (e.g. a completion notification) have
// somewhere to land.
builder.Services.AddModuleDbContext<Sense.Modules.Training.Infrastructure.Persistence.TrainingDbContext>(
    builder.Configuration, Sense.Modules.Training.Infrastructure.Persistence.TrainingDbContext.SchemaName);
builder.Services.AddTrainingCoreServices();
builder.Services.AddModuleHandlers(
    Sense.Modules.Training.Infrastructure.Persistence.TrainingDbContext.SchemaName,
    typeof(Sense.Modules.Training.Domain.TrainingProgram).Assembly,
    typeof(Sense.Modules.Training.Application.TrainingErrors).Assembly,
    typeof(Sense.Modules.Training.Infrastructure.Persistence.TrainingDbContext).Assembly,
    typeof(Sense.Modules.Training.Contracts.TrainingPermissions).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<Sense.Modules.Training.Infrastructure.Persistence.TrainingDbContext>>();

// Onboarding: consumes EmployeeHired / EmployeeTerminated to materialize onboarding / offboarding checklists.
builder.Services.AddModuleDbContext<Sense.Modules.Onboarding.Infrastructure.Persistence.OnboardingDbContext>(
    builder.Configuration, Sense.Modules.Onboarding.Infrastructure.Persistence.OnboardingDbContext.SchemaName);
builder.Services.AddOnboardingCoreServices();
builder.Services.AddModuleHandlers(
    Sense.Modules.Onboarding.Infrastructure.Persistence.OnboardingDbContext.SchemaName,
    typeof(Sense.Modules.Onboarding.Domain.Tasks.OnboardingTaskTemplate).Assembly,
    typeof(Sense.Modules.Onboarding.Application.IOnboardingUnitOfWork).Assembly,
    typeof(Sense.Modules.Onboarding.Infrastructure.Persistence.OnboardingDbContext).Assembly,
    typeof(Sense.Modules.Onboarding.Contracts.OnboardingPermissions).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<Sense.Modules.Onboarding.Infrastructure.Persistence.OnboardingDbContext>>();

await builder.Build().RunAsync();

namespace Sense.Worker
{
    /// <summary>Faz 0: Hangfire gelene kadar basit polling ile modül outbox'ını işler.</summary>
    public sealed class OutboxPollingService<TContext>(IServiceProvider services, IOptions<OutboxOptions> options) : BackgroundService
        where TContext : Sense.Shared.Infrastructure.Persistence.ModuleDbContext
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds);
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = services.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<TContext>>();
                var processed = await processor.ProcessAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(interval, stoppingToken);
                }
            }
        }
    }
}
