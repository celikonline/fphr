namespace Sense.Modules.Notifications.Domain;

/// <summary>Column/length limits shared by the domain guards and the EF configuration.</summary>
public static class NotificationsLimits
{
    public const int TemplateKeyMaxLength = 100;
    public const int ChannelMaxLength = 20;
    public const int TitleMaxLength = 300;
    public const int BodyMaxLength = 2000;
    public const int ActionUrlMaxLength = 500;
    public const int AddressMaxLength = 320;
    public const int RecipientNameMaxLength = 200;
    public const int AddressSourceMaxLength = 30;
    public const int CultureMaxLength = 10;
    public const int ErrorMaxLength = 2000;
    public const int StatusMaxLength = 20;
    public const int PushTokenMaxLength = 255;
    public const int PushPlatformMaxLength = 20;
    public const int DeviceNameMaxLength = 100;

    public const int HrMessageSubjectMaxLength = 300;
    public const int HrMessageBodyMaxLength = 4096;
    public const int HrMessageChannelsMaxLength = 60;
    public const int MessageGroupNameMaxLength = 200;
}
