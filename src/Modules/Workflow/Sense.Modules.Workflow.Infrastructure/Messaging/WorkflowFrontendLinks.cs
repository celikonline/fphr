using Microsoft.Extensions.Options;
using Sense.Modules.Workflow.Application;

namespace Sense.Modules.Workflow.Infrastructure.Messaging;

/// <summary>
/// Binds the shared "Frontend" configuration section (Sense.Shared.Contracts.Configuration.ConfigurationSections.Frontend
/// - the same section Identity's FrontendLinks/FrontendOptions binds for BaseUrl). Workflow.Infrastructure cannot
/// reference Identity.Infrastructure's FrontendOptions type directly (modules talk only via *.Contracts), so this
/// is a minimal local options type carrying only the BaseUrl property Workflow needs.
/// </summary>
public sealed class WorkflowFrontendOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Frontend links for email-driven decisions - mirrors Identity's FrontendLinks implementation shape.</summary>
public sealed class WorkflowFrontendLinks(IOptions<WorkflowFrontendOptions> options) : IWorkflowFrontendLinks
{
    private const string DecisionPathSegment = "/approve/";

    public string DecisionLink(string rawToken) =>
        string.Concat(options.Value.BaseUrl.TrimEnd('/'), DecisionPathSegment, Uri.EscapeDataString(rawToken));
}
