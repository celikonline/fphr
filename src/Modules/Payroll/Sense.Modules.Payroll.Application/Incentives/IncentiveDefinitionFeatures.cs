using FluentValidation;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Incentives;

/// <summary>A rate as the API shows and receives it: a literal <c>value</c> XOR a <c>legislationKey</c>.</summary>
public sealed record IncentiveRateDto(decimal? Value, string? LegislationKey)
{
    internal IncentiveRate ToDomain() => new() { Value = Value, LegislationKey = LegislationKey };

    internal static IncentiveRateDto? From(IncentiveRate? rate) => rate is null ? null : new IncentiveRateDto(rate.Value, rate.LegislationKey);
}

/// <summary>Kind-specific parameters (same shape in the API and in the jsonb column).</summary>
public sealed record IncentiveParametersDto(
    IncentiveRateDto? Rate,
    IncentiveCoverageBase? CoverageBase,
    decimal? CapMultiplier,
    IncentiveRateDto? EmployerSgkRate,
    bool CoversEmployerSgk,
    bool CoversEmployerUnemployment,
    bool CoversEmployeeSgk,
    bool CoversEmployeeUnemployment)
{
    internal IncentiveParameters ToDomain() => new()
    {
        Rate = Rate?.ToDomain(),
        CoverageBase = CoverageBase,
        CapMultiplier = CapMultiplier,
        EmployerSgkRate = EmployerSgkRate?.ToDomain(),
        CoversEmployerSgk = CoversEmployerSgk,
        CoversEmployerUnemployment = CoversEmployerUnemployment,
        CoversEmployeeSgk = CoversEmployeeSgk,
        CoversEmployeeUnemployment = CoversEmployeeUnemployment,
    };

    internal static IncentiveParametersDto From(IncentiveParameters p) => new(
        IncentiveRateDto.From(p.Rate), p.CoverageBase, p.CapMultiplier, IncentiveRateDto.From(p.EmployerSgkRate),
        p.CoversEmployerSgk, p.CoversEmployerUnemployment, p.CoversEmployeeSgk, p.CoversEmployeeUnemployment);
}

public sealed record IncentiveDefinitionDto(
    Guid Id,
    string Code,
    string Name,
    string? LawReference,
    string? DeclarationCode,
    IncentiveKind Kind,
    IncentiveCategory Category,
    IncentiveParametersDto Parameters,
    int? MaxMonths,
    bool IsSmmmApproved,
    DateTimeOffset? ApprovedAt,
    Guid? ApprovedByUserId,
    string? ApprovedByName,
    string? ApprovalNote,
    bool IsComplete,
    bool IsActive,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string? TemplateKey);

internal static class IncentiveDefinitionMapper
{
    public static IncentiveDefinitionDto ToDto(IncentiveDefinition d, IReadOnlyDictionary<Guid, string>? approverNames = null) => new(
        d.Id, d.Code, d.Name, d.LawReference, d.DeclarationCode, d.Kind, d.Category, IncentiveParametersDto.From(d.Parameters), d.MaxMonths,
        d.IsSmmmApproved, d.ApprovedAt, d.ApprovedByUserId,
        d.ApprovedByUserId is { } approver && approverNames is not null && approverNames.TryGetValue(approver, out var name) ? name : null,
        d.ApprovalNote, d.IsComplete, d.IsActive, d.ValidFrom, d.ValidTo, d.TemplateKey);

    /// <summary>Display names of the approving users (a user no longer found simply has no name).</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ApproverNamesAsync(
        IUserProfileLookup users, Guid tenantId, IEnumerable<IncentiveDefinition> definitions, CancellationToken cancellationToken)
    {
        var ids = definitions.Where(d => d.ApprovedByUserId is not null).Select(d => d.ApprovedByUserId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var found = await users.GetAsync(tenantId, ids, cancellationToken);
        return found.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
    }
}

/// <summary>The legislation parameters an incentive rate may reference (PAY-INCENTIVE-PLAN "parameter-catalog").</summary>
public static class IncentiveParameterCatalog
{
    public static IReadOnlyList<string> Keys { get; } =
    [
        LegislationParameterKeys.PayrollSgkFloor,
        LegislationParameterKeys.PayrollSgkCeiling,
        LegislationParameterKeys.PayrollSgkEmployeeRate,
        LegislationParameterKeys.PayrollSgkEmployerRate,
        LegislationParameterKeys.PayrollSgkEmployerDiscountRate,
        LegislationParameterKeys.PayrollUnemploymentEmployeeRate,
        LegislationParameterKeys.PayrollUnemploymentEmployerRate,
        LegislationParameterKeys.PayrollMinimumWageGross,
    ];
}

