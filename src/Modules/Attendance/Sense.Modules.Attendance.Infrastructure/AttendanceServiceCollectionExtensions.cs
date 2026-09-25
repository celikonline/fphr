using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Compensatory;
using Sense.Modules.Attendance.Application.Employees;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Application.PunchRequests;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Infrastructure.Jobs;
using Sense.Modules.Attendance.Infrastructure.Lookups;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure.WorkSchedules;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Infrastructure;

public static class AttendanceServiceCollectionExtensions
{
    /// <summary>Everything the API module needs (DbContext and handler scanning are registered by the caller).</summary>
    public static IServiceCollection AddAttendanceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAttendanceCoreServices(configuration);
        // Raporlar menüsü katkıları (senseik-029) - API-only: IEmployeeReportReader/IOrgUnitLookup implementasyonları Worker'da yok.
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.AttendanceMonthlySummaryReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.AttendanceMonthlyClosingReportProvider>();
        // Günlük puantaj listeleri (Z1.1): IDataScopeService/IOrgUnitLookup/ILeaveOnDateReader yalnız API'de var.
        services.AddScoped<IDailyAttendanceQuery, Application.Daily.DailyAttendanceQuery>();

        // Zaman-devam Z0.3: yeniden hesaplama kuyruğunu yalnız API süreci boşaltır (iş durumu bellekte; durum ucu API'de). Servisler
        // AddAttendanceCoreServices'te (Worker'daki onaylı okutma talebi RecalculateNowAsync çağırır).
        services.AddHostedService<RecalculationHostedService>();
        services.AddSingleton<RecalculationQueueConsumer>();
        // Zaman raporları (Z4.1a, R1): günlük sorgu + vardiya planı okuyucuları yalnız API'de var.
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.IncalculableReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.NoShiftAssignedReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.DailyStatusListsReportProvider>();
        // Okutma tabanlı zaman raporları (Z4.1b, R2).
        services.TryAddScoped<Reporting.IPunchReportReader, Reporting.PunchReportReader>();
        services.TryAddScoped<ITerminalRepository, TerminalRepository>(); // Terminal adı sütunu; Z5.1 aynı kaydı eklerse TryAdd çakışmaz.
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.ManualEntriesReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.CurrentlyInsideReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.BreaksReportProvider>();
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Reporting.IntermediatePunchesReportProvider>();
        return services;
    }

    /// <summary>
    /// Services Attendance's integration event handlers need, without dependencies on other modules' Infrastructure -
    /// Sense.Worker registers only these so DI validation in Development passes.
    /// </summary>
    public static IServiceCollection AddAttendanceCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAttendanceUnitOfWork>(sp => sp.GetRequiredService<AttendanceDbContext>());
        services.AddScoped<IWorkScheduleRepository, WorkScheduleRepository>();
        services.AddScoped<IEmployeeWorkScheduleAssignmentRepository, EmployeeWorkScheduleAssignmentRepository>();
        services.AddScoped<IAttendanceDayRepository, AttendanceDayRepository>();
        services.AddScoped<IAnomalyResolutionRepository, AnomalyResolutionRepository>();
        services.AddScoped<IShiftSettingsRepository, ShiftSettingsRepository>();
        services.AddScoped<IShiftAssignmentRepository, ShiftAssignmentRepository>();
        services.AddScoped<IShiftPlanLockRepository, ShiftPlanLockRepository>();
        services.AddScoped<IEmployeeAvailabilityRepository, EmployeeAvailabilityRepository>();
        services.AddScoped<ICompensatoryWorkLedgerRepository, CompensatoryWorkLedgerRepository>();
        services.AddScoped<IEqualizationPeriodRepository, EqualizationPeriodRepository>();
        services.AddScoped<ISelfCheckInRepository, SelfCheckInRepository>();
        services.AddScoped<ITenantLocalClock, TenantLocalClock>();
        services.AddScoped<WorkScheduleAssignmentService>();
        services.AddScoped<IWorkScheduleReadService, WorkScheduleReadService>();
        services.AddScoped<IAttendanceMonthlySummaryReader, AttendanceMonthlySummaryReader>();
        services.AddScoped<IAttendanceOvertimePayrollReader, AttendanceOvertimePayrollReader>();
        services.AddScoped<IMonthlyTimesheetApprovalRepository, MonthlyTimesheetApprovalRepository>();
        // İzinli günde çalışma detektörü (senseik-029): Leave.Contracts.IApprovedLeaveDateLookup üzerinden - toplu yeniden
        // tarama (DetectWorkedWhileOnLeaveHandler) ve ay sonu kapatma (ApproveMonthlyTimesheetHandler) kullanır. Bilinçli
        // olarak RecordAttendanceHandler/AttendanceImportRowHandler'a bağlanmaz (bkz. LeaveOverlapChecker doc comment).
        services.AddScoped<LeaveOverlapChecker>();
        // Resmi tatil (Organization) + onaylı izin (Leave) günleri: ekip ızgarası ve aylık özet "eksik gün" hesabı. İki port da opsiyonel (bkz. NonWorkingDayResolver).
        services.AddScoped<NonWorkingDayResolver>();
        services.AddScoped<IAttendanceDailyKpiReader, AttendanceDailyKpiReader>();
        services.AddScoped<IShiftPlanTemplateRepository, ShiftPlanTemplateRepository>();
        // Eksik kayıt talebi (Z2.1, B6).
        services.AddScoped<IPunchRequestRepository, PunchRequestRepository>();
        services.AddScoped<Sense.Modules.Workflow.Contracts.IApprovalRequestSummaryProvider, PunchRequestApprovalSummaryProvider>();
        // Z3.1 toplu atama (B8): vardiya tanımı okuma; B2 aynı kaydı eklerse tekrar harmsızdır (son kayıt kazanır).
        services.TryAddScoped<IShiftDefinitionRepository, ShiftDefinitionRepository>();
        services.AddScoped<IRotationRepository, RotationRepository>();
        // Z0.2 vardiya kataloğu + K2 plan çözümleme (B2).
        // TryAdd: B9 (rotasyon) da aynı repository'yi kaydeder; birleşmede çift kayıt olmasın.
        services.TryAddScoped<IShiftDefinitionRepository, ShiftDefinitionRepository>();
        services.TryAddScoped<IPlannedDayResolver, PlannedDayResolver>();
        // Zaman-devam B1 (Z0.1): ham okutma deposu + tek yazma yolu. TryAdd: aynı dalgada başka şeritler de depoyu kaydedebilir.
        services.TryAddScoped<IPunchRepository, PunchRepository>();
        services.TryAddScoped<IPunchRecorder, Application.Punches.PunchRecorder>();
        // Zaman-devam I0: okutma tabanlı yeniden hesap (B3 + B1 + B2) - API ve Worker. RecalculationExecutor'un IEmployeeDirectory'si
        // opsiyoneldir (Worker'da yok; orada çağıranlar çalışan listesi verir).
        services.AddScoped<IDayRebuilder, PunchDayRebuilder>();
        services.AddScoped<RecalculationExecutor>();
        services.AddSingleton<RecalculationJobStore>();
        services.AddSingleton<AttendanceDayRecalculator>();
        services.AddSingleton<IAttendanceDayRecalculator>(sp => sp.GetRequiredService<AttendanceDayRecalculator>());
        return services;
    }

    /// <summary>Attendance's cross-module integration event handlers (EmployeeHired, TimesheetWeekCompleted), registered explicitly for Sense.Worker.</summary>
    public static IServiceCollection AddAttendanceIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredHandler>();
        services.AddScoped<IIntegrationEventHandler<TimesheetWeekCompleted>, TimesheetWeekCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<Sense.Modules.Workflow.Contracts.Events.ApprovalCompleted>, PunchRequestApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<Sense.Modules.Workflow.Contracts.Events.ApprovalRejected>, PunchRequestApprovalRejectedHandler>();
        return services;
    }
}
