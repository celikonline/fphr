using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Domain;

/// <summary>Terminal, terminal grubu ve çalışan terminal kimliği (Z5.1/Z5.4).</summary>
public sealed class TerminalTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Terminal_is_created_active_and_stores_only_key_hash_and_prefix()
    {
        var group = Guid.NewGuid();

        var terminal = Terminal.Create(Tenant, " T-01 ", " Ana giriş ", group, null, TerminalDirectionMode.InOnly, "hash-value", "sk_ab12").Value;

        terminal.Code.ShouldBe("T-01");
        terminal.Name.ShouldBe("Ana giriş");
        terminal.TerminalGroupId.ShouldBe(group);
        terminal.DirectionMode.ShouldBe(TerminalDirectionMode.InOnly);
        terminal.ApiKeyHash.ShouldBe("hash-value");
        terminal.ApiKeyPrefix.ShouldBe("sk_ab12");
        terminal.IsActive.ShouldBeTrue();
        terminal.LastSeenAtUtc.ShouldBeNull();
        Terminal.SensitiveFields.ShouldContain(nameof(Terminal.ApiKeyHash));
    }

    [Fact]
    public void Terminal_rejects_missing_code_name_or_key()
    {
        Terminal.Create(Tenant, "", "Ana", null, null, TerminalDirectionMode.Both, "h", "p").Error.Code.ShouldBe(AttendanceDomainErrors.TerminalCodeRequired);
        Terminal.Create(Tenant, "T", " ", null, null, TerminalDirectionMode.Both, "h", "p").Error.Code.ShouldBe(AttendanceDomainErrors.TerminalNameRequired);
        Terminal.Create(Tenant, "T", "Ana", null, null, TerminalDirectionMode.Both, "", "p").Error.Code.ShouldBe(AttendanceDomainErrors.TerminalApiKeyRequired);
        Terminal.Create(Tenant, "T", "Ana", null, null, TerminalDirectionMode.Both, "h", new string('p', Terminal.ApiKeyPrefixMaxLength + 1)).Error.Code
            .ShouldBe(AttendanceDomainErrors.TerminalApiKeyRequired);
    }

    [Fact]
    public void Terminal_key_rotation_and_heartbeat()
    {
        var terminal = Terminal.Create(Tenant, "T", "Ana", null, null, TerminalDirectionMode.Both, "old", "old_p").Value;
        var seen = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        terminal.RotateApiKey("new", "new_p").IsSuccess.ShouldBeTrue();
        terminal.MarkSeen(seen);
        terminal.Deactivate();

        terminal.ApiKeyHash.ShouldBe("new");
        terminal.ApiKeyPrefix.ShouldBe("new_p");
        terminal.LastSeenAtUtc.ShouldBe(seen);
        terminal.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Group_requires_name()
    {
        TerminalGroup.Create(Tenant, " Merkez ").Value.Name.ShouldBe("Merkez");
        TerminalGroup.Create(Tenant, " ").Error.Code.ShouldBe(AttendanceDomainErrors.TerminalGroupNameRequired);
    }

    [Fact]
    public void Credential_requires_employee_and_value()
    {
        var employee = Guid.NewGuid();

        var credential = EmployeeTerminalCredential.Create(Tenant, employee, TerminalCredentialKind.Card, " 0012345 ").Value;

        credential.Value.ShouldBe("0012345");
        credential.Kind.ShouldBe(TerminalCredentialKind.Card);
        credential.IsActive.ShouldBeTrue();
        EmployeeTerminalCredential.Create(Tenant, employee, TerminalCredentialKind.Pin, " ").Error.Code.ShouldBe(AttendanceDomainErrors.TerminalCredentialValueRequired);
        EmployeeTerminalCredential.Create(Tenant, Guid.Empty, TerminalCredentialKind.Pin, "1234").Error.Code.ShouldBe(AttendanceDomainErrors.PunchEmployeeRequired);
    }
}