/// <summary>Checks that every legislation key a definition references really exists (done on approval, not on saving a draft).</summary>
internal static class IncentiveLegislationKeys
{
    public static async Task<Result> VerifyExistAsync(IncentiveDefinition definition, ILegislationParameterReader legislation, CancellationToken cancellationToken)
    {
        var referenced = new[]
        {
            (Field: "rate", Key: definition.Parameters.Rate?.LegislationKey),
            (Field: "employerSgkRate", Key: definition.Parameters.EmployerSgkRate?.LegislationKey),
        }.Where(r => r.Key is not null).ToList();
        if (referenced.Count == 0)
        {
            return Result.Success();
        }

        var values = await legislation.ListAsync(referenced.Select(r => r.Key!).Distinct().ToList(), LegislationCountries.Turkey, cancellationToken);
        var existing = values.Where(v => v.DecimalValue is not null).Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var missing = referenced.FirstOrDefault(r => !existing.Contains(r.Key!));
        return missing.Key is null ? Result.Success() : Error.Validation(PayrollDomainErrors.IncentiveLegislationKeyInvalid, ("field", missing.Field));
    }
}

[RequiresPermission("payroll.trace.view")]
public sealed record ListIncentiveDefinitionsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<IncentiveDefinitionDto>>;

public sealed class ListIncentiveDefinitionsHandler(ITenantContext tenant, IIncentiveDefinitionRepository definitions, IUserProfileLookup users)
    : IQueryHandler<ListIncentiveDefinitionsQuery, IReadOnlyList<IncentiveDefinitionDto>>
{
    public async Task<Result<IReadOnlyList<IncentiveDefinitionDto>>> Handle(ListIncentiveDefinitionsQuery query, CancellationToken cancellationToken)
    {
        var list = await definitions.ListAsync(query.IncludeInactive, cancellationToken);
        var names = await IncentiveDefinitionMapper.ApproverNamesAsync(users, tenant.TenantId, list, cancellationToken);
        return list.Select(d => IncentiveDefinitionMapper.ToDto(d, names)).ToList();
    }
}

[RequiresPermission("payroll.trace.view")]
public sealed record GetIncentiveDefinitionQuery(Guid Id) : IQuery<IncentiveDefinitionDto>;

