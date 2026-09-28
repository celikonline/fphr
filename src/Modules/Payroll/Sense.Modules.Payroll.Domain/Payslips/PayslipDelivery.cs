using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// TK-030 kabul kriteri 2: e-pusula tebliğ kanıtı (11.3.07-bordro.md "E-pusula tebliğ kanıtı",
/// <c>PayslipDelivery{Channel, DeliveredAt, ViewedAt, AcknowledgedAt, EvidenceHash}</c>). Bu dilim yalnız
/// çalışanın kendi görüntüleme (<see cref="MarkViewed"/>) ve "Okudum, onaylıyorum" (<see cref="Acknowledge"/>)
/// akışını kapsar - <c>DeliveredAt</c>/<c>SignatureRequestId</c> (KEP/OTP, hatırlatma job'ı) kapsam dışı, hukuki
/// karar insanın (bkz. docs/takim/handoff/TK-030.md). Bir pusula (<see cref="PayslipId"/>) tek bir tebliğ kaydı taşır.
/// </summary>
public sealed class PayslipDelivery : TenantEntity<Guid>
{
    private PayslipDelivery()
    {
    }

    private PayslipDelivery(Guid id, Guid tenantId, Guid payslipId, Guid employeeId, PayslipDeliveryChannel channel)
        : base(id, tenantId)
    {
        PayslipId = payslipId;
        EmployeeId = employeeId;
        Channel = channel;
    }

    public Guid PayslipId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public PayslipDeliveryChannel Channel { get; private set; }

    /// <summary>Onay anındaki <see cref="Payslip.Version"/> - hangi hesaplama sürümünün onaylandığını sabitler.</summary>
    public int PayslipVersion { get; private set; }

    /// <summary>Çalışanın pusula PDF'ini ilk indirdiği an; sonraki indirmeler değiştirmez.</summary>
    public DateTimeOffset? ViewedAt { get; private set; }

    /// <summary>"Okudum, onaylıyorum" tıklamasının anı; idempotent - ikinci çağrı ilk değeri değiştirmez.</summary>
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    /// <summary>Onay anındaki <c>Payslip.InputHash</c> + <see cref="PayslipVersion"/>'dan SHA-256 (PII yok,
    /// bkz. <see cref="PayslipEvidenceHash"/>) - hangi içeriğin onaylandığının kanıtı.</summary>
    public string? EvidenceHash { get; private set; }

    public static PayslipDelivery Create(Guid tenantId, Guid payslipId, Guid employeeId, PayslipDeliveryChannel channel)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(payslipId);
        Guard.NotDefault(employeeId);
        return new PayslipDelivery(Guid.NewGuid(), tenantId, payslipId, employeeId, channel);
    }

    public void MarkViewed(DateTimeOffset at) => ViewedAt ??= at;

    /// <summary>İdempotent: zaten onaylanmışsa ilk <see cref="AcknowledgedAt"/>/<see cref="PayslipVersion"/>/
    /// <see cref="EvidenceHash"/>/<see cref="Channel"/> korunur - kanıt kaydı sonradan üzerine yazılmaz.</summary>
    public void Acknowledge(DateTimeOffset at, PayslipDeliveryChannel channel, int payslipVersion, string evidenceHash)
    {
        if (AcknowledgedAt is not null)
        {
            return;
        }

        AcknowledgedAt = at;
        Channel = channel;
        PayslipVersion = payslipVersion;
        EvidenceHash = Guard.NotEmpty(evidenceHash);
    }
}
