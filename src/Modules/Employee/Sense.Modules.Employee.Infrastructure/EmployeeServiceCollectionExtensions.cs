using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Employee.Infrastructure;

public static class EmployeeServiceCollectionExtensions
{
    /// <summary>
    /// Employee's own integration event handlers, registered explicitly for Sense.Worker (the only outbox processor; it does
    /// not scan the Application assembly). Needs IFileStorage, which the Worker registers via AddFileStorage.
    /// </summary>
    public static IServiceCollection AddEmployeeIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeNoteFileOrphaned>, EmployeeNoteFileCleanupHandler>();
        return services;
    }
}
