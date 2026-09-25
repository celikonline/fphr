using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure.Terminals;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// Terminal güvenliği gerçek PostgreSQL'de (W0-a şeması, migration yok): veritabanında ve denetim kaydında yalnız özetler; kiracılar arası önek
/// araması doğru terminali bulur; pasif/yenilenmiş anahtar reddedilir; nabız son görülmeyi kiracı bağlamı ve denetim satırı olmadan günceller;
/// kimlik araması kiracıya bağlıdır; anahtardan okutmaya uçtan uca kiracı yalnız terminalden gelir.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TerminalPersistenceTests : AttendancePostgresFixture
{
    private readonly FakePunchRecorder _recorder = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.PostConfigure<TerminalSecurityOptions>(o => o.CredentialPepper = TerminalTestData.TestPepper);
        services.AddSingleton<IPunchRecorder>(_recorder);
    }

    [Fact]
    public async Task Key_and_credential_are_stored_only_as_digests_and_audit_never_carries_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        SetCurrentUser(tenant, Guid.NewGuid(), "admin@example.com", new HashSet<string>(["HR"]));
        var hasher = TerminalTestData.Hasher();
        TerminalApiKeyDto created;
        TerminalApiKeyDto rotated;
        string digest;

        await using (var db = AuditedContext())
        {
            var repository = new TerminalRepository(db);
            created = (await new CreateTerminalHandler(repository, Substitute.For<ILocationLookup>(), TenantContextAccessor!).Handle(
                new CreateTerminalCommand(new TerminalInput("T-01", "Ana giriş", null, null, TerminalDirectionMode.Both)), ct)).Value;
            await db.SaveChangesAsync(ct);

            rotated = (await new RotateTerminalApiKeyHandler(repository).Handle(new RotateTerminalApiKeyCommand(created.TerminalId), ct)).Value;
            digest = hasher.Hash(tenant, TerminalCredentialKind.Pin, "4711");
            var credential = EmployeeTerminalCredential.Create(tenant, Guid.NewGuid(), TerminalCredentialKind.Pin, digest).Value;
            repository.AddCredential(credential);
            await db.SaveChangesAsync(ct);

            credential.Deactivate();
            credential.Reassign(Guid.NewGuid()).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        var terminalRow = await RowsAsJsonAsync("terminals", ct);
        var credentialRow = await RowsAsJsonAsync("employee_terminal_credentials", ct);
        var auditRows = await RowsAsJsonAsync("audit_log_entries", ct);

        terminalRow.ShouldContain(TerminalApiKey.Hash(rotated.ApiKey));
        terminalRow.ShouldNotContain(TerminalApiKey.Hash(created.ApiKey));
        terminalRow.ShouldNotContain(created.ApiKey);
        terminalRow.ShouldNotContain(rotated.ApiKey);
        credentialRow.ShouldContain(digest);
        credentialRow.ShouldNotContain("4711");
        auditRows.ShouldContain("Terminal");
        auditRows.ShouldContain("EmployeeTerminalCredential");
        foreach (var secret in new[] { created.ApiKey, rotated.ApiKey, TerminalApiKey.Hash(created.ApiKey), TerminalApiKey.Hash(rotated.ApiKey), created.ApiKeyPrefix, rotated.ApiKeyPrefix, digest, "4711" })
        {
            auditRows.ShouldNotContain(secret);
        }
    }

    [Fact]
    public async Task Authenticator_matches_the_right_tenant_across_prefix_collisions_and_rejects_inactive_or_rotated_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var keyA = TerminalTestData.KeyWithPrefix("Collide1");
        var keyB = TerminalTestData.KeyWithPrefix("Collide1");
        var terminalA = await SaveTerminalAsync(tenantA, keyA, ct);
        var terminalB = await SaveTerminalAsync(tenantB, keyB, ct);

        (await AuthenticateAsync(keyB, ct))!.ShouldBe(new TerminalIdentity(terminalB.Id, tenantB, TerminalDirectionMode.Both, null));
        (await AuthenticateAsync(keyA, ct))!.TenantId.ShouldBe(tenantA);

        using (TenantContextSetter!.BeginScope(tenantB))
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            (await db.Terminals.SingleAsync(t => t.Id == terminalB.Id, ct)).Deactivate();
            await db.SaveChangesAsync(ct);
        }

        using (TenantContextSetter.BeginScope(tenantA))
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await new RotateTerminalApiKeyHandler(new TerminalRepository(db)).Handle(new RotateTerminalApiKeyCommand(terminalA.Id), ct);
            await db.SaveChangesAsync(ct);
        }

        (await AuthenticateAsync(keyB, ct)).ShouldBeNull();
        (await AuthenticateAsync(keyA, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Heartbeat_updates_LastSeen_without_tenant_context_or_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var terminal = await SaveTerminalAsync(tenant, TerminalApiKey.Generate().Key, ct);
        var seen = new DateTime(2026, 9, 25, 8, 30, 15, DateTimeKind.Utc);
        var auditBefore = await RowsAsJsonAsync("audit_log_entries", ct);

        using (var scope = ServiceProvider!.CreateScope())
        {
            TenantContextAccessor!.IsResolved.ShouldBeFalse();
            await new TerminalRepository(scope.ServiceProvider.GetRequiredService<AttendanceDbContext>()).MarkTerminalSeenAsync(tenant, terminal.Id, seen, ct);
            await new TerminalRepository(scope.ServiceProvider.GetRequiredService<AttendanceDbContext>()).MarkTerminalSeenAsync(Guid.NewGuid(), terminal.Id, seen.AddHours(1), ct);
        }

        using var read = ServiceProvider!.CreateScope();
        var stored = await read.ServiceProvider.GetRequiredService<AttendanceDbContext>().Terminals.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .SingleAsync(t => t.Id == terminal.Id, ct);
        stored.LastSeenAtUtc.ShouldBe(seen);
        (await RowsAsJsonAsync("audit_log_entries", ct)).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task Credential_lookup_is_tenant_scoped_and_ignores_inactive()
    {
        var ct = TestContext.Current.CancellationToken;
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var hasher = TerminalTestData.Hasher();
        var digestA = hasher.Hash(tenantA, TerminalCredentialKind.Card, "04A1B2C3");
        var digestB = hasher.Hash(tenantB, TerminalCredentialKind.Card, "04A1B2C3");
        var inactiveDigest = hasher.Hash(tenantA, TerminalCredentialKind.Card, "OLD-CARD");
        var employeeA = Guid.NewGuid();
        await SaveCredentialAsync(tenantA, employeeA, digestA, active: true, ct);
        await SaveCredentialAsync(tenantB, Guid.NewGuid(), digestB, active: true, ct);
        await SaveCredentialAsync(tenantA, Guid.NewGuid(), inactiveDigest, active: false, ct);

        using var scope = ServiceProvider!.CreateScope();
        var found = await new TerminalRepository(scope.ServiceProvider.GetRequiredService<AttendanceDbContext>())
            .ListActiveCredentialsByValuesAsync(tenantA, [digestA, digestB, inactiveDigest], ct);

        digestA.ShouldNotBe(digestB);
        found.Select(c => (c.TenantId, c.EmployeeId)).ShouldBe([(tenantA, employeeA)]);
    }

    [Fact]
    public async Task End_to_end_key_to_punch_uses_only_the_terminal_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var key = TerminalApiKey.Generate().Key;
        var terminal = await SaveTerminalAsync(tenant, key, ct, TerminalDirectionMode.InOnly);
        await SaveCredentialAsync(tenant, employee, TerminalTestData.Hasher().Hash(tenant, TerminalCredentialKind.Card, "04A1B2C3"), active: true, ct);

        var identity = (await AuthenticateAsync(key, ct))!;
        using var tenantScope = TenantContextSetter!.BeginScope(identity.TenantId);
        using var scope = ServiceProvider!.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<TerminalPunchIngest>().IngestAsync(identity,
            [
                new TerminalPunchItem(new TerminalCredentialInput(TerminalCredentialKind.Card, "04a1b2c3"), new DateTime(2026, 9, 25, 7, 58, 0, DateTimeKind.Utc), null),
                new TerminalPunchItem(new TerminalCredentialInput(TerminalCredentialKind.Card, "FFFF0000"), new DateTime(2026, 9, 25, 7, 59, 0, DateTimeKind.Utc), null),
            ], ct);

        (result.Value.Accepted, result.Value.Rejected).ShouldBe((1, 1));
        _recorder.Received.Single().ShouldBe(new PunchInput(
            tenant, employee, new DateTime(2026, 9, 25, 7, 58, 0, DateTimeKind.Utc), PunchDirectionDto.In, PunchSourceDto.Terminal, null, terminal.Id, null, null, null,
            false, null, null));
    }

    private AttendanceDbContext AuditedContext()
    {
        var connection = ServiceProvider!.GetRequiredService<IConfiguration>().GetConnectionString("Database");
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new AuditTenantInterceptor(TenantContextAccessor!, CurrentUser!, TimeProvider.System),
                new AuditLogInterceptor(CurrentUser!, AuditSource!, TimeProvider.System, NullLogger<AuditLogInterceptor>.Instance))
            .Options;
        return new AttendanceDbContext(options, TenantContextAccessor!);
    }

    private async Task<string> RowsAsJsonAsync(string table, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ServiceProvider!.GetRequiredService<IConfiguration>().GetConnectionString("Database"));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"SELECT coalesce(string_agg(row_to_json(t)::text, '|' ORDER BY t.id), '') FROM attendance.{table} t", connection);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task<Terminal> SaveTerminalAsync(Guid tenant, string key, CancellationToken ct, TerminalDirectionMode mode = TerminalDirectionMode.Both)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var terminal = TerminalTestData.NewTerminal(tenant, key, mode);
        db.Terminals.Add(terminal);
        await db.SaveChangesAsync(ct);
        return terminal;
    }

    private async Task SaveCredentialAsync(Guid tenant, Guid employee, string digest, bool active, CancellationToken ct)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var credential = EmployeeTerminalCredential.Create(tenant, employee, TerminalCredentialKind.Card, digest).Value;
        if (!active)
        {
            credential.Deactivate();
        }

        db.EmployeeTerminalCredentials.Add(credential);
        await db.SaveChangesAsync(ct);
    }

    private async Task<TerminalIdentity?> AuthenticateAsync(string key, CancellationToken ct)
    {
        TenantContextAccessor!.IsResolved.ShouldBeFalse("terminal kimlik doğrulaması kiracı bağlamı olmadan çalışır");
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITerminalKeyAuthenticator>().AuthenticateAsync(key, ct);
    }
}
