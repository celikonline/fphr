using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Api.Controllers;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// Kimlik yönetim uçları (Z5.4): izinler, yanıtlarda değer/özet yok, ekleme kuralları (biçim, işten ayrılan, başka çalışanda aktif, pepper yok → 503),
/// pasifleştirme ve EmployeeTerminated → kimlikleri pasifleştiren işleyici (Worker'da çözülür).
/// </summary>
public sealed class TerminalCredentialManagementTests
{
    private const string Pin = "471193";

    /// <summary>API JSON biçimi (camelCase, enum'lar camelCase metin).</summary>
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();
    private readonly List<EmployeeTerminalCredential> _added = [];
    private readonly List<EmployeeTerminalCredential> _stored = [];

    public TerminalCredentialManagementTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _terminals.When(t => t.AddCredential(Arg.Any<EmployeeTerminalCredential>())).Do(call => _added.Add(call.Arg<EmployeeTerminalCredential>()));
        _terminals.ListCredentialsByValuesAsync(default!, default).ReturnsForAnyArgs(call =>
            _stored.Where(c => call.Arg<IReadOnlyCollection<string>>().Contains(c.Value)).ToList());
    }

    [Fact]
    public async Task Management_endpoints_require_permission_and_never_return_value()
    {
        typeof(ListEmployeeTerminalCredentialsQuery).GetCustomAttribute<RequiresPermissionAttribute>()!.Permission.ShouldBe(AttendancePermissions.TerminalView.Key);
        typeof(AddEmployeeTerminalCredentialCommand).GetCustomAttribute<RequiresPermissionAttribute>()!.Permission.ShouldBe(AttendancePermissions.TerminalManage.Key);
        typeof(DeactivateEmployeeTerminalCredentialCommand).GetCustomAttribute<RequiresPermissionAttribute>()!.Permission.ShouldBe(AttendancePermissions.TerminalManage.Key);
        typeof(TerminalCredentialsController).GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/v{version:apiVersion}/attendance/terminal-credentials");

        // İzinsiz kullanıcı: gerçek AuthorizationBehaviour → 403, işleyici çalışmaz.
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(default, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(false);
        var behaviour = new AuthorizationBehaviour<AddEmployeeTerminalCredentialCommand, Result<Guid>>(user, permissions, Substitute.For<IDataScopeService>());
        var handlerRan = false;
        var denied = await behaviour.Handle(
            new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, Pin),
            () =>
            {
                handlerRan = true;
                return Task.FromResult<Result<Guid>>(Guid.NewGuid());
            },
            TestContext.Current.CancellationToken);
        denied.Error.Type.ShouldBe(ErrorType.Forbidden);
        handlerRan.ShouldBeFalse();

        // Yanıt biçimi: değer/özet alanı yok; listelenen JSON'da ne ham değer ne özet var.
        typeof(TerminalCredentialDto).GetProperties().Select(p => p.Name).ShouldBe(["Id", "EmployeeId", "Kind", "IsActive", "CreatedAt"]);
        var stored = Stored(_employeeId, TerminalCredentialKind.Pin, Pin, active: true);
        _terminals.ListCredentialsForEmployeeAsync(_employeeId, Arg.Any<CancellationToken>()).Returns([stored]);
        var list = (await new ListEmployeeTerminalCredentialsHandler(_terminals).Handle(
            new ListEmployeeTerminalCredentialsQuery(_employeeId), TestContext.Current.CancellationToken)).Value;
        var json = JsonSerializer.Serialize(list, ApiJson);
        json.ShouldContain("\"kind\":\"pin\"");
        json.ShouldNotContain(stored.Value);
        json.ShouldNotContain(Pin);

        // İstek nesneleri loga/izlemeye düşse bile değer yazılmaz.
        new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, Pin).ToString().ShouldNotContain(Pin);
        new AddTerminalCredentialRequest(_employeeId, TerminalCredentialKind.Pin, Pin).ToString().ShouldNotContain(Pin);
    }

    [Fact]
    public async Task Add_hashes_value_and_is_idempotent_for_the_same_employee()
    {
        var handler = AddHandler();

        var created = await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, " 471193 "), TestContext.Current.CancellationToken);

        var credential = _added.ShouldHaveSingleItem();
        created.Value.ShouldBe(credential.Id);
        credential.Value.ShouldBe(TerminalTestData.Hasher().Digest(_tenantId, TerminalCredentialKind.Pin, Pin));
        _stored.Add(credential);
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, Pin), TestContext.Current.CancellationToken))
            .Value.ShouldBe(credential.Id);
        _added.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Add_rejects_bad_format_unknown_or_terminated_employee_and_active_credential_of_another()
    {
        var terminated = Guid.NewGuid();
        var other = Guid.NewGuid();
        _stored.Add(Stored(other, TerminalCredentialKind.Pin, Pin, active: true));
        var handler = AddHandler(new Dictionary<Guid, string> { [terminated] = "Terminated", [other] = "Active" });
        var ct = TestContext.Current.CancellationToken;

        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, "12a"), ct)).Error
            .ShouldBe(Error.Validation(TerminalCredentialErrors.PinInvalid));
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Card, new string('1', 65)), ct)).Error.Code
            .ShouldBe(TerminalCredentialErrors.ValueTooLong);
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Card, "  "), ct)).Error.Code
            .ShouldBe(AttendanceDomainErrors.TerminalCredentialValueRequired);
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(Guid.NewGuid(), TerminalCredentialKind.Pin, "555512"), ct)).Error
            .ShouldBe(Error.NotFound(TerminalCredentialErrors.EmployeeNotFound));
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(terminated, TerminalCredentialKind.Pin, "555512"), ct)).Error
            .ShouldBe(Error.Rule(TerminalCredentialErrors.EmployeeNotActive));
        // Başka çalışanda aktif: kartta 409 in_use, PIN'de genel ret (varlığı açıkça söylenmez); hiçbirinde sahip bilgisi yok.
        _stored.Add(Stored(other, TerminalCredentialKind.Card, "C-77", active: true));
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Card, "c-77"), ct)).Error
            .ShouldBe(Error.Conflict(AttendanceDomainErrors.TerminalCredentialInUse));
        (await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, Pin), ct)).Error
            .ShouldBe(Error.Rule(TerminalCredentialErrors.Rejected));
        _added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_without_pepper_returns_503()
    {
        var handler = new AddEmployeeTerminalCredentialHandler(_terminals, TerminalTestData.Hasher(pepper: null), TerminalTestData.Contacts(), _tenant);

        var result = await handler.Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, Pin), TestContext.Current.CancellationToken);

        result.Error.Type.ShouldBe(ErrorType.ServiceUnavailable);
        result.Error.Code.ShouldBe(TerminalErrors.CredentialPepperMissing);
        _added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deactivate_credential_is_idempotent()
    {
        var credential = Stored(_employeeId, TerminalCredentialKind.Card, "C-1", active: true);
        _terminals.GetCredentialByIdAsync(credential.Id, Arg.Any<CancellationToken>()).Returns(credential);
        var handler = new DeactivateEmployeeTerminalCredentialHandler(_terminals);

        (await handler.Handle(new DeactivateEmployeeTerminalCredentialCommand(credential.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        (await handler.Handle(new DeactivateEmployeeTerminalCredentialCommand(credential.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        credential.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Activate_reactivates_inactive_credential_of_the_same_employee_without_changing_digest()
    {
        var credential = Stored(_employeeId, TerminalCredentialKind.Pin, Pin, active: false);
        var digest = credential.Value;
        _stored.Add(credential);
        _terminals.GetCredentialByIdAsync(credential.Id, Arg.Any<CancellationToken>()).Returns(credential);
        var handler = ActivateHandler();
        var ct = TestContext.Current.CancellationToken;

        (await handler.Handle(new ActivateEmployeeTerminalCredentialCommand(credential.Id), ct)).IsSuccess.ShouldBeTrue();
        (credential.IsActive, credential.EmployeeId, credential.Value).ShouldBe((true, _employeeId, digest));
        (await handler.Handle(new ActivateEmployeeTerminalCredentialCommand(credential.Id), ct)).IsSuccess.ShouldBeTrue();
        typeof(ActivateEmployeeTerminalCredentialCommand).GetCustomAttribute<RequiresPermissionAttribute>()!.Permission.ShouldBe(AttendancePermissions.TerminalManage.Key);
        typeof(TerminalCredentialsController).GetMethod(nameof(TerminalCredentialsController.Activate))!.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe("{id:guid}/activate");
    }

    [Fact]
    public async Task Activate_rejects_terminated_employee_and_same_digest_active_elsewhere()
    {
        var terminated = Guid.NewGuid();
        var ofTerminated = Stored(terminated, TerminalCredentialKind.Card, "C-1", active: false);
        _terminals.GetCredentialByIdAsync(ofTerminated.Id, Arg.Any<CancellationToken>()).Returns(ofTerminated);
        // Tekil indeks aynı özetin ikinci kaydını engeller; yine de başka çalışanda aktif kopya görülürse ekleme kuralı uygulanır.
        var pin = Stored(_employeeId, TerminalCredentialKind.Pin, Pin, active: false);
        var card = Stored(_employeeId, TerminalCredentialKind.Card, "C-2", active: false);
        _stored.AddRange([pin, card, Stored(Guid.NewGuid(), TerminalCredentialKind.Pin, Pin, active: true), Stored(Guid.NewGuid(), TerminalCredentialKind.Card, "C-2", active: true)]);
        _terminals.GetCredentialByIdAsync(pin.Id, Arg.Any<CancellationToken>()).Returns(pin);
        _terminals.GetCredentialByIdAsync(card.Id, Arg.Any<CancellationToken>()).Returns(card);
        var handler = ActivateHandler(new Dictionary<Guid, string> { [terminated] = "Terminated" });
        var ct = TestContext.Current.CancellationToken;

        (await handler.Handle(new ActivateEmployeeTerminalCredentialCommand(ofTerminated.Id), ct)).Error.ShouldBe(Error.Rule(TerminalCredentialErrors.EmployeeNotActive));
        (await handler.Handle(new ActivateEmployeeTerminalCredentialCommand(pin.Id), ct)).Error.ShouldBe(Error.Rule(TerminalCredentialErrors.Rejected));
        (await handler.Handle(new ActivateEmployeeTerminalCredentialCommand(card.Id), ct)).Error.ShouldBe(Error.Conflict(AttendanceDomainErrors.TerminalCredentialInUse));
        new[] { ofTerminated, pin, card }.ShouldAllBe(c => !c.IsActive);
    }

    [Fact]
    public async Task Deactivate_on_employee_terminated_event()
    {
        var card = Stored(_employeeId, TerminalCredentialKind.Card, "C-1", active: true);
        var pin = Stored(_employeeId, TerminalCredentialKind.Pin, Pin, active: true);
        var alreadyOff = Stored(_employeeId, TerminalCredentialKind.ExternalId, "X-1", active: false);
        _terminals.ListCredentialsForEmployeeAsync(_employeeId, Arg.Any<CancellationToken>()).Returns([card, pin, alreadyOff]);
        var unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
        var handler = new EmployeeTerminatedTerminalCredentialsHandler(_terminals, unitOfWork);
        var terminatedEvent = new EmployeeTerminated(_tenantId, _employeeId, TerminationType.Resignation, "RES", null, new DateOnly(2026, 9, 30));

        await handler.Handle(terminatedEvent, TestContext.Current.CancellationToken);

        new[] { card, pin, alreadyOff }.ShouldAllBe(c => !c.IsActive);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Tekrar teslim: aktif kimlik kalmadı → kayıt yok.
        unitOfWork.ClearReceivedCalls();
        await handler.Handle(terminatedEvent, TestContext.Current.CancellationToken);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Worker_resolves_terminated_handler()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=localhost;Database=x;Username=x;Password=x",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);

        // Sense.Worker'ın Attendance kaydıyla aynı: Application derlemesi taranmaz, işleyiciler açıkça eklenir.
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendancePermissions).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmployeeTerminated>>()
            .ShouldHaveSingleItem().ShouldBeOfType<EmployeeTerminatedTerminalCredentialsHandler>();
    }

    private ActivateEmployeeTerminalCredentialHandler ActivateHandler(IReadOnlyDictionary<Guid, string>? statuses = null) =>
        new(_terminals, Contacts(statuses), _tenant);

    private IEmployeeContactLookup Contacts(IReadOnlyDictionary<Guid, string>? statuses)
    {
        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(default, default!, default).ReturnsForAnyArgs(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
            .Select(id => new EmployeeContact(id, "Ad", "Soyad", null, null, null, statuses?.GetValueOrDefault(id) ?? "Active"))
            .ToList());
        return contacts;
    }

    private AddEmployeeTerminalCredentialHandler AddHandler(IReadOnlyDictionary<Guid, string>? statuses = null)
    {
        var known = new HashSet<Guid>([_employeeId, .. statuses?.Keys ?? []]);
        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(default, default!, default).ReturnsForAnyArgs(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
            .Where(known.Contains)
            .Select(id => new EmployeeContact(id, "Ad", "Soyad", null, null, null, statuses?.GetValueOrDefault(id) ?? "Active"))
            .ToList());
        return new AddEmployeeTerminalCredentialHandler(_terminals, TerminalTestData.Hasher(), contacts, _tenant);
    }

    private EmployeeTerminalCredential Stored(Guid employeeId, TerminalCredentialKind kind, string raw, bool active)
    {
        var credential = EmployeeTerminalCredential.Create(_tenantId, employeeId, kind, TerminalTestData.Hasher().Digest(_tenantId, kind, raw)).Value;
        if (!active)
        {
            credential.Deactivate();
        }

        return credential;
    }
}
