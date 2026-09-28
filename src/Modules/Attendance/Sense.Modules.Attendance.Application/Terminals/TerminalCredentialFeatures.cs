using Sense.Modules.Attendance.Contracts;
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

/// <summary>
/// Bir çalışanın terminal kimlikleri (Z5.4), türe göre. Çağıranın <c>attendance.terminal.view</c> veri kapsamı dışındaki çalışan
/// <see cref="TerminalCredentialErrors.EmployeeNotFound"/> (404) döner - varlık sızmaz. <see cref="EmployeeId"/> verilmezse boş liste.
/// </summary>
[RequiresPermission("attendance.terminal.view")]
public sealed record ListEmployeeTerminalCredentialsQuery(Guid? EmployeeId) : IQuery<IReadOnlyList<TerminalCredentialDto>>;

public sealed class ListEmployeeTerminalCredentialsHandler(ITerminalRepository terminals, IDataScopeService dataScope)
    : IQueryHandler<ListEmployeeTerminalCredentialsQuery, IReadOnlyList<TerminalCredentialDto>>
{
    public async Task<Result<IReadOnlyList<TerminalCredentialDto>>> Handle(ListEmployeeTerminalCredentialsQuery query, CancellationToken cancellationToken)
    {
        if (query.EmployeeId is not { } employeeId || employeeId == Guid.Empty)
        {
            return Array.Empty<TerminalCredentialDto>();
        }

        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TerminalView.Key, employeeId, cancellationToken))
        {
            return Error.NotFound(TerminalCredentialErrors.EmployeeNotFound);
        }

        return (await terminals.ListCredentialsForEmployeeAsync(employeeId, cancellationToken))
            .Select(c => new TerminalCredentialDto(c.Id, c.EmployeeId, c.Kind, c.IsActive, c.CreatedAt))
            .ToList();
    }
}

/// <summary>
/// Çalışana kimlik tanımlar (Z5.4): değer biçim denetiminden sonra HMAC ile özetlenir, yalnız özet saklanır. Pepper yoksa 503. Kapsam dışı
/// çalışan 404 <see cref="TerminalCredentialErrors.EmployeeNotFound"/>; işten ayrılmış (ya da kaydı olmayan) çalışan 422
/// <see cref="TerminalCredentialErrors.EmployeeNotActive"/>. Aynı kimlik zaten bu çalışandaysa mevcut kaydın kimliği döner (pasifse yeniden
/// etkinleşir); başka çalışanda aktifse kart/harici 409 in_use, PIN genel rejected (sahip bilgisi yok), pasifse bu çalışana devredilir.
/// Eşzamanlı aynı özet eklemesi (tekil indeks ihlali) aynı çakışma hatasına çevrilir (<see cref="ITerminalCredentialSaver"/>).
/// <see cref="ToString"/> değeri yazmaz.
/// </summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record AddEmployeeTerminalCredentialCommand(Guid EmployeeId, TerminalCredentialKind Kind, string? Value) : ICommand<Guid>
{
    public override string ToString() =>
        $"{nameof(AddEmployeeTerminalCredentialCommand)} {{ {nameof(EmployeeId)} = {EmployeeId}, {nameof(Kind)} = {Kind} }}";
}

public sealed class AddEmployeeTerminalCredentialHandler(
    ITerminalRepository terminals, ITerminalCredentialHasher hasher, IEmployeeLookup employees, IDataScopeService dataScope,
    ITerminalCredentialSaver saver, ITenantContext tenant)
    : ICommandHandler<AddEmployeeTerminalCredentialCommand, Guid>
{
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

        var employee = await TerminalCredentialAccess.CheckEmployeeAsync(
            dataScope, employees, tenant.TenantId, command.EmployeeId, cancellationToken);
        if (employee.IsFailure)
        {
            return employee.Error;
        }

        var digest = hasher.Hash(tenant.TenantId, command.Kind, value);
        if (digest.IsFailure)
        {
            return digest.Error;
        }

        var existing = (await terminals.ListCredentialsByValuesAsync([digest.Value], cancellationToken)).FirstOrDefault(c => c.Kind == command.Kind);
        var assigned = TerminalCredentialRules.Assign(terminals, tenant.TenantId, command.EmployeeId, command.Kind, digest.Value, existing);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        var saved = await saver.SaveAsync(command.Kind, cancellationToken);
        return saved.IsFailure ? saved.Error : assigned.Value.Credential.Id;
    }
}

