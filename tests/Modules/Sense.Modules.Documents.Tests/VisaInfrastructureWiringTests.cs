using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Documents.Infrastructure;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Storage;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>
/// Regression tests for two things mock-based handler tests cannot see: (1) every constructor dependency of the visa
/// services/handlers is registered (or is one of the known host-provided services), which is what Sense.Worker needs so a
/// missing port cannot break every ApprovalCompleted delivery; (2) the jsonb template list is change-tracked by value.
/// </summary>
public sealed class VisaInfrastructureWiringTests
{
    // Provided by the host (AddSenseCore / AddFileStorage / module DbContext registration), never by Documents itself.
    private static readonly HashSet<Type> HostProvided =
    [
        typeof(ITenantContext),
        typeof(ICurrentUser),
        typeof(IFileStorage),
        typeof(DocumentsDbContext),
        typeof(TimeProvider),
    ];

    private static ServiceCollection DocumentsRegistrations()
    {
        var services = new ServiceCollection();
        services.AddDocumentsCoreServices();
        services.AddDocumentsIntegrationEventHandlers();
        return services;
    }

    [Fact]
    public void EveryVisaServiceAndHandler_HasAllItsConstructorDependenciesRegistered()
    {
        var services = DocumentsRegistrations();
        var registered = services.Select(d => d.ServiceType).ToHashSet();
        var visaImplementations = services
            .Where(d => d.ImplementationType is { Namespace: not null } type && type.Namespace.Contains("Visa", StringComparison.Ordinal))
            .Select(d => d.ImplementationType!)
            .Distinct()
            .ToList();

        visaImplementations.ShouldNotBeEmpty();

        var missing = visaImplementations
            .SelectMany(type => type.GetConstructors().Single().GetParameters().Select(p => (Type: type, Dependency: p.ParameterType)))
            .Where(x => !registered.Contains(x.Dependency)
                && !HostProvided.Contains(x.Dependency)
                && !(x.Dependency.IsGenericType && x.Dependency.GetGenericTypeDefinition() == typeof(ILogger<>)))
            .Select(x => $"{x.Type.Name} needs {x.Dependency.Name}")
            .ToList();

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void TheApprovalHandlersAreRegisteredForBothWorkflowDecisions()
    {
        var registered = DocumentsRegistrations().Select(d => (d.ServiceType, d.ImplementationType)).ToList();

        registered.ShouldContain((typeof(IIntegrationEventHandler<ApprovalCompleted>), typeof(VisaApprovalCompletedHandler)));
        registered.ShouldContain((typeof(IIntegrationEventHandler<ApprovalRejected>), typeof(VisaApprovalRejectedHandler)));
    }

    [Fact]
    public void TemplateDocumentIds_AreChangeTrackedByValue_NotByReference()
    {
        var tenant = Substitute.For<ITenantContext>();
        var options = new DbContextOptionsBuilder<DocumentsDbContext>().UseNpgsql("Host=localhost;Database=unused").Options;
        using var context = new DocumentsDbContext(options, tenant);
        var comparer = context.Model
            .FindEntityType(typeof(VisaSettings))!
            .FindProperty(nameof(VisaSettings.TemplateDocumentIds))!
            .GetValueComparer()!;
        var settings = VisaSettings.CreateDefault(Guid.NewGuid());
        var before = Guid.NewGuid();
        settings.SetTemplateDocumentIds([before]);

        // What EF stores when the entity is first tracked, then the same instance is mutated in place by the domain method.
        var snapshot = comparer.Snapshot(settings.TemplateDocumentIds);
        settings.SetTemplateDocumentIds([before, Guid.NewGuid()]);

        comparer.Equals(snapshot, settings.TemplateDocumentIds).ShouldBeFalse();
    }
}
