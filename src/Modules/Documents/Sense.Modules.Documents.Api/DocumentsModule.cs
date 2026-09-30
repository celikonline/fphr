using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Modules.Documents.Infrastructure;
using Sense.Modules.Documents.Infrastructure.BackgroundJobs;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Documents.Api;

/// <summary>Documents module: file storage, uploads, downloads with presigned URLs and tenant isolation.</summary>
public sealed class DocumentsModule : IModule
{
    public string Name => DocumentsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(DocumentsModule).Assembly,
        typeof(DocumentsPermissions).Assembly,
        typeof(DocumentsDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => DocumentsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Register DbContext for this module
        // Audit trail for IAuditLogged aggregates - same wiring as LeaveModule/ExpenseModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<DocumentsDbContext>(
            configuration, DocumentsDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<DocumentsDbContext>>();

        // Register MediatR handlers for this module
        services.AddModuleHandlers(DocumentsDbContext.SchemaName, typeof(Application.DocumentsErrors).Assembly, typeof(DocumentsDbContext).Assembly);

        // Shared company files (senseik-028)
        services.AddScoped<ISharedCompanyFileRepository, SharedCompanyFileRepository>();

        // Document/visa/document type catalog + requirement tracker repos, provisioning/materialization services, unit of work
        services.AddDocumentsCoreServices();

        // Employee card PDF section (API only).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.DocumentsCardPdfSection>();

        // Reporting catalog provider (API host only, mirrors EmployeeModule): it takes IStringLocalizer<SharedResource>,
        // which Sense.Worker never registers, so it must stay out of AddDocumentsCoreServices.
        services.AddScoped<Sense.Modules.Reporting.Contracts.IReportProvider, Infrastructure.Reporting.MissingDocumentReportProvider>();

        // ADR-0004: Hangfire (BackgroundJobExecutor) runs in Sense.Api, so scheduled-job handlers are registered
        // here, not in AddDocumentsIntegrationEventHandlers (Sense.Worker-only, for outbox event consumers).
        services.AddScoped<IScheduledJobHandler, DocumentExpiryReminderJobHandler>();
    }
}
