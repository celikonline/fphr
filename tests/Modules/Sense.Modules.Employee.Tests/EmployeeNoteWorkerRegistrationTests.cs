using Microsoft.Extensions.Configuration;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Infrastructure;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Infrastructure.DependencyInjection;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Sense.Worker is the only host that processes the outbox but does not scan Employee's Application assembly, so the
/// note attachment cleanup handler must be registered explicitly there (AddEmployeeIntegrationEventHandlers).
/// </summary>
public sealed class EmployeeNoteWorkerRegistrationTests
{
    private static ServiceProvider BuildWorkerLikeProvider(bool withEmployeeEventHandlers)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=localhost;Database=x;Username=x;Password=x",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        services.AddSingleton(NSubstitute.Substitute.For<IFileStorage>());
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);

        // Exactly what Sense.Worker's Program.cs registers for Employee: the Application assembly is NOT scanned.
        services.AddModuleHandlers(
            EmployeeDbContext.SchemaName,
            typeof(EmployeePermissions).Assembly,
            typeof(EmployeeDbContext).Assembly,
            typeof(IEmployeeRepository).Assembly);
        if (withEmployeeEventHandlers)
        {
            services.AddEmployeeIntegrationEventHandlers();
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void WorkerRegistration_ResolvesTheEmployeeNoteFileCleanupHandler()
    {
        using var provider = BuildWorkerLikeProvider(withEmployeeEventHandlers: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmployeeNoteFileOrphaned>>()
            .ShouldHaveSingleItem().ShouldBeOfType<EmployeeNoteFileCleanupHandler>();
    }

    [Fact]
    public void WithoutTheExplicitRegistration_NoHandlerWouldRunInTheWorker()
    {
        using var provider = BuildWorkerLikeProvider(withEmployeeEventHandlers: false);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmployeeNoteFileOrphaned>>().ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolvedHandler_DeletesTheStoredObject()
    {
        var storage = NSubstitute.Substitute.For<IFileStorage>();
        var tenantId = Guid.NewGuid();
        var handler = new EmployeeNoteFileCleanupHandler(storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmployeeNoteFileCleanupHandler>.Instance);

        await handler.Handle(new EmployeeNoteFileOrphaned(tenantId, "k/1"), CancellationToken.None);

        await storage.Received(1).DeleteFileAsync(tenantId, Domain.Employees.EmployeeNote.BucketName, "k/1", NSubstitute.Arg.Any<CancellationToken>());
    }
}
