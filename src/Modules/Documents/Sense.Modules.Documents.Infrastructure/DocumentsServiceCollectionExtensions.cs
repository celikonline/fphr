using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
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
        services.AddScoped<DocumentTypeProvisioningService>();
        services.AddScoped<DocumentRequirementMaterializationService>();
        return services;
    }

    /// <summary>
    /// Documents' cross-module integration event handlers (EmployeeHired, and Assets' AssetAssigned/AssetReturned -
    /// roadmap #65), registered explicitly for Sense.Worker (not the whole Application assembly, so API-only handler
    /// dependencies stay out).
    /// </summary>
    public static IServiceCollection AddDocumentsIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredDocumentRequirementHandler>();
        services.AddScoped<IIntegrationEventHandler<AssetAssigned>, AssetAssignedDocumentRequirementHandler>();
        services.AddScoped<IIntegrationEventHandler<AssetReturned>, AssetReturnedDocumentRequirementHandler>();
        return services;
    }
}
