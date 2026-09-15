using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Payroll.Application;

/// <summary>Payroll DbContext's unit of work, for handlers/services that run outside the command pipeline.</summary>
public interface IPayrollUnitOfWork : IUnitOfWork;
