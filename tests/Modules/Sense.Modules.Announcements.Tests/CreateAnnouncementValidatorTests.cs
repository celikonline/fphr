using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>Regresyon: <c>type:99, priority:99</c> gibi tanımsız enum değerleri duyuru oluştururken kabul ediliyordu.</summary>
public class CreateAnnouncementValidatorTests
{
    private static CreateAnnouncementCommand Command(AnnouncementType type = AnnouncementType.General, AnnouncementPriority priority = AnnouncementPriority.Normal, string? audience = null) =>
        new("Başlık", "İçerik", type, priority, audience);

    [Fact]
    public void ValidCommand_Passes() =>
        Assert.True(new CreateAnnouncementValidator().Validate(Command()).IsValid);

    [Fact]
    public void UndefinedType_IsRejected() =>
        Assert.False(new CreateAnnouncementValidator().Validate(Command(type: (AnnouncementType)99)).IsValid);

    [Fact]
    public void UndefinedPriority_IsRejected() =>
        Assert.False(new CreateAnnouncementValidator().Validate(Command(priority: (AnnouncementPriority)99)).IsValid);

    [Fact]
    public void TargetAudienceLongerThanColumn_IsRejected() =>
        Assert.False(new CreateAnnouncementValidator().Validate(Command(audience: new string('x', AnnouncementsLimits.TargetAudienceMaxLength + 1))).IsValid);
}
