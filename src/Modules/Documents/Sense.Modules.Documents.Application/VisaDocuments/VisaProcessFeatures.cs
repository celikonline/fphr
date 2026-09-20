using FluentValidation;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>Read model for a visa document process (employee selection list and the admin definition list).</summary>
public sealed record VisaDocumentProcessDto(Guid Id, string Name, string? CountryCode, bool IsActive);

internal static class VisaProcessProvisioning
{
    /// <summary>Default process templates seeded once per tenant on first access (Turkish display names, like every
    /// other tenant-owned catalog seed - a tenant renames/deactivates them from the admin page). Country code null =
    /// not tied to a single country (Schengen covers many).</summary>
    private static readonly (string Name, string? CountryCode)[] Defaults =
    [
        ("Schengen vizesi", null),
        ("Birleşik Krallık vizesi", "GB"),
        ("ABD vizesi", "US"),
        ("Diğer ülkeler", null),
    ];

    public static async Task EnsureSeededAsync(
        IVisaDocumentProcessRepository processes, Guid tenantId, IDocumentsUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (await processes.AnyAsync(tenantId, cancellationToken))
        {
            return;
        }

        foreach (var (name, countryCode) in Defaults)
        {
            await processes.AddAsync(VisaDocumentProcess.Create(tenantId, name, countryCode).Value, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public static VisaDocumentProcessDto ToDto(VisaDocumentProcess p) => new(p.Id, p.Name, p.CountryCode, p.IsActive);

    public static string? NormalizeCountry(string? countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
}

/// <summary>Lists the tenant's active visa document processes for the request form's selection list, seeding the
/// default templates on first access.</summary>
[RequiresPermission("documents.my.visa_requests")]
public sealed record ListVisaDocumentProcessesQuery : IQuery<IReadOnlyList<VisaDocumentProcessDto>>;

public sealed class ListVisaDocumentProcessesHandler(IVisaDocumentProcessRepository processes, ITenantContext tenant, IDocumentsUnitOfWork unitOfWork)
    : IQueryHandler<ListVisaDocumentProcessesQuery, IReadOnlyList<VisaDocumentProcessDto>>
{
    public async Task<Result<IReadOnlyList<VisaDocumentProcessDto>>> Handle(ListVisaDocumentProcessesQuery query, CancellationToken cancellationToken)
    {
        await VisaProcessProvisioning.EnsureSeededAsync(processes, tenant.TenantId, unitOfWork, cancellationToken);
        var items = await processes.GetActiveAsync(tenant.TenantId, cancellationToken);
        return Result.Success<IReadOnlyList<VisaDocumentProcessDto>>(items.Select(VisaProcessProvisioning.ToDto).ToList());
    }
}

/// <summary>Admin definition list: every process including inactive ones ("Vize belge süreçleri" settings page).</summary>
[RequiresPermission("documents.visa.manage")]
public sealed record ListAllVisaDocumentProcessesQuery : IQuery<IReadOnlyList<VisaDocumentProcessDto>>;

public sealed class ListAllVisaDocumentProcessesHandler(IVisaDocumentProcessRepository processes, ITenantContext tenant, IDocumentsUnitOfWork unitOfWork)
    : IQueryHandler<ListAllVisaDocumentProcessesQuery, IReadOnlyList<VisaDocumentProcessDto>>
{
    public async Task<Result<IReadOnlyList<VisaDocumentProcessDto>>> Handle(ListAllVisaDocumentProcessesQuery query, CancellationToken cancellationToken)
    {
        await VisaProcessProvisioning.EnsureSeededAsync(processes, tenant.TenantId, unitOfWork, cancellationToken);
        var items = await processes.GetAllAsync(tenant.TenantId, cancellationToken);
        return Result.Success<IReadOnlyList<VisaDocumentProcessDto>>(items.Select(VisaProcessProvisioning.ToDto).ToList());
    }
}

[RequiresPermission("documents.visa.manage")]
public sealed record CreateVisaDocumentProcessCommand(string Name, string? CountryCode) : ICommand<VisaDocumentProcessDto>;

public sealed class CreateVisaDocumentProcessValidator : AbstractValidator<CreateVisaDocumentProcessCommand>
{
    public CreateVisaDocumentProcessValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(VisaLimits.ProcessNameMaxLength);
        RuleFor(x => x.CountryCode).Length(VisaLimits.CountryCodeMinLength, VisaLimits.CountryCodeMaxLength)
            .Must(code => code!.All(char.IsAsciiLetter))
            .When(x => !string.IsNullOrWhiteSpace(x.CountryCode))
            .WithErrorCode(VisaDomainErrors.VisaDocumentRequestInvalidCountryCode);
    }
}

public sealed class CreateVisaDocumentProcessHandler(IVisaDocumentProcessRepository processes, ITenantContext tenant)
    : ICommandHandler<CreateVisaDocumentProcessCommand, VisaDocumentProcessDto>
{
    public async Task<Result<VisaDocumentProcessDto>> Handle(CreateVisaDocumentProcessCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        if (await processes.NameExistsAsync(tenant.TenantId, name, null, cancellationToken))
        {
            return Error.Conflict(VisaDomainErrors.VisaDocumentProcessAlreadyExists);
        }

        var created = VisaDocumentProcess.Create(tenant.TenantId, name, VisaProcessProvisioning.NormalizeCountry(command.CountryCode));
        if (created.IsFailure)
        {
            return created.Error;
        }

        await processes.AddAsync(created.Value, cancellationToken);
        return VisaProcessProvisioning.ToDto(created.Value);
    }
}

[RequiresPermission("documents.visa.manage")]
public sealed record UpdateVisaDocumentProcessCommand(Guid Id, string Name, string? CountryCode) : ICommand<VisaDocumentProcessDto>;

public sealed class UpdateVisaDocumentProcessValidator : AbstractValidator<UpdateVisaDocumentProcessCommand>
{
    public UpdateVisaDocumentProcessValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(VisaLimits.ProcessNameMaxLength);
        RuleFor(x => x.CountryCode).Length(VisaLimits.CountryCodeMinLength, VisaLimits.CountryCodeMaxLength)
            .Must(code => code!.All(char.IsAsciiLetter))
            .When(x => !string.IsNullOrWhiteSpace(x.CountryCode))
            .WithErrorCode(VisaDomainErrors.VisaDocumentRequestInvalidCountryCode);
    }
}

public sealed class UpdateVisaDocumentProcessHandler(IVisaDocumentProcessRepository processes, ITenantContext tenant)
    : ICommandHandler<UpdateVisaDocumentProcessCommand, VisaDocumentProcessDto>
{
    public async Task<Result<VisaDocumentProcessDto>> Handle(UpdateVisaDocumentProcessCommand command, CancellationToken cancellationToken)
    {
        if (await processes.GetByIdAsync(command.Id, cancellationToken) is not { } process)
        {
            return Error.NotFound(VisaDomainErrors.VisaDocumentProcessNotFound);
        }

        var name = command.Name.Trim();
        if (await processes.NameExistsAsync(tenant.TenantId, name, process.Id, cancellationToken))
        {
            return Error.Conflict(VisaDomainErrors.VisaDocumentProcessAlreadyExists);
        }

        var updated = process.Update(name, VisaProcessProvisioning.NormalizeCountry(command.CountryCode));
        return updated.IsFailure ? updated.Error : VisaProcessProvisioning.ToDto(process);
    }
}

/// <summary>Activates/deactivates a process. Deactivated processes disappear from the request form; existing requests
/// that reference them are unaffected.</summary>
[RequiresPermission("documents.visa.manage")]
public sealed record SetVisaDocumentProcessActiveCommand(Guid Id, bool IsActive) : ICommand<VisaDocumentProcessDto>;

public sealed class SetVisaDocumentProcessActiveHandler(IVisaDocumentProcessRepository processes)
    : ICommandHandler<SetVisaDocumentProcessActiveCommand, VisaDocumentProcessDto>
{
    public async Task<Result<VisaDocumentProcessDto>> Handle(SetVisaDocumentProcessActiveCommand command, CancellationToken cancellationToken)
    {
        if (await processes.GetByIdAsync(command.Id, cancellationToken) is not { } process)
        {
            return Error.NotFound(VisaDomainErrors.VisaDocumentProcessNotFound);
        }

        if (command.IsActive)
        {
            process.Reactivate();
        }
        else
        {
            process.Deactivate();
        }

        return VisaProcessProvisioning.ToDto(process);
    }
}
