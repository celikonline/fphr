namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Okutma ucu iş kuralları (yapılandırma <see cref="Section"/>; gizli değildir).</summary>
public sealed class TerminalIngestOptions
{
    public const string Section = "Attendance:Terminals";

    /// <summary>Terminal başına, kayan pencerede izin verilen başarısız PIN kalemi sayısı; aşılınca o terminalde PIN girişi reddedilir.</summary>
    public int PinFailureBudget { get; set; } = 20;

    public int PinFailureWindowMinutes { get; set; } = 5;

    /// <summary>Bundan eski okutma kalemleri <c>too_old</c> ile reddedilir (gelecek toleransı recorder'da).</summary>
    public int MaxPunchAgeDays { get; set; } = 45;
}
