namespace Sense.Shared.Infrastructure.Localization;

/// <summary>E-posta şablon anahtarları ve yer tutucuları.</summary>
public static class EmailTemplateKeys
{
    public const string InvitationSubject = "email.invitation.subject";
    public const string InvitationBody = "email.invitation.body";
    public const string PasswordResetSubject = "email.password_reset.subject";
    public const string PasswordResetBody = "email.password_reset.body";
    public const string WelcomeSubject = "email.welcome.subject";
    public const string WelcomeBody = "email.welcome.body";
    public const string ReportReadySubject = "email.report_ready.subject";
    public const string ReportReadyBody = "email.report_ready.body";

    public static class Placeholders
    {
        public const string Name = "{name}";
        public const string Tenant = "{tenant}";
        public const string Url = "{url}";
        public const string Expires = "{expires}";
    }
}
