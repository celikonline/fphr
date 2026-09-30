using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Application.PayslipImports;
using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Infrastructure.Lookups;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Documents.Infrastructure.Persistence.Visa;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Infrastructure;

public static class DocumentsServiceCollectionExtensions
{
    /// <summary>
    /// Services Documents' integration event handler needs (catalog repos, provisioning, materialization), without
    /// depending on other modules' Infrastructure. Used by both the API module (DocumentsModule.AddModule) and
    /// Sense.Worker, mirroring Leave's/Attendance's AddXCoreServices.
    /// </summary>
    public static IServiceCollection AddDocumentsCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IDocumentsUnitOfWork>(sp => sp.GetRequiredService<DocumentsDbContext>());
        services.AddScoped<IDocumentTypeDefinitionRepository, DocumentTypeDefinitionRepository>();
        services.AddScoped<IEmployeeDocumentRequirementRepository, EmployeeDocumentRequirementRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        // Denetim A5-belge: Leave taleplerine eklenen belgeleri doğrular (LeaveRequestRules); her iki host'ta da
        // DocumentsDbContext kayıtlı olduğundan (Api: DocumentsModule, Worker: Program.cs) burada kayıt yeterli.
        services.AddScoped<IDocumentAttachmentLookup, Lookups.DocumentAttachmentLookup>();
        services.AddScoped<DocumentTypeProvisioningService>();
        services.AddScoped<DocumentRequirementMaterializationService>();
        // Visa document requests (senseik-001): the approval handlers registered by AddDocumentsIntegrationEventHandlers run
        // in Sense.Worker, so everything they (and the API handlers) depend on is registered here for both hosts. The
        // delivery service needs IFileStorage, which Sense.Worker gets from AddFileStorage.
        services.AddScoped<IVisaDocumentRequestRepository, VisaDocumentRequestRepository>();
        services.AddScoped<IVisaDocumentProcessRepository, VisaDocumentProcessRepository>();
        services.AddScoped<IVisaSettingsRepository, VisaSettingsRepository>();
        services.AddScoped<IVisaSettingsProvider, VisaSettingsProvider>();
        services.AddScoped<IVisaDocumentDeliveryService, VisaDocumentDeliveryService>();
        // Generic cross-module document delivery (Assets' zimmet tutanağı and any future module-generated file);
        // also needs IFileStorage, same as the visa delivery service above.
        services.AddScoped<ISystemDocumentDeliveryService, SystemDocumentDeliveryService>();
        // E3/E8 (B13): shared file type catalog, external payslip bulk import, shared-file zip download.
        services.AddScoped<ISharedFileTypeRepository, SharedFileTypeRepository>();
        services.AddScoped<IPayslipEmployeeResolver, PayslipEmployeeResolver>();
        services.AddScoped<PayslipFileMatcher>();
        services.AddHttpClient(SharedFileContentReader.HttpClientName);
        services.AddScoped<ISharedFileContentReader, SharedFileContentReader>();
        // IReportProvider (MissingDocumentReportProvider) is registered in DocumentsModule.AddModule, not here: it
        // needs IStringLocalizer<SharedResource>, which only the API host (AddSenseWeb -> AddLocalization) provides.
        return services;
    }

    /// <summary>
    /// Documents' cross-module integration event handlers (EmployeeHired, EmployeeProfileChanged, and Assets'
    /// AssetAssigned/AssetReturned - roadmap #65), registered explicitly for Sense.Worker (not the whole Application
    /// assembly, so API-only handler dependencies stay out).
    /// </summary>
    public static IServiceCollection AddDocumentsIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredDocumentRequirementHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeProfileChanged>, EmployeeProfileChangedDocumentRequirementHandler>();
        services.AddScoped<IIntegrationEventHandler<AssetAssigned>, AssetAssignedDocumentRequirementHandler>();
        services.AddScoped<IIntegrationEventHandler<AssetReturned>, AssetReturnedDocumentRequirementHandler>();
        // Workflow decisions on "VisaDocument" requests (visa document request approval, senseik-001).
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, VisaApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, VisaApprovalRejectedHandler>();
        return services;
    }
}
