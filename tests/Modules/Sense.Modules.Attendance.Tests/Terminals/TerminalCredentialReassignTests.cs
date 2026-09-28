using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Pasif kimliğin yeniden atanması (tekil indeks pasifleri de kapsar → yeni kayıt değil Reassign).</summary>
public sealed class TerminalCredentialReassignTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Inactive_credential_can_be_reassigned_and_is_reactivated()
    {
        var credential = EmployeeTerminalCredential.Create(Tenant, Guid.NewGuid(), TerminalCredentialKind.Card, new string('a', 64)).Value;
        var next = Guid.NewGuid();
        credential.Deactivate();

        credential.Reassign(next).IsSuccess.ShouldBeTrue();

        credential.EmployeeId.ShouldBe(next);
        credential.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Active_credential_cannot_be_moved_silently()
    {
        var owner = Guid.NewGuid();
        var credential = EmployeeTerminalCredential.Create(Tenant, owner, TerminalCredentialKind.Card, new string('b', 64)).Value;

        credential.Reassign(Guid.NewGuid()).Error.Code.ShouldBe(AttendanceDomainErrors.TerminalCredentialInUse);
        credential.EmployeeId.ShouldBe(owner);

        credential.Deactivate();
        credential.Reassign(Guid.Empty).Error.Code.ShouldBe(AttendanceDomainErrors.PunchEmployeeRequired);
        credential.IsActive.ShouldBeFalse();
    }
}
