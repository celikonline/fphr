namespace Sense.Modules.Identity.Contracts;

/// <summary>
/// Diğer modüllerin (ör. Workflow, yönetici/politika bulunamayan onay taleplerini bir rol kuyruğuna düşürürken)
/// bir çalışanın belirli bir sistem rolünü taşıyıp taşımadığını veya bir rolü taşıyan çalışanları Identity'nin iç
/// katmanlarına bağımlı olmadan okuması. Rol kodu için <c>SystemRoleCodes</c> (ör. <c>SystemRoleCodes.HR</c>) kullanılır.
/// Implemented by Sense.Modules.Identity.Infrastructure ve IdentityModule.AddModule tarafından kaydedilir.
/// </summary>
public interface IRoleAssignmentLookup
{
    /// <summary>Verilen rol koduna sahip, kullanıcı hesabı çalışana bağlı olan çalışan kimlikleri.</summary>
    Task<IReadOnlyList<Guid>> ListEmployeeIdsInRoleAsync(Guid tenantId, string roleCode, CancellationToken ct = default);

    /// <summary>Çalışanın kullanıcı hesabı verilen rol koduna sahip mi (çalışanın hesabı yoksa false).</summary>
    Task<bool> EmployeeHasRoleAsync(Guid tenantId, Guid employeeId, string roleCode, CancellationToken ct = default);
}
