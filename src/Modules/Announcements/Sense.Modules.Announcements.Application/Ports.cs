using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Announcements.Application;

/// <summary>Announcements' SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface IAnnouncementsUnitOfWork : IUnitOfWork;
