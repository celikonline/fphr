namespace Sense.Shared.Contracts.Security;

/// <summary>
/// Employee modülünün Identity'ye sunduğu kapsam kontratı: bir çalışanın Team/Department/Location kapsamındaki çalışan kimlikleri.
/// Employee modülü yüklü değilse kayıt yoktur; DataScopeService Self'e düşer.
/// </summary>
public interface IEmployeeScopeLookup
{
    Task<IReadOnlySet<Guid>> GetEmployeeIdsInScopeAsync(Guid employeeId, DataScope scope, CancellationToken cancellationToken);
}
