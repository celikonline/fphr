using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Onboarding.Application;

/// <summary>Onboarding's SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface IOnboardingUnitOfWork : IUnitOfWork;
