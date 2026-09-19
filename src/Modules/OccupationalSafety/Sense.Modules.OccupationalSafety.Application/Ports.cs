using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.OccupationalSafety.Application;

/// <summary>Occupational Safety module's UnitOfWork surface for the command pipeline; implemented by OhsDbContext.</summary>
public interface IOhsUnitOfWork : IUnitOfWork;