public sealed class GetIncentiveDefinitionHandler(ITenantContext tenant, IIncentiveDefinitionRepository definitions, IUserProfileLookup users)
    : IQueryHandler<GetIncentiveDefinitionQuery, IncentiveDefinitionDto>
{
    public async Task<Result<IncentiveDefinitionDto>> Handle(GetIncentiveDefinitionQuery query, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(query.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        return IncentiveDefinitionMapper.ToDto(definition, await IncentiveDefinitionMapper.ApproverNamesAsync(users, tenant.TenantId, [definition], cancellationToken));
    }
}

/// <summary>One entry of the in-code template catalog; <see cref="AlreadyAdded"/> is true once the tenant created its definition.</summary>
public sealed record IncentiveTemplateDto(
    string TemplateKey,
    string Code,
    string Name,
    string? LawReference,
    IncentiveKind Kind,
    string? DeclarationCode,
    bool PreApproved,
    bool AlreadyAdded);

[RequiresPermission("payroll.trace.view")]
public sealed record ListIncentiveTemplatesQuery : IQuery<IReadOnlyList<IncentiveTemplateDto>>;

public sealed class ListIncentiveTemplatesHandler(IIncentiveDefinitionRepository definitions)
    : IQueryHandler<ListIncentiveTemplatesQuery, IReadOnlyList<IncentiveTemplateDto>>
{
    public async Task<Result<IReadOnlyList<IncentiveTemplateDto>>> Handle(ListIncentiveTemplatesQuery query, CancellationToken cancellationToken)
    {
        var result = new List<IncentiveTemplateDto>();
        foreach (var template in IncentiveTemplates.All)
        {
            var added = await definitions.TemplateExistsAsync(template.TemplateKey, cancellationToken);
            result.Add(new IncentiveTemplateDto(
                template.TemplateKey, template.Code, template.Name, template.LawReference, template.Kind, template.DeclarationCode, template.PreApproved, added));
        }

        return result;
    }
}

public sealed record LegislationKeyValueDto(string Key, decimal? Value, DateOnly EffectiveFrom);

public sealed record IncentiveParameterCatalogDto(IReadOnlyList<LegislationKeyValueDto> LegislationKeys);

/// <summary>The legislation keys a rate may reference with their value in force today (keys without a value in force are left out).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record GetIncentiveParameterCatalogQuery : IQuery<IncentiveParameterCatalogDto>;

public sealed class GetIncentiveParameterCatalogHandler(ILegislationParameterReader legislation, TimeProvider clock)
    : IQueryHandler<GetIncentiveParameterCatalogQuery, IncentiveParameterCatalogDto>
{
    public async Task<Result<IncentiveParameterCatalogDto>> Handle(GetIncentiveParameterCatalogQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var values = await legislation.ListAsync(IncentiveParameterCatalog.Keys, LegislationCountries.Turkey, cancellationToken);
        var keys = IncentiveParameterCatalog.Keys
            .Select(key => values.Resolve(key, today))
            .OfType<LegislationParameterValue>()
            .Select(v => new LegislationKeyValueDto(v.Key, v.DecimalValue, v.EffectiveFrom))
            .ToList();
        return new IncentiveParameterCatalogDto(keys);
    }
}

/// <summary>Editable definition fields as the API receives them; ValidFrom defaults to today (UTC) on create and to the current value on update.</summary>
public sealed record IncentiveDefinitionInput(
    string Name,
    string? LawReference,
    string? DeclarationCode,
    IncentiveKind Kind,
    IncentiveParametersDto? Parameters,
    int? MaxMonths,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    internal IncentiveDefinitionData ToData(DateOnly defaultValidFrom) => new(
        Name, LawReference, DeclarationCode, Kind, Parameters?.ToDomain() ?? IncentiveParameters.Empty, MaxMonths, ValidFrom ?? defaultValidFrom, ValidTo);
}

[RequiresPermission("payroll.incentive.manage")]
public sealed record CreateIncentiveDefinitionCommand(string Code, IncentiveDefinitionInput Input) : ICommand<Guid>;

public sealed class CreateIncentiveDefinitionValidator : AbstractValidator<CreateIncentiveDefinitionCommand>
{
    public CreateIncentiveDefinitionValidator() => RuleFor(x => x.Input).NotNull();
}

public sealed class CreateIncentiveDefinitionHandler(ITenantContext tenant, IIncentiveDefinitionRepository definitions, TimeProvider clock)
    : ICommandHandler<CreateIncentiveDefinitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateIncentiveDefinitionCommand command, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var created = IncentiveDefinition.Create(tenant.TenantId, command.Code, command.Input.ToData(today));
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (await definitions.CodeExistsAsync(created.Value.Code, cancellationToken))
        {
            return Error.Conflict(PayrollDomainErrors.IncentiveCodeTaken);
        }

        definitions.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Creates a definition from the in-code catalog. Once per tenant (409); pre-approved templates (5510) are approved by the system.</summary>
[RequiresPermission("payroll.incentive.manage")]
public sealed record CreateIncentiveFromTemplateCommand(string TemplateKey) : ICommand<Guid>;

public sealed class CreateIncentiveFromTemplateValidator : AbstractValidator<CreateIncentiveFromTemplateCommand>
{
    public CreateIncentiveFromTemplateValidator() => RuleFor(x => x.TemplateKey).NotEmpty();
}

public sealed class CreateIncentiveFromTemplateHandler(ITenantContext tenant, IIncentiveDefinitionRepository definitions, TimeProvider clock)
    : ICommandHandler<CreateIncentiveFromTemplateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateIncentiveFromTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = IncentiveTemplates.Find(command.TemplateKey);
        if (template is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveTemplateNotFound);
        }

        if (await definitions.TemplateExistsAsync(template.TemplateKey, cancellationToken))
        {
            return Error.Conflict(PayrollDomainErrors.IncentiveTemplateAlreadyAdded);
        }

        if (await definitions.CodeExistsAsync(template.Code, cancellationToken))
        {
            return Error.Conflict(PayrollDomainErrors.IncentiveCodeTaken);
        }

        var now = clock.GetUtcNow();
        var created = IncentiveDefinition.CreateFromTemplate(tenant.TenantId, template, DateOnly.FromDateTime(now.UtcDateTime), now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        definitions.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Edits everything except the code. A calculation-relevant change (kind, parameters, declaration code, max months, dates) drops the approval.</summary>
[RequiresPermission("payroll.incentive.manage")]
public sealed record UpdateIncentiveDefinitionCommand(Guid Id, IncentiveDefinitionInput Input) : ICommand;

public sealed class UpdateIncentiveDefinitionValidator : AbstractValidator<UpdateIncentiveDefinitionCommand>
{
    public UpdateIncentiveDefinitionValidator() => RuleFor(x => x.Input).NotNull();
}

public sealed class UpdateIncentiveDefinitionHandler(
    IIncentiveDefinitionRepository definitions, IEmployeeIncentiveAssignmentRepository assignments, IncentiveChangeFlagger flagger)
    : ICommandHandler<UpdateIncentiveDefinitionCommand>
{
    public async Task<Result> Handle(UpdateIncentiveDefinitionCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        var data = command.Input.ToData(definition.ValidFrom);

        // Assignments rely on the category (overlap rule) and, in the SGK category, on the declaration code.
        if (await assignments.ListByDefinitionAsync(definition.Id, DateOnly.MinValue, cancellationToken) is { Count: > 0 })
        {
            if (Enum.IsDefined(data.Kind) && IncentiveDefinition.CategoryOf(data.Kind) != definition.Category)
            {
                return Error.Conflict(PayrollDomainErrors.IncentiveKindCategoryLocked);
            }

            if (definition.Category == IncentiveCategory.Sgk && string.IsNullOrWhiteSpace(data.DeclarationCode))
            {
                return Error.Validation(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
            }
        }

        var updated = definition.Update(data);
        if (updated.IsFailure)
        {
            return updated;
        }

        await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Approval is a declaration flag by the tenant's SMMM: who, when and a note are recorded; only a complete definition whose legislation keys exist can be approved.</summary>
[RequiresPermission("payroll.incentive.approve")]
public sealed record ApproveIncentiveDefinitionCommand(Guid Id, string? Note) : ICommand;

public sealed class ApproveIncentiveDefinitionHandler(
    IIncentiveDefinitionRepository definitions,
    ILegislationParameterReader legislation,
    ICurrentUser currentUser,
    IncentiveChangeFlagger flagger,
    TimeProvider clock) : ICommandHandler<ApproveIncentiveDefinitionCommand>
{
    public async Task<Result> Handle(ApproveIncentiveDefinitionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        // An incomplete definition reports its first missing field from Approve itself; keys are only looked up once everything is filled in.
        if (definition.IsComplete)
        {
            var keys = await IncentiveLegislationKeys.VerifyExistAsync(definition, legislation, cancellationToken);
            if (keys.IsFailure)
            {
                return keys;
            }
        }

        var approved = definition.Approve(userId, clock.GetUtcNow(), command.Note);
        if (approved.IsFailure)
        {
            return approved;
        }

        await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        return Result.Success();
    }
}

[RequiresPermission("payroll.incentive.approve")]
public sealed record RevokeIncentiveApprovalCommand(Guid Id, string? Note) : ICommand;

public sealed class RevokeIncentiveApprovalHandler(IIncentiveDefinitionRepository definitions, IncentiveChangeFlagger flagger)
    : ICommandHandler<RevokeIncentiveApprovalCommand>
{
    public async Task<Result> Handle(RevokeIncentiveApprovalCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        var revoked = definition.RevokeApproval(command.Note);
        if (revoked.IsFailure)
        {
            return revoked;
        }

        await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        return Result.Success();
    }
}

[RequiresPermission("payroll.incentive.manage")]
public sealed record ActivateIncentiveDefinitionCommand(Guid Id) : ICommand;

public sealed class ActivateIncentiveDefinitionHandler(IIncentiveDefinitionRepository definitions, IncentiveChangeFlagger flagger)
    : ICommandHandler<ActivateIncentiveDefinitionCommand>
{
    public async Task<Result> Handle(ActivateIncentiveDefinitionCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        if (!definition.IsActive)
        {
            definition.Activate();
            await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        }

        return Result.Success();
    }
}

[RequiresPermission("payroll.incentive.manage")]
public sealed record DeactivateIncentiveDefinitionCommand(Guid Id) : ICommand;

public sealed class DeactivateIncentiveDefinitionHandler(IIncentiveDefinitionRepository definitions, IncentiveChangeFlagger flagger)
    : ICommandHandler<DeactivateIncentiveDefinitionCommand>
{
    public async Task<Result> Handle(DeactivateIncentiveDefinitionCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        if (definition.IsActive)
        {
            definition.Deactivate();
            await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        }

        return Result.Success();
    }
}
