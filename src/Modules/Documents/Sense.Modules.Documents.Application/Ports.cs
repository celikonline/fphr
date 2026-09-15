using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Documents.Application;

/// <summary>Documents DbContext's unit of work, for the query-triggered catalog seeding and integration event handlers that run outside the command pipeline (mirrors Leave's ILeaveUnitOfWork).</summary>
public interface IDocumentsUnitOfWork : IUnitOfWork;
