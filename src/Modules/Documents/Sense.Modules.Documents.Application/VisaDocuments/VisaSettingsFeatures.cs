using FluentValidation;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>Read model for the tenant's visa settings.</summary>
public sealed record VisaSettingsDto(Guid? ApproverEmployeeId, IReadOnlyList<Guid> TemplateDocumentIds);

internal static class VisaSettingsMapper
{
    public static VisaSettingsDto ToDto(VisaSettings settings) =>
        new(settings.ApproverEmployeeId, settings.TemplateDocumentIds);
}

/// <summary>Ensures the tenant's single VisaSettings row exists (lazy default seed, mirrors Attendance's
/// ShiftSettingsProvisioning.EnsureAsync).</summary>
internal static class VisaSettingsProvisioning
{
    public static async Task<VisaSettings> EnsureAsync(
        IVisaSettingsRepository settings,
        Guid tenantId,
        IDocumentsUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var existing = await settings.FindAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var seeded = VisaSettings.CreateDefault(tenantId);
        settings.Add(seeded);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return seeded;
    }
}

/// <summary>Reads the tenant's visa settings (approver + template documents), seeding a default row on first
/// access. Same permission as managing them - there is no separate "view visa settings" concern (only the settings
/// page reads this).</summary>
[RequiresPermission("documents.visa.manage")]
public sealed record GetVisaSettingsQuery : IQuery<VisaSettingsDto>;

public sealed class GetVisaSettingsHandler(IVisaSettingsRepository settings, ITenantContext tenant, IDocumentsUnitOfWork unitOfWork)
    : IQueryHandler<GetVisaSettingsQuery, VisaSettingsDto>
{
    public async Task<Result<VisaSettingsDto>> Handle(GetVisaSettingsQuery query, CancellationToken cancellationToken) =>
        VisaSettingsMapper.ToDto(await VisaSettingsProvisioning.EnsureAsync(settings, tenant.TenantId, unitOfWork, cancellationToken));
}

/// <summary>Configures the tenant's visa approver and delivered template documents (Settings page - "Vize
/// süreçleri" per the handoff doc's Web Layer Tasks). Both fields are optional to allow clearing them; there is no
/// validation on TemplateDocumentIds existing as real Document rows here - a stale id is simply skipped at delivery
/// time (<c>IVisaDocumentDeliveryService</c>), the same tolerant approach as a deactivated DocumentType.</summary>
[RequiresPermission("documents.visa.manage")]
public sealed record UpdateVisaSettingsCommand(Guid? ApproverEmployeeId, IReadOnlyList<Guid> TemplateDocumentIds) : ICommand<VisaSettingsDto>;

public sealed class UpdateVisaSettingsHandler(
    IVisaSettingsRepository settings, ITenantContext tenant, IDocumentsUnitOfWork unitOfWork, IDocumentRepository documents)
    : ICommandHandler<UpdateVisaSettingsCommand, VisaSettingsDto>
{
    public async Task<Result<VisaSettingsDto>> Handle(UpdateVisaSettingsCommand command, CancellationToken cancellationToken)
    {
        // Every template is copied into each requester's folder, so it must be a plain, active company document - never
        // another employee's file or an earlier delivered copy.
        foreach (var templateId in command.TemplateDocumentIds.Where(id => id != Guid.Empty).Distinct())
        {
            var template = await documents.GetByIdAsync(templateId, cancellationToken);
            if (template.IsFailure
                || template.Value.Status != DocumentStatus.Active
                || template.Value.EmployeeId is not null
                || template.Value.VisaDocumentRequestId is not null)
            {
                return Error.Validation(VisaDomainErrors.VisaSettingsTemplateInvalid);
            }
        }

        var current = await VisaSettingsProvisioning.EnsureAsync(settings, tenant.TenantId, unitOfWork, cancellationToken);
        current.SetApprover(command.ApproverEmployeeId);
        current.SetTemplateDocumentIds(command.TemplateDocumentIds);
        return VisaSettingsMapper.ToDto(current);
    }
}

/// <summary>Bounds the template list: it is a handful of documents (passport copy checklist, invitation letter
/// template, ...), and every one is copied per approved request.</summary>
public sealed class UpdateVisaSettingsValidator : AbstractValidator<UpdateVisaSettingsCommand>
{
    public const int MaxTemplateDocuments = 20;

    public UpdateVisaSettingsValidator()
    {
        RuleFor(x => x.TemplateDocumentIds).NotNull().Must(ids => ids.Count <= MaxTemplateDocuments);
    }
}
