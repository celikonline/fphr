using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Identity.Contracts;

/// <summary>
/// Identity'nin outbox'ından yayınlanır: bir süper admin yeni bir şirket (kiracı) oluşturduğunda. Diğer modüller kiracıya
/// özel varsayılanları (Organization: birim tipleri, kök şirket birimi, TR resmi tatil takvimi) bu olayla hazırlar.
/// Tüketiciler idempotent olmalıdır (outbox teslimatı at-least-once).
/// </summary>
public sealed record TenantProvisioned(Guid TenantId, string TenantName) : IntegrationEvent(TenantId);