/// <summary>
/// Pasif kimliği AYNI çalışanda yeniden etkinleştirir (F15 "aktif et"); özet değişmez, idempotent. Kapsam dışı çalışanın kimliği yokmuş
/// gibi 404 (<see cref="AttendanceDomainErrors.TerminalCredentialNotFound"/>). İşten ayrılmış çalışanın kimliği etkinleştirilemez
/// (<see cref="TerminalCredentialErrors.EmployeeNotActive"/>). Aynı (tür, özet) başka çalışanda aktifse ekleme kuralı: kart/harici
/// <see cref="AttendanceDomainErrors.TerminalCredentialInUse"/>, PIN genel <see cref="TerminalCredentialErrors.Rejected"/> (tekil indeks
/// bunu normalde engeller; savunma amaçlı). Başka çalışana devir bu uçla değil, aynı değerle ekleme/içe aktarmayla yapılır.
/// </summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record ActivateEmployeeTerminalCredentialCommand(Guid CredentialId) : ICommand;

public sealed class ActivateEmployeeTerminalCredentialHandler(
    ITerminalRepository terminals, IEmployeeLookup employees, IDataScopeService dataScope, ITerminalCredentialSaver saver, ITenantContext tenant)
    : ICommandHandler<ActivateEmployeeTerminalCredentialCommand>
{
    public async Task<Result> Handle(ActivateEmployeeTerminalCredentialCommand command, CancellationToken cancellationToken)
    {
        var found = await TerminalCredentialAccess.FindInScopeAsync(terminals, dataScope, command.CredentialId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var credential = found.Value;
        if (credential.IsActive)
        {
            return Result.Success();
        }

        if (!await employees.ExistsActiveAsync(tenant.TenantId, credential.EmployeeId, cancellationToken))
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
        return await saver.SaveAsync(credential.Kind, cancellationToken);
    }
}

/// <summary>
/// Kimliği pasife alır (kayıp kart vb.); idempotent. Kapsam dışı çalışanın kimliği yokmuş gibi 404. Pasif kimlik okutmada eşlenmez, sonra aynı
/// ya da başka çalışana yeniden atanabilir.
/// </summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record DeactivateEmployeeTerminalCredentialCommand(Guid CredentialId) : ICommand;

public sealed class DeactivateEmployeeTerminalCredentialHandler(ITerminalRepository terminals, IDataScopeService dataScope)
    : ICommandHandler<DeactivateEmployeeTerminalCredentialCommand>
{
    public async Task<Result> Handle(DeactivateEmployeeTerminalCredentialCommand command, CancellationToken cancellationToken)
    {
        var found = await TerminalCredentialAccess.FindInScopeAsync(terminals, dataScope, command.CredentialId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.Deactivate();
        return Result.Success();
    }
}

/// <summary>Kimlik uçlarının ortak veri kapsamı denetimleri (attendance.terminal.manage kapsamı; kapsam dışı = yok, varlık sızmaz).</summary>
internal static class TerminalCredentialAccess
{
    public static async Task<Result<EmployeeTerminalCredential>> FindInScopeAsync(
        ITerminalRepository terminals, IDataScopeService dataScope, Guid credentialId, CancellationToken cancellationToken)
    {
        var credential = await terminals.GetCredentialByIdAsync(credentialId, cancellationToken);
        if (credential.IsFailure)
        {
            return credential.Error;
        }

        return await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TerminalManage.Key, credential.Value.EmployeeId, cancellationToken)
            ? credential.Value
            : Error.NotFound(AttendanceDomainErrors.TerminalCredentialNotFound);
    }

    /// <summary>Kapsam dışı → 404 employee_not_found; kayıt yok ya da işten ayrılmış → 422 employee_not_active (yalnız durum okunur).</summary>
    public static async Task<Result> CheckEmployeeAsync(
        IDataScopeService dataScope, IEmployeeLookup employees, Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TerminalManage.Key, employeeId, cancellationToken))
        {
            return Error.NotFound(TerminalCredentialErrors.EmployeeNotFound);
        }

        return await employees.ExistsActiveAsync(tenantId, employeeId, cancellationToken)
            ? Result.Success()
            : Error.Rule(TerminalCredentialErrors.EmployeeNotActive);
    }
}
