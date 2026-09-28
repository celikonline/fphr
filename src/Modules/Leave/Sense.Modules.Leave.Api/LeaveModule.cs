using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Leave.Api;

/// <summary>Leave module composition root: rule sets/types, ledger + balances, LegislationParameter reader (ADR 0008).</summary>
public sealed class LeaveModule : IModule
{
    public string Name => LeaveDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(LeaveModule).Assembly,
        typeof(LeavePermissions).Assembly,
        typeof(LeaveDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => LeavePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // senseik-049: Identity dışına genişletilen denetim izi - IdentityModule'deki IdentityAuditLogInterceptor
        // deseninin aynısı, paylaşımlı AuditLogInterceptor ile (bkz. Sense.Shared.Infrastructure.Persistence.Audit).
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<LeaveDbContext>(
            configuration, LeaveDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<LeaveDbContext>>();
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly,
            typeof(LeavePermissions).Assembly);
        services.AddLeaveServices(configuration);

        // Employee card PDF sections (API only; Employee collects every IEmployeeCardPdfSection).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.LeaveCardPdfSection>();
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.OvertimeCardPdfSection>();

        // "İzin Günleri" Excel import template "leave.days" (collected by the DataTransfer module).
        services.AddScoped<IImportTemplate, Application.Import.LeaveDaysImportTemplate>();
        services.AddScoped<IImportRowHandler, Application.Import.LeaveDaysImportRowHandler>();
    }
}
