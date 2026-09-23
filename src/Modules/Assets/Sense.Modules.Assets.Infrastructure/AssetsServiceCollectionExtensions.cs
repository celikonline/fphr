using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Application.Categories;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Infrastructure.Documents;
using Sense.Modules.Assets.Infrastructure.Lookups;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Assets.Infrastructure;

public static class AssetsServiceCollectionExtensions
{
    /// <summary>
    /// Services Assets needs (repos, provisioning, unit of work, cross-module IAssetLookup/IAssetHandoverFormLookup).
    /// Used by both the API module (AssetsModule.AddModule) and Sense.Worker (so the outbox poller and its consumers
    /// resolve - including Documents' AssetAssignedDocumentRequirementHandler, which calls
    /// IAssetHandoverFormLookup.RenderAsync from the worker-side outbox poller, hence IAssetHandoverFormProvider
    /// must resolve there too), mirroring Expense's AddExpenseCoreServices.
    /// </summary>
    public static IServiceCollection AddAssetsCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IAssetCategoryRepository, AssetCategoryRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetsUnitOfWork>(sp => sp.GetRequiredService<AssetsDbContext>());
        services.AddScoped<AssetCategoryProvisioningService>();
        services.AddScoped<IAssetHandoverFormProvider, AssetHandoverFormProvider>();
        services.AddScoped<IAssetLookup, AssetLookup>();
        services.AddScoped<IAssetHandoverFormLookup, AssetHandoverFormLookup>();
        return services;
    }

    /// <summary>
    /// Assets' cross-module integration event handlers (faz4: EmployeeTerminated -> open-asset audit trail;
    /// TK-018: Workflow ApprovalCompleted/ApprovalRejected -> finish/cancel a pending "AssetAssignment"), registered
    /// explicitly for Sense.Worker (not the whole Application assembly - mirrors Documents' AddDocumentsIntegrationEventHandlers).
    /// </summary>
    public static IServiceCollection AddAssetsIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeTerminated>, EmployeeTerminatedAssetAuditHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, AssetAssignmentApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, AssetAssignmentApprovalRejectedHandler>();
        return services;
    }
}
