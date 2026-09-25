using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// "terminal-credentials" içe aktarma gerçek PostgreSQL'de (şema değişmedi): kimlik tablosunda ve içe aktarma kaynaklı denetim satırlarında yalnız
/// özet; aynı dosyanın tekrarı yeni kayıt açmaz (tekil indeks); pasif kimlik aynı satırla başka çalışana geçer; arama kiracıya bağlıdır.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TerminalCredentialImportPersistenceTests : AttendancePostgresFixture
{
    private const string Card = "04A1B2C3D4";
    private const string Pin = "48291573";

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.PostConfigure<TerminalSecurityOptions>(o => o.CredentialPepper = TerminalTestData.TestPepper);
    }

    [Fact]
    public async Task Import_row_hashes_value_and_never_stores_plaintext_in_database_or_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var (ali, veli) = (Guid.NewGuid(), Guid.NewGuid());
        SetCurrentUser(tenant, Guid.NewGuid(), "hr@example.com", new HashSet<string>(["HR"]));
        var employees = Substitute.For<IEmployeeLookup>();
        employees.FindBySicilNumarasiAsync(tenant, "S-1", Arg.Any<CancellationToken>()).Returns(new EmployeeSicilMatch(ali, false));
        employees.FindBySicilNumarasiAsync(tenant, "S-2", Arg.Any<CancellationToken>()).Returns(new EmployeeSicilMatch(veli, false));

        var first = await ImportAsync(employees, ct, Row(2, "S-1", "Kart", Card), Row(3, "S-1", "PIN", Pin));
        var again = await ImportAsync(employees, ct, Row(2, "S-1", "Kart", Card.ToLowerInvariant()), Row(3, "S-1", "PIN", Pin));

        first.Rows.Select(r => r.Outcome).ShouldBe([ImportRowOutcome.Inserted, ImportRowOutcome.Inserted]);
        again.Rows.Select(r => r.Outcome).ShouldBe([ImportRowOutcome.Skipped, ImportRowOutcome.Skipped]);

        // Kart kaybedildi → pasif; aynı kart yeni çalışana içe aktarmayla devredilir (aynı kayıt, yeni kayıt yok).
        await using (var db = AuditedContext())
        {
            (await db.EmployeeTerminalCredentials.SingleAsync(c => c.Kind == TerminalCredentialKind.Card, ct)).Deactivate();
            await db.SaveChangesAsync(ct);
        }

        var handedOver = await ImportAsync(employees, ct, Row(2, "S-2", "Kart", Card));
        handedOver.Rows.Single().Outcome.ShouldBe(ImportRowOutcome.Updated);

        await using (var read = AuditedContext())
        {
            var stored = await read.EmployeeTerminalCredentials.OrderBy(c => c.Kind).ToListAsync(ct);
            stored.Select(c => (c.Kind, c.EmployeeId, c.IsActive)).ShouldBe([(TerminalCredentialKind.Card, veli, true), (TerminalCredentialKind.Pin, ali, true)]);
            stored[0].Id.ShouldBe(first.Rows[0].TargetEntityId!.Value);
        }

        var credentialRows = await RowsAsJsonAsync("employee_terminal_credentials", ct);
        var auditRows = await RowsAsJsonAsync("audit_log_entries", ct);
        var hasher = TerminalTestData.Hasher();
        credentialRows.ShouldContain(hasher.Digest(tenant, TerminalCredentialKind.Card, Card));
        auditRows.ShouldContain("EmployeeTerminalCredential");
        foreach (var raw in new[] { Card, Card.ToLowerInvariant(), Pin })
        {
            credentialRows.ShouldNotContain(raw);
            auditRows.ShouldNotContain(raw);
        }

        auditRows.ShouldNotContain(hasher.Digest(tenant, TerminalCredentialKind.Card, Card));
        auditRows.ShouldNotContain(hasher.Digest(tenant, TerminalCredentialKind.Pin, Pin));
    }

    [Fact]
    public async Task Credential_lookup_for_import_is_tenant_scoped_and_includes_inactive()
    {
        var ct = TestContext.Current.CancellationToken;
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var digestA = TerminalTestData.Hasher().Digest(tenantA, TerminalCredentialKind.Pin, Pin);
        var digestB = TerminalTestData.Hasher().Digest(tenantB, TerminalCredentialKind.Pin, Pin);
        await SaveAsync(tenantA, digestA, active: false, ct);
        await SaveAsync(tenantB, digestB, active: true, ct);

        using var tenantScope = TenantContextSetter!.BeginScope(tenantA);
        using var scope = ServiceProvider!.CreateScope();
        var found = await new TerminalRepository(scope.ServiceProvider.GetRequiredService<AttendanceDbContext>()).ListCredentialsByValuesAsync([digestA, digestB], ct);

        found.Select(c => (c.TenantId, c.Value, c.IsActive)).ShouldBe([(tenantA, digestA, false)]);
    }

    private async Task<ImportBatchResult> ImportAsync(IEmployeeLookup employees, CancellationToken ct, params ImportRow[] rows)
    {
        using var importScope = AuditSource!.BeginScope(AuditSources.Import);
        await using var db = AuditedContext();
        var handler = new TerminalCredentialImportRowHandler(TenantContextAccessor!, employees, new TerminalRepository(db), TerminalTestData.Hasher(), db);
        return await handler.ProcessAsync(
            new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows,
                [TerminalCredentialImportColumns.SicilNumarasi, TerminalCredentialImportColumns.Kind, TerminalCredentialImportColumns.Value], rows),
            ct);
    }

    private async Task SaveAsync(Guid tenant, string digest, bool active, CancellationToken ct)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var credential = EmployeeTerminalCredential.Create(tenant, Guid.NewGuid(), TerminalCredentialKind.Pin, digest).Value;
        if (!active)
        {
            credential.Deactivate();
        }

        db.EmployeeTerminalCredentials.Add(credential);
        await db.SaveChangesAsync(ct);
    }

    private static ImportRow Row(int number, string sicil, string kind, string value) => new(number, new Dictionary<string, ImportCell>
    {
        [TerminalCredentialImportColumns.SicilNumarasi] = new(sicil),
        [TerminalCredentialImportColumns.Kind] = new(kind),
        [TerminalCredentialImportColumns.Value] = new(value),
    });

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
}
