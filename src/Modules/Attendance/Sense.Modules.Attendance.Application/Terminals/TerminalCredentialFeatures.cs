using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// Çalışanın terminal kimliği listede: yalnız tür, durum ve oluşturulma anı. Kimlik değeri ya da özeti ASLA dönmez; saklanan HMAC özetinden
/// son haneler türetilemez ve şema değişmediği için "son 2 hane" alanı yoktur (istemci maske gösterir).
/// </summary>
public sealed record TerminalCredentialDto(Guid Id, Guid EmployeeId, TerminalCredentialKind Kind, bool IsActive, DateTime CreatedAt);

/// <summary>Bir çalışanın terminal kimlikleri (Z5.4), türe göre.</summary>
[RequiresPermission("attendance.terminal.view")]
public sealed record ListEmployeeTerminalCredentialsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<TerminalCredentialDto>>;

public sealed class ListEmployeeTerminalCredentialsHandler(ITerminalRepository terminals)
    : IQueryHandler<ListEmployeeTerminalCredentialsQuery, IReadOnlyList<TerminalCredentialDto>>
{
    public async Task<Result<IReadOnlyList<TerminalCredentialDto>>> Handle(ListEmployeeTerminalCredentialsQuery query, CancellationToken cancellationToken) =>
        (await terminals.ListCredentialsForEmployeeAsync(query.EmployeeId, cancellationToken))
            .Select(c => new TerminalCredentialDto(c.Id, c.EmployeeId, c.Kind, c.IsActive, c.CreatedAt))
            .ToList();
}

/// <summary>
/// Çalışana kimlik tanımlar (Z5.4): değer biçim denetiminden sonra HMAC ile özetlenir, yalnız özet saklanır. Pepper yoksa 503. Aynı kimlik
/// zaten bu çalışandaysa mevcut kaydın kimliği döner (pasifse yeniden etkinleşir); başka çalışanda aktifse 409 (sahip bilgisi yok), pasifse bu
/// çalışana devredilir. <see cref="ToString"/> değeri yazmaz.
/// </summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record AddEmployeeTerminalCredentialCommand(Guid EmployeeId, TerminalCredentialKind Kind, string? Value) : ICommand<Guid>
{
    public override string ToString() =>
        $"{nameof(AddEmployeeTerminalCredentialCommand)} {{ {nameof(EmployeeId)} = {EmployeeId}, {nameof(Kind)} = {Kind} }}";
}

public sealed class AddEmployeeTerminalCredentialHandler(
    ITerminalRepository terminals, ITerminalCredentialHasher hasher, IEmployeeContactLookup employees, ITenantContext tenant)
    : ICommandHandler<AddEmployeeTerminalCredentialCommand, Guid>
{
    private const string TerminatedStatus = "Terminated";

    public async Task<Result<Guid>> Handle(AddEmployeeTerminalCredentialCommand command, CancellationToken cancellationToken)
    {
        if (!hasher.IsAvailable)
        {
            return Error.ServiceUnavailable(TerminalErrors.CredentialPepperMissing);
        }

        var value = command.Value?.Trim();
        if (!Enum.IsDefined(command.Kind) || string.IsNullOrEmpty(value))
        {
            return Error.Validation(AttendanceDomainErrors.TerminalCredentialValueRequired);
        }

        if (TerminalCredentialRules.FormatError(command.Kind, value) is { } formatError)
        {
            return Error.Validation(formatError);
        }

        var employee = (await employees.GetContactsAsync(tenant.TenantId, [command.EmployeeId], cancellationToken))
            .FirstOrDefault(e => e.EmployeeId == command.EmployeeId);
        if (employee is null)
        {
            return Error.NotFound(TerminalCredentialErrors.EmployeeNotFound);
        }

        if (employee.Status == TerminatedStatus)
        {
            return Error.Rule(TerminalCredentialErrors.EmployeeNotActive);
        }

        var digest = hasher.Hash(tenant.TenantId, command.Kind, value);
        if (digest.IsFailure)
        {
            return digest.Error;
        }

        var existing = (await terminals.ListCredentialsByValuesAsync([digest.Value], cancellationToken)).FirstOrDefault(c => c.Kind == command.Kind);
        var assigned = TerminalCredentialRules.Assign(terminals, tenant.TenantId, command.EmployeeId, command.Kind, digest.Value, existing);
        return assigned.IsFailure ? assigned.Error : assigned.Value.Credential.Id;
    }
}

/// <summary>
/// Pasif kimliği AYNI çalışanda yeniden etkinleştirir (F15 "aktif et"); özet değişmez, idempotent. İşten ayrılmış çalışanın kimliği
/// etkinleştirilemez (<see cref="TerminalCredentialErrors.EmployeeNotActive"/>). Aynı (tür, özet) başka çalışanda aktifse ekleme kuralı:
/// kart/harici <see cref="AttendanceDomainErrors.TerminalCredentialInUse"/>, PIN genel <see cref="TerminalCredentialErrors.Rejected"/> (tekil indeks
/// bunu normalde engeller; savunma amaçlı). Başka çalışana devir bu uçla değil, aynı değerle ekleme/içe aktarmayla yapılır.
/// </summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record ActivateEmployeeTerminalCredentialCommand(Guid CredentialId) : ICommand;

public sealed class ActivateEmployeeTerminalCredentialHandler(ITerminalRepository terminals, IEmployeeContactLookup employees, ITenantContext tenant)
    : ICommandHandler<ActivateEmployeeTerminalCredentialCommand>
{
    private const string TerminatedStatus = "Terminated";

    public async Task<Result> Handle(ActivateEmployeeTerminalCredentialCommand command, CancellationToken cancellationToken)
    {
        var found = await terminals.GetCredentialByIdAsync(command.CredentialId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var credential = found.Value;
        if (credential.IsActive)
        {
            return Result.Success();
        }

        var employee = (await employees.GetContactsAsync(tenant.TenantId, [credential.EmployeeId], cancellationToken))
            .FirstOrDefault(e => e.EmployeeId == credential.EmployeeId);
        if (employee is null || employee.Status == TerminatedStatus)
        {
            return Error.Rule(TerminalCredentialErrors.EmployeeNotActive);
        }

        var activeElsewhere = (await terminals.ListCredentialsByValuesAsync([credential.Value], cancellationToken))
            .Any(c => c.Id != credential.Id && c.Kind == credential.Kind && c.IsActive);
        if (activeElsewhere)
        {
            return TerminalCredentialRules.InUseBySomeoneElse(credential.Kind);
        }

        credential.Activate();
        return Result.Success();
    }
}

/// <summary>Kimliği pasife alır (kayıp kart vb.); idempotent. Pasif kimlik okutmada eşlenmez, sonra aynı ya da başka çalışana yeniden atanabilir.</summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record DeactivateEmployeeTerminalCredentialCommand(Guid CredentialId) : ICommand;

public sealed class DeactivateEmployeeTerminalCredentialHandler(ITerminalRepository terminals) : ICommandHandler<DeactivateEmployeeTerminalCredentialCommand>
{
    public async Task<Result> Handle(DeactivateEmployeeTerminalCredentialCommand command, CancellationToken cancellationToken)
    {
        var credential = await terminals.GetCredentialByIdAsync(command.CredentialId, cancellationToken);
        if (credential.IsFailure)
        {
            return credential.Error;
        }

        credential.Value.Deactivate();
        return Result.Success();
    }
}
