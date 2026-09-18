using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain.Support;

/// <summary>
/// "Desteğe bağlan" talebi (11.3.13, roadmap #69). Kiracı düzeyi; platform tarafı (Sense destek ekibi) Faz 8
/// platform konsolundan kuyruğu okuyacak — bu paket yalnız oluşturma/kendi taleplerini görme/yanıtlama akışını kurar.
/// </summary>
public sealed class SupportTicket : TenantAggregateRoot<Guid>, IAuditLogged
{
    // ReSharper disable once UnusedMember.Local
    private SupportTicket() { }

    public Guid RequestedByUserId { get; private set; }
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public SupportTicketCategory Category { get; private set; }
    public SupportTicketChannel Channel { get; private set; }
    public SupportTicketStatus Status { get; private set; }

    /// <summary>Talep açıldığı sayfa/rota (tanı bağlamı) — ör. "/app/leave/requests".</summary>
    public string? SourceRoute { get; private set; }

    /// <summary>Son başarısız isteğin correlation id'si varsa (destek ekibi loglarda arar).</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Dış sağlayıcıya (ör. e-posta thread id, ileride Zendesk/Freshdesk) referans.</summary>
    public string? ExternalRef { get; private set; }

    public string? Resolution { get; private set; }
    public DateTime? AnsweredAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public static Result<SupportTicket> Create(
        Guid tenantId,
        Guid requestedByUserId,
        string subject,
        string body,
        SupportTicketCategory category,
        string? sourceRoute = null,
        string? correlationId = null,
        SupportTicketChannel channel = SupportTicketChannel.Ticket)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > AnnouncementsLimits.SubjectMaxLength)
        {
            return Error.Validation(SupportDomainErrors.SubjectRequired);
        }

        if (string.IsNullOrWhiteSpace(body) || body.Length > AnnouncementsLimits.BodyMaxLength)
        {
            return Error.Validation(SupportDomainErrors.BodyRequired);
        }

        return new SupportTicket
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestedByUserId = requestedByUserId,
            Subject = subject.Trim(),
            Body = body.Trim(),
            Category = category,
            Channel = channel,
            Status = SupportTicketStatus.Open,
            SourceRoute = sourceRoute,
            CorrelationId = correlationId,
        };
    }

    /// <summary>Destek ekibi yanıtladı: talep "Answered" olur, çalışan görürse tekrar yazabilir (yeniden Open'a dönmez — basit v1 akışı).</summary>
    public Result Respond(string resolution)
    {
        if (Status == SupportTicketStatus.Closed)
        {
            return Error.Conflict(SupportDomainErrors.AlreadyClosed);
        }

        if (string.IsNullOrWhiteSpace(resolution) || resolution.Length > AnnouncementsLimits.ResolutionMaxLength)
        {
            return Error.Validation(SupportDomainErrors.ResolutionRequired);
        }

        Resolution = resolution.Trim();
        Status = SupportTicketStatus.Answered;
        AnsweredAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Close()
    {
        if (Status == SupportTicketStatus.Closed)
        {
            return Error.Conflict(SupportDomainErrors.AlreadyClosed);
        }

        Status = SupportTicketStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public void AttachExternalRef(string externalRef) => ExternalRef = externalRef;
}
