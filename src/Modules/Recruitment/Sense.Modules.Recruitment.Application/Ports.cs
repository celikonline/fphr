using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Recruitment.Application;

/// <summary>Recruitment's SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface IRecruitmentUnitOfWork : IUnitOfWork;
