using System.Reflection;
using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Zaman &amp; devam sözleşme kayıtları: her bildirilen izin <see cref="AttendancePermissions.All"/> içinde olmalı.</summary>
public sealed class AttendancePermissionsTests
{
    [Fact]
    public void All_contains_every_declared_permission()
    {
        var declared = typeof(AttendancePermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Permission))
            .Select(f => ((Permission)f.GetValue(null)!).Key)
            .ToList();

        var all = AttendancePermissions.All.Select(p => p.Key).ToHashSet();

        declared.ShouldNotBeEmpty();
        declared.Where(key => !all.Contains(key)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("attendance.daily.view")]
    [InlineData("attendance.punch.manage")]
    [InlineData("attendance.shiftdefinition.view")]
    [InlineData("attendance.shiftdefinition.manage")]
    [InlineData("attendance.recalculate.run")]
    [InlineData("attendance.punchrequest.manage")]
    [InlineData("attendance.my.punchrequest")]
    [InlineData("attendance.rotation.view")]
    [InlineData("attendance.rotation.manage")]
    [InlineData("attendance.shifttemplate.manage")]
    [InlineData("attendance.terminal.view")]
    [InlineData("attendance.terminal.manage")]
    [InlineData("attendance.dashboard.view")]
    public void All_contains_new_time_and_attendance_key(string key) =>
        AttendancePermissions.All.Select(p => p.Key).ShouldContain(key);
}
