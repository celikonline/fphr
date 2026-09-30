namespace Sense.Modules.Notifications.Application;

/// <summary>Application layer error codes (localized in SharedResource).</summary>
public static class NotificationsErrors
{
    public const string NotificationNotFound = "notifications.notification.not_found";
    public const string UnknownTemplate = "notifications.preference.unknown_template";
    public const string UnknownChannel = "notifications.preference.unknown_channel";
    public const string TemplateNotConfigurable = "notifications.preference.not_configurable";
    public const string InvalidPushToken = "notifications.device.invalid_token";
    public const string InvalidPushPlatform = "notifications.device.invalid_platform";

    public const string SmsChannelUnavailable = "notifications.message.sms_unavailable";
    public const string UnknownMessageChannel = "notifications.message.unknown_channel";
    public const string RecipientsRequired = "notifications.message.recipients_required";
    public const string GroupNotFound = "notifications.messagegroup.not_found";
    public const string GroupNameTaken = "notifications.messagegroup.name_taken";
}
