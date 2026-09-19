using Sense.Modules.Analytics.Api;
using Sense.Modules.Announcements.Api;
using Sense.Modules.Assets.Api;
using Sense.Modules.Attendance.Api;
using Sense.Modules.DataTransfer.Api;
using Sense.Modules.Documents.Api;
using Sense.Modules.Employee.Api;
using Sense.Modules.Expense.Api;
using Sense.Modules.Identity.Api;
using Sense.Modules.Integrations.Api;
using Sense.Modules.Leave.Api;
using Sense.Modules.Notifications.Api;
using Sense.Modules.Organization.Api;
using Sense.Modules.Payroll.Api;
using Sense.Modules.Performance.Api;
using Sense.Modules.Recruitment.Api;
using Sense.Modules.Reporting.Api;
using Sense.Modules.Training.Api;
using Sense.Modules.Workflow.Api;
using Sense.Modules.OccupationalSafety.Api;
using Sense.Shared.Contracts.Modules;

namespace Sense.Api;

/// <summary>Host'un yüklediği modüllerin açık listesi (assembly taraması yerine derleme zamanı görünürlük).</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> Modules { get; } =
    [
        new IdentityModule(),
        new OrganizationModule(),
        new EmployeeModule(),
        new DocumentsModule(),
        new DataTransferModule(),
        new WorkflowModule(),
        new NotificationsModule(),
        new LeaveModule(),
        new AttendanceModule(),
        new AnalyticsModule(),
        new PayrollModule(),
        new RecruitmentModule(),
        new ExpenseModule(),
        new IntegrationsModule(),
        new AssetsModule(),
        new ReportingModule(),
        new PerformanceModule(),
        new AnnouncementsModule(),
        new OhsModule(),
        new TrainingModule(),
    ];
}
