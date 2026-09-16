using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Assets.Application;

/// <summary>Assets module's UnitOfWork surface for the command pipeline; implemented by AssetsDbContext.</summary>
public interface IAssetsUnitOfWork : IUnitOfWork;
