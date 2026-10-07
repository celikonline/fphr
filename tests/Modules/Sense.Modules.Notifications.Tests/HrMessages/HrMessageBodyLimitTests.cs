using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Application.HrMessages;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.HrMessages;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests.HrMessages;

/// <summary>Pure unit tests (no database): the visible-text limit of an HR message body versus the HTML storage ceiling.</summary>
public sealed class HrMessageBodyLimitTests
{
    private static SendHrMessageCommand Send(EmployeeSelection? selection, string body) =>
        new("Konu", body, HrMessageImportance.Normal, [NotificationChannels.InApp], selection, null);

    [Fact]
    public void Visible_text_over_4096_rejected_at_4096_accepted()
    {
        var validator = new SendHrMessageValidator();
        var selection = new EmployeeSelection([Guid.NewGuid()], null, null);

        validator.Validate(Send(selection, body: new string('a', 4097))).IsValid.ShouldBeFalse();
        validator.Validate(Send(selection, body: new string('a', 4096))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Visible_text_limit_ignores_markup()
    {
        var validator = new SendHrMessageValidator();
        var selection = new EmployeeSelection([Guid.NewGuid()], null, null);

        validator.Validate(Send(selection, body: "<p>" + new string('a', 4096) + "</p>")).IsValid.ShouldBeTrue();
        validator.Validate(Send(selection, body: "<p><strong>" + new string('a', 4096) + "</strong></p><p>b</p>")).IsValid.ShouldBeFalse();
        validator.Validate(Send(selection, body: "<p><a href=\"https://example.com/" + new string('x', 5000) + "\">" + new string('a', 4096) + "</a></p>")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Storage_ceiling_still_applies_to_the_raw_html()
    {
        var validator = new SendHrMessageValidator();
        var selection = new EmployeeSelection([Guid.NewGuid()], null, null);

        validator.Validate(Send(selection, body: "<p>a</p>" + new string(' ', NotificationsLimits.HrMessageBodyMaxLength))).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Domain_ctor_only_enforces_the_storage_ceiling()
    {
        var atCeiling = new HrMessage(
            Guid.NewGuid(), Guid.NewGuid(), "s", new string('a', NotificationsLimits.HrMessageBodyMaxLength), HrMessageImportance.Normal, [NotificationChannels.InApp]);
        atCeiling.Body.Length.ShouldBe(NotificationsLimits.HrMessageBodyMaxLength);

        Should.Throw<ArgumentException>(() => new HrMessage(
            Guid.NewGuid(), Guid.NewGuid(), "s", new string('a', NotificationsLimits.HrMessageBodyMaxLength + 1), HrMessageImportance.Normal, [NotificationChannels.InApp]));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 3)]
    [InlineData("a b  c", 6)]
    [InlineData("<p>abc</p>", 3)]
    [InlineData("<p><strong>ab</strong><em>c</em></p>", 3)]
    [InlineData("<ul><li>a</li><li><b><i>bc</i></b></li></ul>", 3)]
    [InlineData("a&amp;b", 3)]
    [InlineData("&lt;b&gt;", 3)]
    [InlineData("a&nbsp;b", 3)]
    [InlineData("<p>a<br>b</p>", 2)]
    [InlineData("<img src=\"https://example.com/a.png\" alt=\"long alternative text\">", 0)]
    [InlineData("<p>x<img src=\"https://example.com/a.png\" alt=\"zzz\">y</p>", 2)]
    [InlineData("<a href=\"https://example.com/very/long/link\" title=\"tt\">go</a>", 2)]
    [InlineData("<script>alert(1)</script>ok", 2)]
    // Elements outside the allow-list are removed with their content by the sanitizer, so they are not stored nor counted.
    [InlineData("<div><span>ab</span></div>", 0)]
    public void VisibleTextLength_counts_only_what_the_reader_sees(string html, int expected) =>
        HrMessageContentSanitizer.VisibleTextLength(html).ShouldBe(expected);
}
